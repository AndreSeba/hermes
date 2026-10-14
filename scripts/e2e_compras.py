"""Prueba de punta a punta de M7 (proveedores y compras) contra la API real y Postgres.

Cubre lo que las pruebas unitarias (SQLite) no pueden: peticiones HTTP simultáneas de verdad (recibir la
misma compra 6 veces a la vez, recibir contra anular, el mismo reintento 10 veces), los códigos de estado
(201/200/400/401/404/409), el aviso de margen y el aislamiento entre tiendas con tokens distintos.
Necesita la API corriendo (perfil http, puerto 5013). Solo para desarrollo. Crea cuentas de prueba en hermes_dev.
Uso: python -I scripts/e2e_compras.py
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


def pide(proveedor, variante, cantidad, costo, clave=None, recibida=False, pagada=False):
    return {"claveIdempotencia": clave or str(uuid.uuid4()), "proveedorId": proveedor,
            "lineas": [{"varianteId": variante, "cantidad": cantidad, "costoUnitario": costo}],
            "notas": None, "recibida": recibida, "pagada": pagada}


def stock_de(token, producto_id):
    return api("GET", f"/api/v1/stock/{producto_id}", token=token)[1]["stock"]


def costo_de(token, producto_id):
    return api("GET", f"/api/v1/productos/{producto_id}", token=token)[1]["costo"]


n = random.randint(10000, 99999)
_, ses = api("POST", "/api/v1/auth/registro", {"nombre": "Compras", "email": f"compras{n}@ejemplo.com", "password": "secreta123", "nombreTienda": f"Compras {n}", "slug": f"compras-{n}"})
t = ses["accessToken"]
_, ses_b = api("POST", "/api/v1/auth/registro", {"nombre": "Otra", "email": f"ocra{n}@ejemplo.com", "password": "secreta123", "nombreTienda": f"Ocra {n}", "slug": f"ocra-{n}"})
tb = ses_b["accessToken"]

# 1. Proveedores
ok("sin sesión no se ven los proveedores (401)", api("GET", "/api/v1/proveedores")[0] == 401)
s, beto = api("POST", "/api/v1/proveedores", {"nombre": "  Cueros Don Beto ", "telefono": "712 34567", "notas": "Entrega los viernes"}, t)
ok("crear un proveedor: 201, teléfono normalizado y nombre sin espacios", s == 201 and beto["telefono"] == "+59171234567" and beto["nombre"] == "Cueros Don Beto" and beto["activo"] is True)
ok("el mismo nombre en otras mayúsculas → 409", api("POST", "/api/v1/proveedores", {"nombre": "CUEROS DON BETO"}, t)[0] == 409)
ok("teléfono inválido → 400", api("POST", "/api/v1/proveedores", {"nombre": "Otro", "telefono": "12345"}, t)[0] == 400)
ok("sin nombre → 400", api("POST", "/api/v1/proveedores", {"nombre": " "}, t)[0] == 400)
_, rosa = api("POST", "/api/v1/proveedores", {"nombre": "Hilos Rosa"}, t)
ok("la otra tienda no ve mis proveedores", api("GET", "/api/v1/proveedores", token=tb)[1] == [] and api("GET", f"/api/v1/proveedores/{beto['id']}", token=tb)[0] == 404)
s, ed = api("PUT", f"/api/v1/proveedores/{rosa['id']}", {"nombre": "Hilos Rosa", "telefono": "68888888"}, t)
ok("editar conservando el propio nombre: 200", s == 200 and ed["telefono"] == "+59168888888")

# 2. Productos para comprar
_, cartera = api("POST", "/api/v1/productos", producto("Cartera de cuero Mía", 180, 110, ["café", "negro"]), t)
_, aretes = api("POST", "/api/v1/productos", producto("Aretes de plata Killa", 100, 50), t)
_, material = api("POST", "/api/v1/productos", producto("Material por encargo", 100, 10, controla=False), t)
cafe, negro, v_aretes, v_material = cartera["variantes"][0]["id"], cartera["variantes"][1]["id"], aretes["variantes"][0]["id"], material["variantes"][0]["id"]

# 3. Registrar una compra: queda en camino, sin tocar nada
s, c1 = api("POST", "/api/v1/compras", {"claveIdempotencia": str(uuid.uuid4()), "proveedorId": beto["id"], "notas": " Pedido de octubre ",
            "lineas": [{"varianteId": negro, "cantidad": 4, "costoUnitario": 95.5}, {"varianteId": cafe, "cantidad": 6, "costoUnitario": 95.5}]}, t)
ok("registrar responde 201 en camino, con total y unidades", s == 201 and c1["estado"] == "EnCamino" and c1["pagada"] is False and c1["unidades"] == 10 and c1["total"] == 955)
ok("las líneas salen en el orden de las variantes (café antes que negro)", [l["descripcion"] for l in c1["lineas"]] == ["Cartera de cuero Mía (café)", "Cartera de cuero Mía (negro)"])
ok("todavía no hay stock ni cambió el costo", stock_de(t, cartera["id"]) == 0 and costo_de(t, cartera["id"]) == 110)
s, res = api("GET", "/api/v1/compras/resumen", token=t)
ok("el resumen ya cuenta 955 por pagar y 1 en camino", s == 200 and res["porPagar"] == 955 and res["enCamino"] == 1 and res["valorEnCamino"] == 955)

# 4. Validaciones
ok("sin líneas → 400", api("POST", "/api/v1/compras", {**pide(beto["id"], cafe, 1, 50), "lineas": []}, t)[0] == 400)
ok("cantidad 0 → 400", api("POST", "/api/v1/compras", pide(beto["id"], cafe, 0, 50), t)[0] == 400)
ok("costo 0 → 400", api("POST", "/api/v1/compras", pide(beto["id"], cafe, 1, 0), t)[0] == 400)
ok("costo con 3 decimales → 400", api("POST", "/api/v1/compras", pide(beto["id"], cafe, 1, 10.555), t)[0] == 400)
ok("costo escrito como texto → 400 (JSON estricto)", api("POST", "/api/v1/compras", {**pide(beto["id"], cafe, 1, 50), "lineas": [{"varianteId": cafe, "cantidad": 1, "costoUnitario": "50"}]}, t)[0] == 400)
ok("la misma variante dos veces → 400", api("POST", "/api/v1/compras", {**pide(beto["id"], cafe, 1, 50), "lineas": [{"varianteId": cafe, "cantidad": 1, "costoUnitario": 50}, {"varianteId": cafe, "cantidad": 2, "costoUnitario": 60}]}, t)[0] == 400)
ok("proveedor inexistente → 404", api("POST", "/api/v1/compras", pide(str(uuid.uuid4()), cafe, 1, 50), t)[0] == 404)
ok("variante inexistente → 404", api("POST", "/api/v1/compras", pide(beto["id"], str(uuid.uuid4()), 1, 50), t)[0] == 404)
ok("otra tienda no puede comprar con mis proveedores → 404", api("POST", "/api/v1/compras", pide(beto["id"], cafe, 1, 50), tb)[0] == 404)

# 5. Idempotencia
clave = str(uuid.uuid4())
s1, a = api("POST", "/api/v1/compras", pide(beto["id"], v_aretes, 2, 60, clave), t)
s2, b = api("POST", "/api/v1/compras", pide(beto["id"], v_aretes, 2, 60, clave), t)
ok("el reintento con la misma clave: 201 y luego 200 con la misma compra", (s1, s2) == (201, 200) and a["id"] == b["id"])
api("POST", f"/api/v1/compras/{a['id']}/anular", token=t)

# 6. Recibir: stock, costo y aviso de margen
s, r = api("POST", f"/api/v1/compras/{c1['id']}/recibir", token=t)
ok("recibir: 200, estado Recibida y sin avisos (costo 95,50 contra precio 180)", s == 200 and r["compra"]["estado"] == "Recibida" and r["avisos"] == [])
ok("el stock sumó 10 (6 café + 4 negro)", stock_de(t, cartera["id"]) == 10)
ok("el costo del producto pasó a 95,50", costo_de(t, cartera["id"]) == 95.5)
ok("recibir otra vez → 409 y el stock no se duplicó", api("POST", f"/api/v1/compras/{c1['id']}/recibir", token=t)[0] == 409 and stock_de(t, cartera["id"]) == 10)

s, c2 = api("POST", "/api/v1/compras", {"claveIdempotencia": str(uuid.uuid4()), "proveedorId": rosa["id"],
            "lineas": [{"varianteId": v_aretes, "cantidad": 3, "costoUnitario": 85}, {"varianteId": v_material, "cantidad": 5, "costoUnitario": 120}]}, t)
s, r = api("POST", f"/api/v1/compras/{c2['id']}/recibir", token=t)
avisos = {x["producto"]: x["estado"] for x in r["avisos"]}
ok(f"avisa el margen flojo: aretes Bajo (85 sobre 100) y el material Pierde (120 sobre 100) ({avisos})", avisos == {"Aretes de plata Killa": "Bajo", "Material por encargo": "Pierde"})
ok("el producto por encargo no cuenta unidades pero sí actualiza su costo", stock_de(t, material["id"]) is None and costo_de(t, material["id"]) == 120)

# 7. Pagar
s, p = api("POST", f"/api/v1/compras/{c1['id']}/pagar", token=t)
ok("pagar: 200 con la fecha de pago", s == 200 and p["pagada"] is True and p["pagadaEn"] is not None)
ok("pagar otra vez → 409", api("POST", f"/api/v1/compras/{c1['id']}/pagar", token=t)[0] == 409)
s, pf = api("GET", "/api/v1/compras?Vista=PorPagar", token=t)
ok("«Por pagar» trae solo la compra a Hilos Rosa", s == 200 and [i["id"] for i in pf["items"]] == [c2["id"]])
s, ec = api("GET", "/api/v1/compras?Vista=EnCamino", token=t)
ok("«En camino» está vacío (todo llegó; la anulada no cuenta)", s == 200 and ec["items"] == [])

# 8. Comparador y detalle del proveedor
api("POST", "/api/v1/compras", pide(beto["id"], v_aretes, 1, 70, recibida=True), t)
s, comp = api("GET", f"/api/v1/compras/comparador/{aretes['id']}", token=t)
ok("el comparador pone primero al más barato (Beto 70 contra Rosa 85)", s == 200 and [(x["proveedor"], x["costoUnitario"], x["esElMasBarato"]) for x in comp] == [("Cueros Don Beto", 70, True), ("Hilos Rosa", 85, False)])
ok("el comparador de un producto ajeno → 404", api("GET", f"/api/v1/compras/comparador/{aretes['id']}", token=tb)[0] == 404)
s, det = api("GET", f"/api/v1/proveedores/{beto['id']}", token=t)
ok("el proveedor muestra sus últimos precios (aretes 70, cartera 95,50) sin la compra anulada", s == 200 and sorted((u["producto"], u["costoUnitario"]) for u in det["ultimosPrecios"]) == [("Aretes de plata Killa", 70), ("Cartera de cuero Mía", 95.5)])
s, lista = api("GET", "/api/v1/proveedores", token=t)
rosa_dto = next(p for p in lista if p["id"] == rosa["id"])
ok("Hilos Rosa figura con lo que se le debe (3 × 85 + 5 × 120 = 855)", rosa_dto["porPagar"] == 855 and rosa_dto["enCamino"] == 0)

# 9. Anular
s, an = api("POST", f"/api/v1/compras/{c2['id']}/anular", token=t)
ok("anular una recibida: 200 y resta lo que había sumado (aretes: 3 + 1 − 3 = 1)", s == 200 and an["estado"] == "Anulada" and stock_de(t, aretes["id"]) == 1)
ok("anular otra vez → 409", api("POST", f"/api/v1/compras/{c2['id']}/anular", token=t)[0] == 409)
ok("una anulada no se recibe ni se paga → 409", api("POST", f"/api/v1/compras/{c2['id']}/recibir", token=t)[0] == 409 and api("POST", f"/api/v1/compras/{c2['id']}/pagar", token=t)[0] == 409)
ok("el stock de la cartera sigue en 10 (no se tocó)", stock_de(t, cartera["id"]) == 10)

# Si esas unidades ya se vendieron, no se puede anular
s, v = api("POST", "/api/v1/ventas", {"claveIdempotencia": str(uuid.uuid4()), "lineas": [{"varianteId": cafe, "cantidad": 6}], "metodoPago": "Efectivo"}, t)
s, e = api("POST", f"/api/v1/compras/{c1['id']}/anular", token=t)
ok("anular una compra cuyas unidades ya se vendieron → 409 con el motivo", s == 409 and "No alcanza el stock" in e["detail"])
ok("y no se anuló nada (sigue recibida y el stock es 4)", api("GET", f"/api/v1/compras/{c1['id']}", token=t)[1]["estado"] == "Recibida" and stock_de(t, cartera["id"]) == 4)
ok("la otra tienda no puede ver, recibir ni anular mi compra → 404", all(api("POST" if x else "GET", f"/api/v1/compras/{c1['id']}" + x, token=tb)[0] == 404 for x in ["", "/recibir", "/anular", "/pagar"]))

# 10. Concurrencia real por HTTP
_, pulsera = api("POST", "/api/v1/productos", producto("Pulsera concurrente", 100, 20), t)
v_p = pulsera["variantes"][0]["id"]
_, cc = api("POST", "/api/v1/compras", pide(beto["id"], v_p, 7, 40), t)
with ThreadPoolExecutor(6) as pool:
    estados = list(pool.map(lambda _: api("POST", f"/api/v1/compras/{cc['id']}/recibir", token=t)[0], range(6)))
ok(f"6 recepciones simultáneas: una gana y 5 reciben 409 ({estados.count(200)}/{estados.count(409)})", estados.count(200) == 1 and estados.count(409) == 5)
ok("el stock se sumó una sola vez (7)", stock_de(t, pulsera["id"]) == 7)

clave = str(uuid.uuid4())
with ThreadPoolExecutor(10) as pool:
    resp = list(pool.map(lambda _: api("POST", "/api/v1/compras", pide(beto["id"], v_p, 3, 40, clave, recibida=True), t), range(10)))
ids = {r[1]["id"] for r in resp}
ok(f"10 reintentos simultáneos con la misma clave: una sola compra ({len(ids)} id)", len(ids) == 1 and sum(r[0] == 201 for r in resp) == 1 and all(r[0] in (200, 201) for r in resp))
ok("y sumó el stock una sola vez (7 + 3 = 10)", stock_de(t, pulsera["id"]) == 10)

# Recibir contra anular, 5 vueltas: el resultado final siempre es coherente
incoherentes = 0
for i in range(5):
    _, pr = api("POST", "/api/v1/productos", producto(f"Carrera {i}", 100, 20), t)
    _, cp = api("POST", "/api/v1/compras", pide(beto["id"], pr["variantes"][0]["id"], 4, 40), t)
    with ThreadPoolExecutor(2) as pool:
        f1 = pool.submit(api, "POST", f"/api/v1/compras/{cp['id']}/recibir", None, t)
        f2 = pool.submit(api, "POST", f"/api/v1/compras/{cp['id']}/anular", None, t)
        f1.result(); f2.result()
    estado = api("GET", f"/api/v1/compras/{cp['id']}", token=t)[1]["estado"]
    stock = stock_de(t, pr["id"])
    if stock != (4 if estado == "Recibida" else 0):
        incoherentes += 1
ok(f"recibir contra anular a la vez (5 vueltas): nunca queda stock de una compra anulada ({incoherentes} incoherentes)", incoherentes == 0)

# 11. Listado
vistos, cursor = [], None
while True:
    s, pag = api("GET", "/api/v1/compras?Limite=4" + (f"&Cursor={cursor}" if cursor else ""), token=t)
    vistos += [i["id"] for i in pag["items"]]
    cursor = pag["siguiente"]
    if not cursor:
        break
ok(f"recorrer todas las páginas no repite ni salta ({len(vistos)} compras)", len(vistos) == len(set(vistos)) == 11)
ok("una vista inventada → 400", api("GET", "/api/v1/compras?Vista=Cualquiera", token=t)[0] == 400)
ok("el resumen de la otra tienda está vacío", api("GET", "/api/v1/compras/resumen", token=tb)[1]["porPagar"] == 0)

# 12. Desactivar un proveedor
s, off = api("POST", f"/api/v1/proveedores/{rosa['id']}/desactivar", token=t)
ok("desactivar: 200, desaparece del listado y no recibe pedidos", s == 200 and off["activo"] is False and all(p["id"] != rosa["id"] for p in api("GET", "/api/v1/proveedores", token=t)[1])
   and api("POST", "/api/v1/compras", pide(rosa["id"], cafe, 1, 50), t)[0] == 400)
ok("con incluirInactivos sigue apareciendo (nada se borra)", any(p["id"] == rosa["id"] for p in api("GET", "/api/v1/proveedores?incluirInactivos=true", token=t)[1]))

print(f"--- fallas: {fallas}")
sys.exit(1 if fallas else 0)
