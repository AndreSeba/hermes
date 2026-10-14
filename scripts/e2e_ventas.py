"""Prueba de punta a punta de M6 (ventas) contra la API real y Postgres.

Cubre lo que las pruebas unitarias (SQLite) no pueden: peticiones HTTP simultáneas de verdad (la última
unidad vendida desde 12 celulares a la vez, el mismo reintento 10 veces, anulaciones en carrera), los
códigos de estado (201/200/409/404/400/401) y el aislamiento entre tiendas con tokens distintos.
Necesita la API corriendo (perfil http, puerto 5013). Solo para desarrollo. Crea cuentas de prueba en hermes_dev.
Uso: python -I scripts/e2e_ventas.py
"""
import json, random, sys, uuid, urllib.request, urllib.error
from concurrent.futures import ThreadPoolExecutor

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


def producto(nombre, precio, costo, colores=None, controla=True):
    return {"nombre": nombre, "descripcion": None, "precio": precio, "costo": costo, "categoriaId": None,
            "visibleEnTienda": True, "colores": colores, "tallas": None, "fotoIds": None, "controlaStock": controla}


def vende(variante, cantidad, pago="Efectivo", cliente=None, clave=None):
    return {"claveIdempotencia": clave or str(uuid.uuid4()), "lineas": [{"varianteId": variante, "cantidad": cantidad}],
            "metodoPago": pago, "cliente": cliente}


def stock_de(token, producto_id):
    return api("GET", f"/api/v1/stock/{producto_id}", token=token)[1]["stock"]


n = random.randint(10000, 99999)
_, ses = api("POST", "/api/v1/auth/registro", {"nombre": "Ventas", "email": f"ventas{n}@ejemplo.com", "password": "secreta123", "nombreTienda": f"Ventas {n}", "slug": f"ventas-{n}"})
t = ses["accessToken"]
_, ses_b = api("POST", "/api/v1/auth/registro", {"nombre": "Otra", "email": f"otra{n}@ejemplo.com", "password": "secreta123", "nombreTienda": f"Otra {n}", "slug": f"otra-{n}"})
tb = ses_b["accessToken"]

# 1. Preparar: tres productos (con colores, sin costo, por encargo) y stock
ok("sin sesión no se vende (401)", api("POST", "/api/v1/ventas", vende(str(uuid.uuid4()), 1))[0] == 401)
_, cartera = api("POST", "/api/v1/productos", producto("Cartera de cuero Mía", 180, 110, ["café", "negro"]), t)
_, aretes = api("POST", "/api/v1/productos", producto("Aretes de plata Killa", 40, 0), t)
_, bolso = api("POST", "/api/v1/productos", producto("Bolso por encargo", 220, 120, controla=False), t)
cafe, negro = cartera["variantes"][0]["id"], cartera["variantes"][1]["id"]
v_aretes, v_bolso = aretes["variantes"][0]["id"], bolso["variantes"][0]["id"]
api("POST", "/api/v1/stock/ingresos", {"productoId": cartera["id"], "lineas": [{"varianteId": cafe, "cantidad": 5}, {"varianteId": negro, "cantidad": 3}]}, t)
api("POST", "/api/v1/stock/ingresos", {"productoId": aretes["id"], "lineas": [{"varianteId": v_aretes, "cantidad": 10}]}, t)

# 2. Qué se ofrece para vender
s, vend = api("GET", "/api/v1/ventas/productos", token=t)
nombres = [i["nombre"] for i in vend["items"]]
ok("se ofrecen los 3 productos, el más nuevo primero", s == 200 and nombres == ["Bolso por encargo", "Aretes de plata Killa", "Cartera de cuero Mía"])
c = next(i for i in vend["items"] if i["nombre"] == "Cartera de cuero Mía")
ok("la cartera trae sus variantes con lo que queda (café 5, negro 3)", c["stock"] == 8 and [(v["color"], v["stock"]) for v in c["variantes"]] == [("café", 5), ("negro", 3)])
ok("el producto por encargo no trae número de stock", next(i for i in vend["items"] if i["nombre"] == "Bolso por encargo")["stock"] is None)

