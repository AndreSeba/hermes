"""Prueba de punta a punta de M5 (stock) contra la API real y Postgres.

Cubre lo que las pruebas unitarias (SQLite) no pueden: el orden por urgencia y el cursor compuesto en SQL
real, los totales sumados en la base y las restricciones. La concurrencia (ventas simultáneas) la cubre la
prueba xUnit con HERMES_TEST_PG y por scripts/e2e_ventas.py (peticiones HTTP simultáneas).
Necesita la API corriendo (perfil http, puerto 5013). Solo para desarrollo. Crea cuentas de prueba en hermes_dev.
Uso: python -I scripts/e2e_stock.py
"""
import json, random, sys, urllib.request, urllib.error

sys.stdout.reconfigure(encoding="utf-8")
BASE = "http://localhost:5013"
fallas = 0


def ok(desc, cond):
    global fallas
    print(("OK    " if cond else "FALLA ") + desc)
    if not cond:
        fallas += 1


def api(metodo, ruta, obj=None, token=None):
    h = {"Content-Type": "application/json"} if obj is not None else {}
    if token:
        h["Authorization"] = "Bearer " + token
    req = urllib.request.Request(BASE + ruta, json.dumps(obj).encode() if obj is not None else None, h, method=metodo)
    try:
        r = urllib.request.urlopen(req)
        b = r.read()
        return r.status, (json.loads(b) if b else None)
    except urllib.error.HTTPError as e:
        b = e.read()
        return e.code, (json.loads(b) if b and b[:1] in b"{[" else None)


def producto(nombre, precio, costo, colores=None, controla=True, minimo=None):
    cuerpo = {"nombre": nombre, "descripcion": None, "precio": precio, "costo": costo, "categoriaId": None,
              "visibleEnTienda": True, "colores": colores, "tallas": None, "fotoIds": None, "controlaStock": controla}
    if minimo is not None:
        cuerpo["stockMinimo"] = minimo
    return cuerpo


n = random.randint(10000, 99999)
_, ses = api("POST", "/api/v1/auth/registro", {"nombre": "Stock", "email": f"stock{n}@ejemplo.com", "password": "secreta123", "nombreTienda": f"Stock {n}", "slug": f"stock-{n}"})
t = ses["accessToken"]
_, ses_b = api("POST", "/api/v1/auth/registro", {"nombre": "Otra", "email": f"oso{n}@ejemplo.com", "password": "secreta123", "nombreTienda": f"Oso {n}", "slug": f"oso-{n}"})
tb = ses_b["accessToken"]

# 1. Productos: con y sin control
ok("sin sesión no se ve el stock (401)", api("GET", "/api/v1/stock")[0] == 401)
_, cartera = api("POST", "/api/v1/productos", producto("Cartera de cuero Mía", 180, 110, ["café", "negro"]), t)
_, aretes = api("POST", "/api/v1/productos", producto("Aretes de plata Killa", 95, 38), t)
_, bolso = api("POST", "/api/v1/productos", producto("Bolso con nombre bordado", 220, 120, controla=False), t)
_, collar = api("POST", "/api/v1/productos", producto("Collar Salar turquesa", 120, 58), t)
ok("por defecto un producto lleva control y avisa con 3", cartera["controlaStock"] is True and cartera["stockMinimo"] == 3)
ok("se puede crear por encargo (sin control)", bolso["controlaStock"] is False)

s, lista = api("GET", "/api/v1/stock", token=t)
estados = [(i["nombre"], i["estado"], i["stock"]) for i in lista["items"]]
ok("recién creados: los 3 con control figuran Agotado (stock 0) y el por encargo al final sin número",
   [e[1] for e in estados] == ["Agotado", "Agotado", "Agotado", "SinControl"] and estados[-1][2] is None and all(e[2] == 0 for e in estados[:3]))

