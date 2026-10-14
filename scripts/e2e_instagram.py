"""Prueba de punta a punta de M11 (lector de Instagram y bandeja) contra la API real y Postgres.

Cubre lo que las pruebas unitarias no pueden: los códigos de estado (200/201/400/401/404/409), que lo importado NO llegue
al catálogo solo, la reserva atómica al publicar (6 publicaciones simultáneas → un solo producto) y el aislamiento entre
tiendas con tokens distintos. Necesita la API corriendo (puerto 5013). Solo para desarrollo. Crea cuentas de prueba.
Uso: python -I scripts/e2e_instagram.py
"""
import json, random, sys, urllib.request, urllib.error
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


def producto(nombre, precio, colores=None, controla=True):
    return {"nombre": nombre, "descripcion": None, "precio": precio, "costo": 0, "categoriaId": None, "visibleEnTienda": True,
            "colores": colores, "tallas": None, "fotoIds": None, "controlaStock": controla}


n = random.randint(10000, 99999)
_, ses = api("POST", "/api/v1/auth/registro", {"nombre": "Insta", "email": f"insta{n}@ejemplo.com", "password": "secreta123", "nombreTienda": f"Insta {n}", "slug": f"insta-{n}"})
t = ses["accessToken"]
_, ses_b = api("POST", "/api/v1/auth/registro", {"nombre": "Otra", "email": f"oinsta{n}@ejemplo.com", "password": "secreta123", "nombreTienda": f"Oinsta {n}", "slug": f"oinsta-{n}"})
tb = ses_b["accessToken"]

TEXTO = "✨ Cartera de cuero Mía ✨\n💰 Bs 180\nQuedan 5\n#carteras #hechoamano"

# 1. Probá tu formato
ok("sin sesión no se lee (401)", api("POST", "/api/v1/instagram/leer", {"texto": TEXTO})[0] == 401)
s, r = api("POST", "/api/v1/instagram/leer", {"texto": TEXTO}, t)
p = r["productos"][0] if s == 200 else {}
ok("«Probá tu formato»: nombre, precio, stock y categoría (sin emojis ni hashtags genéricos)", s == 200 and p["nombre"] == "Cartera de cuero Mía" and p["precio"] == 180 and p["stock"] == 5 and p["categoria"] == "Carteras" and p["faltan"] == [])
ok("y no guardó nada (la bandeja sigue vacía)", api("GET", "/api/v1/instagram/borradores", token=t)[1]["items"] == [])
s, r = api("POST", "/api/v1/instagram/leer", {"texto": "Mochila escolar\nHecha a mano"}, t)
ok("sin precio: lo dice (faltan = precio) y avisa", r["productos"][0]["faltan"] == ["precio"] and any("precio" in a for a in r["productos"][0]["avisos"]))
ok("un texto vacío → 400", api("POST", "/api/v1/instagram/leer", {"texto": "  "}, t)[0] == 400)
ok("un texto de más de 5000 caracteres → 400", api("POST", "/api/v1/instagram/leer", {"texto": "x" * 5001}, t)[0] == 400)
s, r = api("POST", "/api/v1/instagram/leer", {"texto": "Novedades 🛍️\n1. Cartera Bs 120\n2. Aretes Bs 95\n3. Collar Bs 150"}, t)
ok("un carrusel se lee como 3 productos", [(x["nombre"], x["precio"]) for x in r["productos"]] == [("Cartera", 120), ("Aretes", 95), ("Collar", 150)])

# 2. Importar a la bandeja: NO llega al catálogo
s, creados = api("POST", "/api/v1/instagram/importar", {"texto": TEXTO}, t)
ok("importar responde 201 y deja el borrador pendiente", s == 201 and len(creados) == 1 and creados[0]["estado"] == "Pendiente" and creados[0]["origen"] == "Manual" and creados[0]["texto"] == TEXTO)
borrador = creados[0]
ok("lo importado NO está en el catálogo", api("GET", "/api/v1/productos", token=t)[1]["items"] == [])
ok("importar un texto sin nada que leer → 400 con el motivo", api("POST", "/api/v1/instagram/importar", {"texto": "#hechoamano #bolivia"}, t)[0] == 400)
s, creados2 = api("POST", "/api/v1/instagram/importar", {"texto": "Mochila escolar\nHecha a mano"}, t)
incompleto = creados2[0]
s, creados3 = api("POST", "/api/v1/instagram/importar", {"texto": "Novedades\n1. Cartera Bs 120\n2. Aretes Bs 95"}, t)
ok("un carrusel deja un borrador por producto", s == 201 and [b["nombre"] for b in creados3] == ["Cartera", "Aretes"])

s, bandeja = api("GET", "/api/v1/instagram/borradores", token=t)
ok("la bandeja lista los 4 pendientes (lo último importado primero, un carrusel en su orden) y cuenta cuántos hay",
   s == 200 and bandeja["pendientes"] == 4 and [b["nombre"] for b in bandeja["items"]][:2] == ["Cartera", "Aretes"])
