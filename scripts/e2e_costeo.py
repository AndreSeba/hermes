"""Prueba de punta a punta de M8 (costeo y precio sugerido) contra la API real y Postgres.

Cubre lo que las pruebas unitarias no pueden: la restricción única y los CHECK de la base, los códigos de estado
(200/204/400/401/404), el costo que se mantiene coherente al recibir una compra o al sumar "Llegaron más", y el
aislamiento entre tiendas con tokens distintos.
Necesita la API corriendo (perfil http, puerto 5013). Solo para desarrollo. Crea cuentas de prueba en hermes_dev.
Uso: python -I scripts/e2e_costeo.py
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


def producto(nombre, precio, costo):
    return {"nombre": nombre, "descripcion": None, "precio": precio, "costo": costo, "categoriaId": None,
            "visibleEnTienda": True, "colores": None, "tallas": None, "fotoIds": None, "controlaStock": True}


def costeo(material=50, empaque=5, mano=30, fijo=15, margen=40):
    return {"material": material, "empaque": empaque, "manoDeObra": mano, "costoFijo": fijo, "margenObjetivo": margen}


n = random.randint(10000, 99999)
_, ses = api("POST", "/api/v1/auth/registro", {"nombre": "Costeo", "email": f"costeo{n}@ejemplo.com", "password": "secreta123", "nombreTienda": f"Costeo {n}", "slug": f"costeo-{n}"})
t = ses["accessToken"]
_, ses_b = api("POST", "/api/v1/auth/registro", {"nombre": "Otra", "email": f"ocosteo{n}@ejemplo.com", "password": "secreta123", "nombreTienda": f"Ocosteo {n}", "slug": f"ocosteo-{n}"})
tb = ses_b["accessToken"]

_, p = api("POST", "/api/v1/productos", producto("Cartera de cuero Mía", 180, 10), t)
ruta = f"/api/v1/productos/{p['id']}/costeo"
v = p["variantes"][0]["id"]

ok("sin sesión no se ve el costeo (401)", api("GET", ruta)[0] == 401)
s, c = api("GET", ruta, token=t)
ok("sin costeo: Tiene = false, costo directo 10 y margen por defecto 40", s == 200 and c["tiene"] is False and c["costoTotal"] == 10 and c["margenObjetivo"] == 40)

s, c = api("POST", ruta + "/calcular", costeo(), t)
ok("calcular: total 100, sugerido 167 (100 ÷ 0,6 redondeado arriba) y ganancia 67", s == 200 and c["costoTotal"] == 100 and c["precioSugerido"] == 167 and c["gananciaConSugerido"] == 67)
ok("y no guarda nada (sigue sin costeo y con costo 10)", api("GET", ruta, token=t)[1]["tiene"] is False and api("GET", f"/api/v1/productos/{p['id']}", token=t)[1]["costo"] == 10)

for desc, cuerpo in [("material negativo", costeo(material=-1)), ("3 decimales", costeo(empaque=1.555)), ("todo en cero", costeo(0, 0, 0, 0)),
                     ("margen 0", costeo(margen=0)), ("margen 91", costeo(margen=91)), ("costo enorme", costeo(material=2000000))]:
    ok(f"guardar con {desc} → 400", api("PUT", ruta, cuerpo, t)[0] == 400)
ok("ningún intento inválido guardó algo", api("GET", ruta, token=t)[1]["tiene"] is False)
ok("un número escrito como texto → 400 (JSON estricto)", api("PUT", ruta, {**costeo(), "material": "50"}, t)[0] == 400)

s, c = api("PUT", ruta, costeo(), t)
_, pr = api("GET", f"/api/v1/productos/{p['id']}", token=t)
ok("guardar: 200, Tiene, el costo del producto pasó a 100 y el PRECIO sigue en 180", s == 200 and c["tiene"] and pr["costo"] == 100 and pr["precio"] == 180)
ok("ganancia actual 80 (44,4 %), estado Ok", c["gananciaActual"] == 80 and c["margenActual"] == 44.4 and c["estadoActual"] == "Ok")
s, c = api("PUT", ruta, costeo(material=80), t)
ok("cambiar el material cambia el sugerido (130 → 217) y el costo, no el precio", c["costoTotal"] == 130 and c["precioSugerido"] == 217 and api("GET", f"/api/v1/productos/{p['id']}", token=t)[1]["precio"] == 180)

# El costo no se puede cambiar "por la puerta de atrás"
cuerpo = {**producto("Cartera de cuero Mía", 200, 999), "colores": None}
s, _ = api("PUT", f"/api/v1/productos/{p['id']}", cuerpo, t)
_, pr = api("GET", f"/api/v1/productos/{p['id']}", token=t)
ok("editar el producto con costeo: el precio cambia (200) pero el costo directo se ignora (130)", s == 200 and pr["precio"] == 200 and pr["costo"] == 130)

# Recibir una compra actualiza el material
_, prov = api("POST", "/api/v1/proveedores", {"nombre": "Don Beto"}, t)
_, compra = api("POST", "/api/v1/compras", {"claveIdempotencia": str(uuid.uuid4()), "proveedorId": prov["id"], "lineas": [{"varianteId": v, "cantidad": 4, "costoUnitario": 70}], "notas": None, "recibida": False, "pagada": False}, t)
api("POST", f"/api/v1/compras/{compra['id']}/recibir", token=t)
c = api("GET", ruta, token=t)[1]
ok("recibir una compra a 70: el material pasa a 70, lo demás no se toca y el costo es 120", c["material"] == 70 and c["empaque"] == 5 and c["manoDeObra"] == 30 and c["costoFijo"] == 15 and c["costoTotal"] == 120
   and api("GET", f"/api/v1/productos/{p['id']}", token=t)[1]["costo"] == 120)
api("POST", "/api/v1/stock/ingresos", {"productoId": p["id"], "lineas": [{"varianteId": v, "cantidad": 2}], "costoUnitario": 65}, t)
c = api("GET", ruta, token=t)[1]
ok("«Llegaron más» con costo 65: material 65 y costo 115", c["material"] == 65 and c["costoTotal"] == 115)

# Concurrencia: 8 primeros guardados a la vez crean UN solo costeo
_, p2 = api("POST", "/api/v1/productos", producto("Carrera", 100, 0), t)
with ThreadPoolExecutor(8) as pool:
    estados = list(pool.map(lambda i: api("PUT", f"/api/v1/productos/{p2['id']}/costeo", costeo(material=10 + i), t)[0], range(8)))
c2 = api("GET", f"/api/v1/productos/{p2['id']}/costeo", token=t)[1]
ok(f"8 guardados simultáneos: ninguno rompe (200/400) y queda un costeo coherente ({sorted(set(estados))})", all(e in (200, 400) for e in estados) and c2["tiene"] and c2["costoTotal"] == api("GET", f"/api/v1/productos/{p2['id']}", token=t)[1]["costo"])

# Otra tienda
ok("la otra tienda no ve, calcula, guarda ni quita mi costeo → 404", all(api(m, ruta + x, o, tb)[0] == 404 for m, x, o in [("GET", "", None), ("POST", "/calcular", costeo()), ("PUT", "", costeo()), ("DELETE", "", None)]))
ok("mi costeo sigue intacto después de esos intentos", api("GET", ruta, token=t)[1]["costoTotal"] == 115)

# Quitar
s, _ = api("DELETE", ruta, token=t)
ok("quitar: 204, el producto conserva el último costo (115) y vuelve a «sin costeo»", s == 204 and api("GET", ruta, token=t)[1]["tiene"] is False and api("GET", f"/api/v1/productos/{p['id']}", token=t)[1]["costo"] == 115)
ok("quitar otra vez es idempotente (204)", api("DELETE", ruta, token=t)[0] == 204)
s, _ = api("PUT", f"/api/v1/productos/{p['id']}", {**producto("Cartera de cuero Mía", 200, 90)}, t)
ok("sin costeo el costo directo vuelve a editarse (90)", s == 200 and api("GET", f"/api/v1/productos/{p['id']}", token=t)[1]["costo"] == 90)
ok("producto inexistente → 404", api("GET", f"/api/v1/productos/{uuid.uuid4()}/costeo", token=t)[0] == 404)

print(f"--- fallas: {fallas}")
sys.exit(1 if fallas else 0)