# 2. Llegaron más
cafe, negro = cartera["variantes"][0]["id"], cartera["variantes"][1]["id"]
s, fila = api("POST", "/api/v1/stock/ingresos", {"productoId": cartera["id"], "lineas": [{"varianteId": cafe, "cantidad": 4}, {"varianteId": negro, "cantidad": 2}], "costoUnitario": 95.5, "nota": "Pedido de octubre"}, t)
ok("llegaron 4 café y 2 negro: stock 6 y estado Ok", s == 200 and fila["stock"] == 6 and fila["estado"] == "Ok" and [v["stock"] for v in fila["variantes"]] == [4, 2])
ok("el costo del producto pasó a 95,50", api("GET", f"/api/v1/productos/{cartera['id']}", token=t)[1]["costo"] == 95.5)
s, fila = api("POST", "/api/v1/stock/ingresos", {"productoId": aretes["id"], "lineas": [{"varianteId": aretes["variantes"][0]["id"], "cantidad": 2}]}, t)
ok("aretes: 2 unidades = por agotarse", s == 200 and fila["estado"] == "Pocos")

# 3. Orden por urgencia, vistas y resumen
_, lista = api("GET", "/api/v1/stock", token=t)
ok("orden: agotado, por agotarse, bien, por encargo", [i["estado"] for i in lista["items"]] == ["Agotado", "Pocos", "Ok", "SinControl"])
_, rep = api("GET", "/api/v1/stock?Vista=AReponer", token=t)
ok("vista 'A reponer' = agotados y por agotarse", [i["nombre"] for i in rep["items"]] == ["Collar Salar turquesa", "Aretes de plata Killa"])
_, enc = api("GET", "/api/v1/stock?Vista=PorEncargo", token=t)
ok("vista 'Por encargo'", [i["nombre"] for i in enc["items"]] == ["Bolso con nombre bordado"])
_, bus = api("GET", "/api/v1/stock?Buscar=ARETES", token=t)
ok("buscar ignora mayúsculas", [i["nombre"] for i in bus["items"]] == ["Aretes de plata Killa"])
_, r = api("GET", "/api/v1/stock/resumen", token=t)
ok(f"resumen: costo {r['valorCosto']} y venta {r['valorVenta']} (6 × 95,50 + 2 × 38 = 649; 6 × 180 + 2 × 95 = 1270)",
   r["valorCosto"] == 649 and r["valorVenta"] == 1270 and (r["agotados"], r["porAgotarse"], r["sinControl"]) == (1, 1, 1))

# 4. Corregir
aretes_v = aretes["variantes"][0]["id"]
s, fila = api("POST", "/api/v1/stock/ajustes", {"productoId": aretes["id"], "lineas": [{"varianteId": aretes_v, "nuevoTotal": 5}], "motivo": "Encontré más en el cajón"}, t)
ok("corregir a 5: queda Ok", s == 200 and fila["stock"] == 5 and fila["estado"] == "Ok")
_, hist = api("GET", f"/api/v1/stock/{aretes['id']}/movimientos", token=t)
ult = hist["items"][0]
ok("el historial guarda la diferencia, el saldo y el motivo", (ult["tipo"], ult["cantidad"], ult["saldo"], ult["nota"]) == ("Ajuste", 3, 5, "Encontré más en el cajón"))
ok("y debajo, la llegada original", hist["items"][1]["tipo"] == "Ingreso" and hist["items"][1]["cantidad"] == 2)
s, r = api("POST", "/api/v1/stock/ajustes", {"productoId": aretes["id"], "lineas": [{"varianteId": aretes_v, "nuevoTotal": 5}]}, t)
ok("corregir a la misma cantidad → 400 'No cambió'", s == 400 and "No cambió" in r["detail"])