# 3. Registrar una venta
s, v = api("POST", "/api/v1/ventas", vende(cafe, 2, "Qr", "  Ana  "), t)
ok("registrar responde 201 con el total y la ganancia (2 × 180; 2 × 70)", s == 201 and v["total"] == 360 and v["ganancia"] == 140 and v["unidades"] == 2)
ok("el cliente se guarda sin espacios y el método se ve como texto", v["cliente"] == "Ana" and v["metodoPago"] == "Qr")
ok("la línea guarda nombre, precio y costo del momento", v["lineas"][0]["descripcion"] == "Cartera de cuero Mía (café)" and v["lineas"][0]["precioUnitario"] == 180 and v["lineas"][0]["costoUnitario"] == 110)
ok("el stock de la cartera bajó a 6", stock_de(t, cartera["id"]) == 6)
v1 = v

# 4. Cambiar el producto no cambia la venta ya hecha
api("PUT", f"/api/v1/productos/{cartera['id']}", {**producto("Cartera renombrada", 999, 500, ["café", "negro"])}, t)
s, v = api("GET", f"/api/v1/ventas/{v1['id']}", token=t)
ok("la venta conserva el nombre y el precio de cuando se hizo", s == 200 and v["lineas"][0]["descripcion"] == "Cartera de cuero Mía (café)" and v["total"] == 360)

# 5. Sin costo / por encargo
s, v = api("POST", "/api/v1/ventas", vende(v_aretes, 3), t)
ok("sin costo cargado no se inventa la ganancia (null)", s == 201 and v["total"] == 120 and v["ganancia"] is None)
s, v = api("POST", "/api/v1/ventas", vende(v_bolso, 4), t)
ok("el producto por encargo se vende sin tocar stock", s == 201 and v["total"] == 880 and api("GET", f"/api/v1/stock/{bolso['id']}", token=t)[1]["stock"] is None)

# 6. Validaciones y rechazos (todo antes de guardar)
ok("cantidad 0 → 400", api("POST", "/api/v1/ventas", vende(cafe, 0), t)[0] == 400)
ok("sin líneas → 400", api("POST", "/api/v1/ventas", {"claveIdempotencia": str(uuid.uuid4()), "lineas": [], "metodoPago": "Efectivo"}, t)[0] == 400)
ok("método de pago inventado → 400", api("POST", "/api/v1/ventas", {**vende(cafe, 1), "metodoPago": "Cheque"}, t)[0] == 400)
ok("cantidad escrita como texto → 400 (JSON estricto)", api("POST", "/api/v1/ventas", {**vende(cafe, 1), "lineas": [{"varianteId": cafe, "cantidad": "2"}]}, t)[0] == 400)
ok("variante inexistente → 404", api("POST", "/api/v1/ventas", vende(str(uuid.uuid4()), 1), t)[0] == 404)
s, e = api("POST", "/api/v1/ventas", {"claveIdempotencia": str(uuid.uuid4()), "metodoPago": "Efectivo",
                                         "lineas": [{"varianteId": cafe, "cantidad": 2}, {"varianteId": negro, "cantidad": 9}]}, t)
ok("vender más de lo que hay → 409 con el motivo", s == 409 and "hay 3 y se necesitan 9" in e["detail"])
ok("y no se descontó NADA (ni la línea que sí alcanzaba)", stock_de(t, cartera["id"]) == 6)
ok("otra tienda no puede vender mis productos → 404", api("POST", "/api/v1/ventas", vende(cafe, 1), tb)[0] == 404)

# 7. Idempotencia
clave = str(uuid.uuid4())
s1, a = api("POST", "/api/v1/ventas", vende(v_aretes, 1, clave=clave), t)
s2, b = api("POST", "/api/v1/ventas", vende(v_aretes, 1, clave=clave), t)
ok("el reintento con la misma clave: 201 y luego 200 con la misma venta", (s1, s2) == (201, 200) and a["id"] == b["id"])
ok("el reintento no volvió a descontar (aretes 10 − 3 − 1 = 6)", stock_de(t, aretes["id"]) == 6)

# 8. Concurrencia real por HTTP: 12 celulares venden a la vez las últimas 5 unidades
_, rojo = api("POST", "/api/v1/productos", producto("Pulsera última", 50, 20), t)
v_rojo = rojo["variantes"][0]["id"]
api("POST", "/api/v1/stock/ingresos", {"productoId": rojo["id"], "lineas": [{"varianteId": v_rojo, "cantidad": 5}]}, t)
with ThreadPoolExecutor(12) as pool:
    estados = list(pool.map(lambda _: api("POST", "/api/v1/ventas", vende(v_rojo, 1), t)[0], range(12)))
