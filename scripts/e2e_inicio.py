"""Prueba de punta a punta de M9 (gastos e Inicio) contra la API real y Postgres.

Cubre lo que las pruebas unitarias (SQLite) no pueden: los totales y rangos de fecha en SQL real, los códigos de
estado (200/201/400/401/404/409), la idempotencia con peticiones simultáneas y el aislamiento entre tiendas.
Necesita la API corriendo (perfil http, puerto 5013). Solo para desarrollo. Crea cuentas de prueba en hermes_dev.
Uso: python -I scripts/e2e_inicio.py
"""
import json, random, sys, uuid, urllib.request, urllib.error
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timedelta, timezone

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


def producto(nombre, precio, costo):
    return {"nombre": nombre, "descripcion": None, "precio": precio, "costo": costo, "categoriaId": None,
            "visibleEnTienda": True, "colores": None, "tallas": None, "fotoIds": None, "controlaStock": True}


def gasto(concepto, monto, fecha=None, clave=None):
    return {"claveIdempotencia": clave or str(uuid.uuid4()), "concepto": concepto, "monto": monto, "fecha": fecha}


def iso(d):
    return d.strftime("%Y-%m-%dT%H:%M:%SZ")


ahora = datetime.now(timezone.utc)
mes = f"Desde={iso(ahora - timedelta(days=30))}&Hasta={iso(ahora + timedelta(days=1))}"

n = random.randint(10000, 99999)
_, ses = api("POST", "/api/v1/auth/registro", {"nombre": "Inicio", "email": f"inicio{n}@ejemplo.com", "password": "secreta123", "nombreTienda": f"Inicio {n}", "slug": f"inicio-{n}"})
t = ses["accessToken"]
_, ses_b = api("POST", "/api/v1/auth/registro", {"nombre": "Otra", "email": f"oinicio{n}@ejemplo.com", "password": "secreta123", "nombreTienda": f"Oinicio {n}", "slug": f"oinicio-{n}"})
tb = ses_b["accessToken"]

# 1. Inicio vacío
ok("sin sesión no se ve el inicio (401)", api("GET", f"/api/v1/inicio?{mes}")[0] == 401)
s, i = api("GET", f"/api/v1/inicio?{mes}", token=t)
ok("una tienda nueva: todo en cero, sin porcentaje y sin actividad", s == 200 and i["ventas"] == 0 and i["gastos"] == 0 and i["porcentajeCubierto"] is None and i["recientes"] == [] and i["paraReponer"] == [])

# 2. Gastos
s, g1 = api("POST", "/api/v1/gastos", gasto("  Alquiler del local ", 800), t)
ok("registrar un gasto: 201, concepto sin espacios", s == 201 and g1["concepto"] == "Alquiler del local" and g1["monto"] == 800 and g1["anulado"] is False)
for desc, cuerpo in [("concepto vacío", gasto(" ", 10)), ("monto 0", gasto("Luz", 0)), ("monto negativo", gasto("Luz", -5)), ("3 decimales", gasto("Luz", 10.555)),
                     ("monto enorme", gasto("Luz", 2000000)), ("fecha futura", gasto("Luz", 10, iso(ahora + timedelta(days=5)))),
                     ("clave vacía", gasto("Luz", 10, clave="00000000-0000-0000-0000-000000000000"))]:
    ok(f"gasto con {desc} → 400", api("POST", "/api/v1/gastos", cuerpo, t)[0] == 400)
ok("un monto escrito como texto → 400 (JSON estricto)", api("POST", "/api/v1/gastos", {**gasto("Luz", 10), "monto": "10"}, t)[0] == 400)
s, g2 = api("POST", "/api/v1/gastos", gasto("Luz de hace 10 días", 120, iso(ahora - timedelta(days=10))), t)
ok("un gasto de una fecha pasada se acepta", s == 201 and g2["fecha"].startswith((ahora - timedelta(days=10)).strftime("%Y-%m-%d")))
s, g_viejo = api("POST", "/api/v1/gastos", gasto("Del año pasado", 1000, iso(ahora - timedelta(days=400))), t)

clave = str(uuid.uuid4())
with ThreadPoolExecutor(10) as pool:
    resp = list(pool.map(lambda _: api("POST", "/api/v1/gastos", gasto("Publicidad", 200, clave=clave), t), range(10)))
ids = {r[1]["id"] for r in resp}
ok(f"10 reintentos simultáneos con la misma clave: un solo gasto ({len(ids)} id)", len(ids) == 1 and sum(r[0] == 201 for r in resp) == 1 and all(r[0] in (200, 201) for r in resp))

s, pag = api("GET", f"/api/v1/gastos?{mes}", token=t)
ok("el listado del mes trae 3 gastos y su total (800 + 120 + 200) sin el del año pasado", s == 200 and len(pag["items"]) == 3 and pag["total"] == 1120)
s, pag2 = api("GET", f"/api/v1/gastos?{mes}&Limite=1", token=t)
ok("con página de 1, el total sigue siendo el del período", len(pag2["items"]) == 1 and pag2["siguiente"] is not None and pag2["total"] == 1120)