# 5. Validaciones
for desc, cuerpo in [("cantidad negativa", {"productoId": aretes["id"], "lineas": [{"varianteId": aretes_v, "cantidad": -1}]}),
                     ("cantidad 0", {"productoId": aretes["id"], "lineas": [{"varianteId": aretes_v, "cantidad": 0}]}),
                     ("costo con 3 decimales", {"productoId": aretes["id"], "lineas": [{"varianteId": aretes_v, "cantidad": 1}], "costoUnitario": 10.555})]:
    s, r = api("POST", "/api/v1/stock/ingresos", cuerpo, t)
    ok(f"llegaron más con {desc} → 400", s == 400 and r["detail"])
s, r = api("POST", "/api/v1/stock/ajustes", {"productoId": aretes["id"], "lineas": [{"varianteId": aretes_v, "nuevoTotal": -2}]}, t)
ok("corregir a un total negativo → 400", s == 400)
s, r = api("POST", "/api/v1/stock/ingresos", {"productoId": bolso["id"], "lineas": [{"varianteId": bolso["variantes"][0]["id"], "cantidad": 3}]}, t)
ok("llegaron más a un producto por encargo → 409 con el motivo", s == 409 and "no lleva control" in r["detail"])
s, r = api("POST", "/api/v1/stock/ingresos", {"productoId": cartera["id"], "lineas": [{"varianteId": aretes_v, "cantidad": 3}]}, t)
ok("la variante de otro producto → 404", s == 404)

# 6. Catálogo, apagar y encender el control
_, cat = api("GET", "/api/v1/productos?Buscar=cartera", token=t)
ok("el catálogo muestra el stock de la cartera (6) y su estado", cat["items"][0]["stock"] == 6 and cat["items"][0]["estadoStock"] == "Ok")
_, cat = api("GET", "/api/v1/productos?Buscar=bolso", token=t)
ok("el producto por encargo no tiene stock en el catálogo", cat["items"][0]["stock"] is None and cat["items"][0]["estadoStock"] == "SinControl")
s, p = api("PUT", f"/api/v1/productos/{cartera['id']}", producto("Cartera de cuero Mía", 180, 95.5, ["café", "negro"], controla=False), t)
ok("apagar el control", s == 200 and p["controlaStock"] is False)
ok("…y el stock deja de verse", api("GET", "/api/v1/productos?Buscar=cartera", token=t)[1]["items"][0]["stock"] is None)
s, p = api("PUT", f"/api/v1/productos/{cartera['id']}", producto("Cartera de cuero Mía", 180, 95.5, ["café", "negro"], controla=True), t)
ok("…encenderlo de nuevo retoma la suma (6)", api("GET", "/api/v1/productos?Buscar=cartera", token=t)[1]["items"][0]["stock"] == 6)

# 7. No se saca una variante que todavía tiene stock
s, r = api("PUT", f"/api/v1/productos/{cartera['id']}", producto("Cartera de cuero Mía", 180, 95.5, ["negro"]), t)
ok("sacar 'café' con 4 en stock → 400 'Todavía quedan 4 de café'", s == 400 and "quedan 4 de café" in r["detail"].lower())
ok("…y no se guardó nada", len(api("GET", f"/api/v1/productos/{cartera['id']}", token=t)[1]["variantes"]) == 2)
api("POST", "/api/v1/stock/ajustes", {"productoId": cartera["id"], "lineas": [{"varianteId": cafe, "nuevoTotal": 0}, {"varianteId": negro, "nuevoTotal": 2}], "motivo": "Se la llevó un cliente"}, t)
s, r = api("PUT", f"/api/v1/productos/{cartera['id']}", producto("Cartera de cuero Mía", 180, 95.5, ["negro"]), t)
ok("con el café en 0 sí se puede sacar", s == 200 and [v["color"] for v in r["variantes"]] == ["negro"])
_, fila = api("GET", "/api/v1/stock?Buscar=cartera", token=t)
ok("el stock solo cuenta variantes activas (2 de negro)", fila["items"][0]["stock"] == 2 and fila["items"][0]["estado"] == "Pocos")