ok("el incompleto figura con lo que le falta", next(b for b in bandeja["items"] if b["id"] == incompleto["id"])["faltan"] == ["precio"])
ok("la otra tienda ve su bandeja vacía y no puede abrir mis borradores → 404", api("GET", "/api/v1/instagram/borradores", token=tb)[1]["items"] == [] and api("GET", f"/api/v1/instagram/borradores/{borrador['id']}", token=tb)[0] == 404)
ok("filtrar por estado inventado → 400", api("GET", "/api/v1/instagram/borradores?Estado=Cualquiera", token=t)[0] == 400)

# 3. Revisar y publicar
cuerpo = {"producto": {**producto("Cartera de cuero Mía", 200), "descripcion": "Revisada"}, "stock": borrador["stock"]}
s, pub = api("POST", f"/api/v1/instagram/borradores/{borrador['id']}/publicar", cuerpo, t)
ok("publicar crea el producto con lo que se revisó (precio 200, no el 180 leído)", s == 200 and pub["producto"]["precio"] == 200 and pub["producto"]["descripcion"] == "Revisada")
ok("y carga el stock de «Quedan 5»", pub["stockCargado"] is True and api("GET", f"/api/v1/stock/{pub['producto']['id']}", token=t)[1]["stock"] == 5)
ok("el borrador queda publicado y enlazado al producto", api("GET", f"/api/v1/instagram/borradores/{borrador['id']}", token=t)[1]["productoId"] == pub["producto"]["id"])
ok("publicar otra vez → 409 y no hay un segundo producto", api("POST", f"/api/v1/instagram/borradores/{borrador['id']}/publicar", cuerpo, t)[0] == 409 and len(api("GET", "/api/v1/productos", token=t)[1]["items"]) == 1)
ok("la otra tienda no puede publicar mi borrador → 404", api("POST", f"/api/v1/instagram/borradores/{incompleto['id']}/publicar", {"producto": producto("Robo", 10)}, tb)[0] == 404)
ok("el que no existe → 404", api("POST", f"/api/v1/instagram/borradores/00000000-0000-0000-0000-000000000001/publicar", cuerpo, t)[0] == 404)

s, e = api("POST", f"/api/v1/instagram/borradores/{incompleto['id']}/publicar", {"producto": producto("Mochila escolar", 0)}, t)
ok("publicar con precio 0 → 400 y el borrador vuelve a la bandeja", s == 400 and api("GET", f"/api/v1/instagram/borradores/{incompleto['id']}", token=t)[1]["estado"] == "Pendiente")
s, pub2 = api("POST", f"/api/v1/instagram/borradores/{incompleto['id']}/publicar", {"producto": producto("Mochila escolar", 150)}, t)
ok("corregido el precio, se publica", s == 200 and pub2["producto"]["precio"] == 150)

# 4. Varias opciones: el stock no se reparte
_, varias = api("POST", "/api/v1/instagram/importar", {"texto": "Billetera\nBs 65\nColores: rojo, negro\nQuedan 6"}, t)
s, pub3 = api("POST", f"/api/v1/instagram/borradores/{varias[0]['id']}/publicar", {"producto": producto("Billetera", 65, ["rojo", "negro"]), "stock": 6}, t)
ok("con varios colores no se reparte el stock y se le explica", pub3["stockCargado"] is False and "varias opciones" in pub3["avisoStock"] and len(pub3["producto"]["variantes"]) == 2)

# 5. Descartar
dd = creados3[0]
s, d = api("POST", f"/api/v1/instagram/borradores/{dd['id']}/descartar", token=t)
ok("descartar: 200 y marcado", s == 200 and d["estado"] == "Descartado")
ok("descartar otra vez o publicar un descartado → 409", api("POST", f"/api/v1/instagram/borradores/{dd['id']}/descartar", token=t)[0] == 409 and api("POST", f"/api/v1/instagram/borradores/{dd['id']}/publicar", {"producto": producto("X", 10)}, t)[0] == 409)
ok("el descartado se puede ver filtrando por estado (no se borra)", [b["id"] for b in api("GET", "/api/v1/instagram/borradores?Estado=Descartado", token=t)[1]["items"]] == [dd["id"]])
ok("la otra tienda no puede descartar mi borrador → 404", api("POST", f"/api/v1/instagram/borradores/{creados3[1]['id']}/descartar", token=tb)[0] == 404)

# 6. Concurrencia real: 6 publicaciones simultáneas del mismo borrador → un solo producto
_, c = api("POST", "/api/v1/instagram/importar", {"texto": "Pulsera de hilo\nBs 40\nQuedan 9"}, t)
with ThreadPoolExecutor(6) as pool:
    estados = list(pool.map(lambda _: api("POST", f"/api/v1/instagram/borradores/{c[0]['id']}/publicar", {"producto": producto("Pulsera de hilo", 40), "stock": 9}, t)[0], range(6)))
ok(f"6 publicaciones simultáneas: una gana y 5 reciben 409 ({estados.count(200)}/{estados.count(409)})", estados.count(200) == 1 and estados.count(409) == 5)
pulseras = [p for p in api("GET", "/api/v1/productos?Buscar=pulsera", token=t)[1]["items"] if p["nombre"] == "Pulsera de hilo"]
ok("y quedó un solo producto con el stock cargado una vez (9)", len(pulseras) == 1 and api("GET", f"/api/v1/stock/{pulseras[0]['id']}", token=t)[1]["stock"] == 9)

print(f"--- fallas: {fallas}")
sys.exit(1 if fallas else 0)