ok(f"12 ventas simultáneas de la última unidad: pasan 5 y 7 reciben 409 ({estados.count(201)}/{estados.count(409)})", estados.count(201) == 5 and estados.count(409) == 7)
ok("el stock quedó en 0, no negativo", stock_de(t, rojo["id"]) == 0)

clave = str(uuid.uuid4())
api("POST", "/api/v1/stock/ingresos", {"productoId": rojo["id"], "lineas": [{"varianteId": v_rojo, "cantidad": 20}]}, t)
with ThreadPoolExecutor(10) as pool:
    resp = list(pool.map(lambda _: api("POST", "/api/v1/ventas", vende(v_rojo, 3, clave=clave), t), range(10)))
ids = {r[1]["id"] for r in resp}
ok(f"10 reintentos simultáneos con la misma clave: una sola venta ({sorted(r[0] for r in resp)[:1]}…, {len(ids)} id)", len(ids) == 1 and all(r[0] in (200, 201) for r in resp) and sum(r[0] == 201 for r in resp) == 1)
ok("y se descontó una sola vez (20 − 3 = 17)", stock_de(t, rojo["id"]) == 17)

# 9. Anular
s, v = api("POST", "/api/v1/ventas", vende(v_rojo, 4), t)
antes = stock_de(t, rojo["id"])
with ThreadPoolExecutor(5) as pool:
    estados = list(pool.map(lambda _: api("POST", f"/api/v1/ventas/{v['id']}/anular", token=t)[0], range(5)))
ok(f"5 anulaciones simultáneas: una gana, 4 reciben 409 ({estados.count(200)}/{estados.count(409)})", estados.count(200) == 1 and estados.count(409) == 4)
ok("el stock volvió una sola vez", stock_de(t, rojo["id"]) == antes + 4)
ok("la otra tienda no puede ver ni anular mi venta → 404", api("GET", f"/api/v1/ventas/{v1['id']}", token=tb)[0] == 404 and api("POST", f"/api/v1/ventas/{v1['id']}/anular", token=tb)[0] == 404)
ok("anular una venta inexistente → 404", api("POST", f"/api/v1/ventas/{uuid.uuid4()}/anular", token=t)[0] == 404)

# 10. Listado, filtros y resumen
s, pag = api("GET", "/api/v1/ventas?Limite=3", token=t)
ok("el listado pagina por cursor (3 + siguiente)", s == 200 and len(pag["items"]) == 3 and pag["siguiente"] is not None)
vistos, cursor = [], None
while True:
    s, pag = api("GET", "/api/v1/ventas?Limite=4" + (f"&Cursor={cursor}" if cursor else ""), token=t)
    vistos += [i["id"] for i in pag["items"]]
    cursor = pag["siguiente"]
    if not cursor:
        break
# Vigentes: cartera, aretes ×3, bolso, aretes (reintento), 5 de la pulsera, 1 con clave repetida = 10 (la anulada no sale).
ok(f"recorrer todas las páginas no repite ni salta ({len(vistos)} ventas vigentes)", len(vistos) == len(set(vistos)) == 10)
s, todas = api("GET", "/api/v1/ventas?IncluirAnuladas=true&Limite=100", token=t)
ok("con IncluirAnuladas aparece la anulada", any(i["anulada"] for i in todas["items"]) and len(todas["items"]) == len(vistos) + 1)
s, res = api("GET", "/api/v1/ventas/resumen", token=t)
ok("el resumen no cuenta la anulada y avisa las unidades sin costo", s == 200 and res["anuladas"] == 1 and res["unidadesSinCosto"] == 3 + 1)  # solo los aretes (3 + 1) no tienen costo
ok("el resumen de la otra tienda está vacío", api("GET", "/api/v1/ventas/resumen", token=tb)[1]["ventas"] == 0)
s, vacio = api("GET", "/api/v1/ventas?Desde=2020-01-01T00:00:00Z&Hasta=2020-01-02T00:00:00Z", token=t)
ok("un rango sin ventas devuelve vacío", s == 200 and vacio["items"] == [])
ok("un cursor inventado → 400", api("GET", "/api/v1/ventas/productos?Cursor=9.xyz", token=t)[0] == 400)

print(f"--- fallas: {fallas}")
sys.exit(1 if fallas else 0)