# 8. Paginación con cursor compuesto en Postgres
for i in range(1, 11):
    _, p = api("POST", "/api/v1/productos", producto(f"Masivo {i}", 10 + i, 5), t)
    if i % 3 == 0:
        api("POST", "/api/v1/stock/ingresos", {"productoId": p["id"], "lineas": [{"varianteId": p["variantes"][0]["id"], "cantidad": 20}]}, t)
    elif i % 3 == 1:
        api("POST", "/api/v1/stock/ingresos", {"productoId": p["id"], "lineas": [{"varianteId": p["variantes"][0]["id"], "cantidad": 1}]}, t)
_, todo = api("GET", "/api/v1/stock?Limite=60", token=t)
esperado = [i["id"] for i in todo["items"]]
vistos, tamanos, cursor = [], [], None
while True:
    _, pag = api("GET", "/api/v1/stock?Limite=4" + (f"&Cursor={cursor}" if cursor else ""), token=t)
    vistos += [i["id"] for i in pag["items"]]
    tamanos.append(len(pag["items"]))
    cursor = pag["siguiente"]
    if not cursor:
        break
ok(f"páginas de 4 en 4 sobre {len(esperado)} productos: {tamanos}", vistos == esperado and len(set(vistos)) == len(esperado) and tamanos[:-1] == [4] * (len(tamanos) - 1))
prio = {"Agotado": 0, "Pocos": 1, "Ok": 2, "SinControl": 3}
ok("el orden por urgencia nunca retrocede en todo el recorrido", [prio[i["estado"]] for i in todo["items"]] == sorted(prio[i["estado"]] for i in todo["items"]))
ok("un cursor inventado → 400", api("GET", "/api/v1/stock?Cursor=hola", token=t)[0] == 400)

# 9. Historial paginado
for k in range(1, 6):
    api("POST", "/api/v1/stock/ingresos", {"productoId": collar["id"], "lineas": [{"varianteId": collar["variantes"][0]["id"], "cantidad": k}]}, t)
_, h1 = api("GET", f"/api/v1/stock/{collar['id']}/movimientos?Limite=2", token=t)
_, h2 = api("GET", f"/api/v1/stock/{collar['id']}/movimientos?Limite=2&Cursor={h1['siguiente']}", token=t)
_, h3 = api("GET", f"/api/v1/stock/{collar['id']}/movimientos?Limite=2&Cursor={h2['siguiente']}", token=t)
ok("el historial va del más nuevo al más viejo en páginas de 2", [m["cantidad"] for m in h1["items"] + h2["items"] + h3["items"]] == [5, 4, 3, 2, 1] and h3["siguiente"] is None)
ok("el saldo de cada movimiento acompaña la suma", [m["saldo"] for m in h1["items"]] == [15, 10])

# 10. Aislamiento
_, vacio = api("GET", "/api/v1/stock", token=tb)
ok("otra tienda ve el stock vacío", vacio["items"] == [])
ok("otra tienda no ve el historial (404)", api("GET", f"/api/v1/stock/{collar['id']}/movimientos", token=tb)[0] == 404)
ok("otra tienda no suma stock (404)", api("POST", "/api/v1/stock/ingresos", {"productoId": collar["id"], "lineas": [{"varianteId": collar["variantes"][0]["id"], "cantidad": 99}]}, tb)[0] == 404)
ok("otra tienda no corrige (404)", api("POST", "/api/v1/stock/ajustes", {"productoId": collar["id"], "lineas": [{"varianteId": collar["variantes"][0]["id"], "nuevoTotal": 0}]}, tb)[0] == 404)
ok("el stock de la dueña no cambió (15)", api("GET", "/api/v1/stock?Buscar=collar", token=t)[1]["items"][0]["stock"] == 15)

print(f"--- fallas: {fallas}")
sys.exit(1 if fallas else 0)