# 3. Ventas + inicio
_, p = api("POST", "/api/v1/productos", producto("Cartera de cuero Mía", 180, 110), t)
v = p["variantes"][0]["id"]
api("POST", "/api/v1/stock/ingresos", {"productoId": p["id"], "lineas": [{"varianteId": v, "cantidad": 20}]}, t)
api("POST", "/api/v1/ventas", {"claveIdempotencia": str(uuid.uuid4()), "lineas": [{"varianteId": v, "cantidad": 5}], "metodoPago": "Efectivo"}, t)
s, i = api("GET", f"/api/v1/inicio?{mes}", token=t)
ok("el inicio: vendiste 900, ganaste 350, gastos 1120, resultado −770", s == 200 and i["totalVendido"] == 900 and i["ganancia"] == 350 and i["gastos"] == 1120 and i["resultado"] == -770)
ok("ya cubriste el 31 % de los gastos (350 ÷ 1120 = 31,25 → 31)", i["porcentajeCubierto"] == 31)
ok("lo más reciente primero: el gasto de Publicidad, la venta y el alquiler (sin el del año pasado)", [(a["tipo"], a["titulo"]) for a in i["recientes"]][:2] in ([("Venta", "5 Cartera de cuero Mía"), ("Gasto", "Publicidad")], [("Gasto", "Publicidad"), ("Venta", "5 Cartera de cuero Mía")]))

# 4. Anular un gasto
s, an = api("POST", f"/api/v1/gastos/{g1['id']}/anular", token=t)
ok("anular un gasto: 200 y marcado", s == 200 and an["anulado"] is True and an["anuladoEn"] is not None)
ok("anular otra vez → 409; uno inexistente → 404", api("POST", f"/api/v1/gastos/{g1['id']}/anular", token=t)[0] == 409 and api("POST", f"/api/v1/gastos/{uuid.uuid4()}/anular", token=t)[0] == 404)
s, i = api("GET", f"/api/v1/inicio?{mes}", token=t)
ok("el inicio ya no cuenta el gasto anulado (gastos 320, cubierto 109 %)", i["gastos"] == 320 and i["porcentajeCubierto"] == 109 and i["resultado"] == 30)
s, todos = api("GET", f"/api/v1/gastos?{mes}&IncluirAnulados=true", token=t)
ok("con IncluirAnulados aparece (3 en el mes, el anulado marcado)", len(todos["items"]) == 3 and any(x["anulado"] for x in todos["items"]) and todos["total"] == 320)

# 5. Reponer y compras
api("POST", "/api/v1/productos", producto("Agotado", 100, 50), t)
_, prov = api("POST", "/api/v1/proveedores", {"nombre": "Don Beto"}, t)
api("POST", "/api/v1/compras", {"claveIdempotencia": str(uuid.uuid4()), "proveedorId": prov["id"], "lineas": [{"varianteId": v, "cantidad": 10, "costoUnitario": 60}], "notas": None, "recibida": False, "pagada": False}, t)
s, i = api("GET", f"/api/v1/inicio?{mes}", token=t)
ok("el inicio avisa 1 agotado, lo que se debe (600) y 1 pedido en camino", i["agotados"] == 1 and i["porPagar"] == 600 and i["enCamino"] == 1)
ok("para reponer trae el producto agotado", [x["nombre"] for x in i["paraReponer"]] == ["Agotado"] and i["paraReponer"][0]["estado"] == "Agotado")
ok("la compra aparece entre lo reciente", any(a["tipo"] == "Compra" and a["titulo"] == "Don Beto" for a in i["recientes"]))

# 6. Otra tienda y rango
s, ib = api("GET", f"/api/v1/inicio?{mes}", token=tb)
ok("la otra tienda ve su inicio vacío", s == 200 and ib["ventas"] == 0 and ib["gastos"] == 0 and ib["recientes"] == [])
ok("y no puede anular mi gasto → 404", api("POST", f"/api/v1/gastos/{g2['id']}/anular", token=tb)[0] == 404)
ok("ni listar mis gastos", api("GET", f"/api/v1/gastos?{mes}", token=tb)[1]["items"] == [])
s, vacio = api("GET", f"/api/v1/inicio?Desde={iso(ahora - timedelta(days=3000))}&Hasta={iso(ahora - timedelta(days=2990))}", token=t)
ok("un rango sin nada: ventas y gastos en cero (lo reciente y lo que se debe no dependen del período)", vacio["ventas"] == 0 and vacio["gastos"] == 0 and vacio["porPagar"] == 600)

print(f"--- fallas: {fallas}")
sys.exit(1 if fallas else 0)
