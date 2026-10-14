"""Prueba de punta a punta de M4 (catálogo) contra la API real y Postgres.

Cubre lo que las pruebas unitarias (SQLite) no pueden: paginación por cursor sobre uuid, búsqueda,
el filtro "Revisar precio" con aritmética decimal, borrados en cascada y las restricciones de la base.
Necesita la API corriendo (perfil http, puerto 5013) y Python con Pillow (`pip install pillow`).
Solo para desarrollo. Crea cuentas de prueba en hermes_dev.
Uso: python -I scripts/e2e_productos.py
"""
import io, json, random, sys, urllib.request, urllib.error
from PIL import Image

sys.stdout.reconfigure(encoding="utf-8")
BASE = "http://localhost:5013"
fallas = 0


def ok(desc, cond):
    global fallas
    print(("OK    " if cond else "FALLA ") + desc)
    if not cond:
        fallas += 1


def pedir(metodo, ruta, cuerpo=None, token=None, cabeceras=None):
    h = dict(cabeceras or {})
    if token:
        h["Authorization"] = "Bearer " + token
    req = urllib.request.Request(BASE + ruta, data=cuerpo, headers=h, method=metodo)
    try:
        r = urllib.request.urlopen(req)
        return r.status, r.read()
    except urllib.error.HTTPError as e:
        return e.code, e.read()


def api(metodo, ruta, obj=None, token=None):
    cuerpo = json.dumps(obj).encode() if obj is not None else None
    s, b = pedir(metodo, ruta, cuerpo, token, {"Content-Type": "application/json"} if obj is not None else None)
    return s, (json.loads(b) if b and b[:1] in b"{[" else None)


def foto(token, color):
    img = io.BytesIO()
    Image.new("RGB", (800, 600), color).save(img, "JPEG", quality=85)
    limite = "----h" + str(random.randint(10**8, 10**9))
    cuerpo = (f'--{limite}\r\nContent-Disposition: form-data; name="archivo"; filename="f.jpg"\r\nContent-Type: image/jpeg\r\n\r\n').encode() \
        + img.getvalue() + f"\r\n--{limite}--\r\n".encode()
    s, b = pedir("POST", "/api/v1/imagenes", cuerpo, token, {"Content-Type": f"multipart/form-data; boundary={limite}"})
    return json.loads(b)


def producto(nombre, precio, costo, categoria=None, colores=None, tallas=None, fotos=None, visible=True, descripcion=None):
    return {"nombre": nombre, "descripcion": descripcion, "precio": precio, "costo": costo, "categoriaId": categoria,
            "visibleEnTienda": visible, "colores": colores, "tallas": tallas, "fotoIds": fotos}


n = random.randint(10000, 99999)
s, ses = api("POST", "/api/v1/auth/registro", {"nombre": "Cat", "email": f"cat{n}@ejemplo.com", "password": "secreta123", "nombreTienda": f"Cat {n}", "slug": f"cat-{n}"})
t = ses["accessToken"]
s, ses_b = api("POST", "/api/v1/auth/registro", {"nombre": "Otra", "email": f"gato{n}@ejemplo.com", "password": "secreta123", "nombreTienda": f"Gato {n}", "slug": f"gato-{n}"})
tb = ses_b["accessToken"]

# 1. Sesión y catálogo vacío
ok("sin sesión no se ve el catálogo (401)", api("GET", "/api/v1/productos")[0] == 401)
s, vacio = api("GET", "/api/v1/productos", token=t)
ok("catálogo vacío: items [] y siguiente null", s == 200 and vacio == {"items": [], "siguiente": None})

# 2. Categorías y productos
_, carteras = api("POST", "/api/v1/categorias", {"nombre": "Carteras"}, t)
_, joyeria = api("POST", "/api/v1/categorias", {"nombre": "Joyería"}, t)
casos = [
    ("Cartera de cuero Mía", 180, 110, carteras["id"], ["café", "negro"]),
    ("Aretes de plata Killa", 95, 38, joyeria["id"], None),
    ("Bolso awayo Inti", 150, 85, carteras["id"], None),
    ("Pulsera trenzada Andes", 35, 32, None, None),
    ("Collar Salar turquesa", 120, 58.50, joyeria["id"], None),
    ("Billetera Tunari", 60, 62, None, None),
    ("Llavero sin costo", 40, 0, None, None),
]
creados = {}
for nombre, precio, costo, cat, colores in casos:
    s, p = api("POST", "/api/v1/productos", producto(nombre, precio, costo, cat, colores), t)
    creados[nombre] = p
    assert s == 201, (s, p)
ok("se crearon los 7 productos (201)", len(creados) == 7)
estados = {n_: p["estadoPrecio"] for n_, p in creados.items()}
ok("los estados salen como texto: " + ", ".join(sorted(set(estados.values()))),
   estados["Cartera de cuero Mía"] == "Ok" and estados["Pulsera trenzada Andes"] == "Bajo"
   and estados["Billetera Tunari"] == "Pierde" and estados["Llavero sin costo"] == "SinCosto")
ok("la ganancia es precio − costo (120 − 58,50 = 61,50)", creados["Collar Salar turquesa"]["ganancia"] == 61.5)

# 3. Paginación por cursor en Postgres
vistos, tamanos, cursor = [], [], None
while True:
    ruta = "/api/v1/productos?limite=3" + (f"&cursor={cursor}" if cursor else "")
    s, pag = api("GET", ruta, token=t)
    tamanos.append(len(pag["items"]))
    vistos += [i["id"] for i in pag["items"]]
    cursor = pag["siguiente"]
    if not cursor:
        break
ok(f"páginas de 3 en 3: {tamanos}", tamanos == [3, 3, 1])
ok("ninguno repetido ni salteado", len(set(vistos)) == 7 and set(vistos) == {p["id"] for p in creados.values()})
ok("más nuevos primero (uuid v7 ordenado en Postgres)", vistos == sorted(vistos, reverse=True))
s, tope = api("GET", "/api/v1/productos?limite=100000", token=t)
ok("un límite absurdo se acota sin fallar", s == 200 and len(tope["items"]) == 7)

# 4. Búsqueda y filtros
def nombres(qs):
    s, pag = api("GET", "/api/v1/productos" + qs, token=t)
    return sorted(i["nombre"] for i in pag["items"]) if s == 200 else s

ok("buscar 'CART' ignora mayúsculas", nombres("?buscar=CART") == ["Cartera de cuero Mía"])
ok("buscar con comodín % no trae todo", nombres("?buscar=%25") == [])
ok("buscar con comodín _ no trae todo", nombres("?buscar=_") == [])
ok("buscar con espacios y tilde", nombres("?buscar=%20m%C3%ADa%20") == ["Cartera de cuero Mía"])
ok("filtro por categoría", nombres(f"?categoriaId={carteras['id']}") == ["Bolso awayo Inti", "Cartera de cuero Mía"])
ok("'Revisar precio' = los que pierden o ganan poco, y no los sin costo",
   nombres("?revisarPrecio=true") == ["Billetera Tunari", "Pulsera trenzada Andes"])
ok("revisar precio + categoría se combinan", nombres(f"?revisarPrecio=true&categoriaId={joyeria['id']}") == [])

# 5. Detalle, variantes y su orden
s, det = api("GET", f"/api/v1/productos/{creados['Cartera de cuero Mía']['id']}", token=t)
ok("el detalle trae las variantes en el orden escrito", [v["color"] for v in det["variantes"]] == ["café", "negro"])
ok("sin color ni talla: una variante 'Único' (null, null)",
   [(v["color"], v["talla"]) for v in creados["Billetera Tunari"]["variantes"]] == [(None, None)])
pid = det["id"]
s, e = api("PUT", f"/api/v1/productos/{pid}", producto("Cartera de cuero Mía", 180, 110, carteras["id"], ["negro", "café", "gris"], ["S", "M"]), t)
ok("editar: 3 colores × 2 tallas = 6, el orden es el pedido",
   s == 200 and len(e["variantes"]) == 6 and [(v["color"], v["talla"]) for v in e["variantes"]][:2] == [("negro", "S"), ("negro", "M")])
s, e = api("PUT", f"/api/v1/productos/{pid}", producto("Cartera de cuero Mía", 180, 110, carteras["id"], ["café"]), t)
ok("sacar colores deja una sola variante activa", s == 200 and [(v["color"], v["talla"]) for v in e["variantes"]] == [("café", None)])
s, e = api("PUT", f"/api/v1/productos/{pid}", producto("Cartera de cuero Mía", 180, 110, carteras["id"], ["café", "NEGRO"]), t)
ok("volver a poner un color funciona (no choca con el índice único)", s == 200 and [v["color"] for v in e["variantes"]] == ["café", "NEGRO"])

# 6. Validaciones (400) y el CHECK de la base
for desc, cuerpo in [("precio 0", producto("X", 0, 0)), ("precio con 3 decimales", producto("X", 10.555, 0)),
                     ("costo negativo", producto("X", 10, -1)), ("nombre vacío", producto("  ", 10, 0)),
                     ("categoría de otra tienda", producto("X", 10, 0, joyeria["id"].replace(joyeria["id"][:4], "0000"))),
                     ("precio gigante", producto("X", 2_000_000, 0))]:
    s, r = api("POST", "/api/v1/productos", cuerpo, t)
    ok(f"{desc} → 400/404 con mensaje claro", s in (400, 404) and r and r.get("detail"))

# 7. Fotos
f1, f2, f3 = foto(t, (200, 30, 30)), foto(t, (30, 200, 30)), foto(t, (30, 30, 200))
s, conf = api("POST", "/api/v1/productos", producto("Con fotos", 100, 0, fotos=[f2["id"], f1["id"], f3["id"]]), t)
ok("producto con 3 fotos: la primera pedida es la portada", s == 201 and [f["id"] for f in conf["fotos"]] == [f2["id"], f1["id"], f3["id"]])
s, lista = api("GET", "/api/v1/productos?buscar=con%20fotos", token=t)
ok("el listado trae la portada con sus urls", lista["items"][0]["portada"]["id"] == f2["id"] and pedir("GET", lista["items"][0]["portada"]["catalogo"])[0] == 200)
s, rep = api("PUT", f"/api/v1/productos/{conf['id']}", producto("Con fotos", 100, 0, fotos=[f3["id"], f2["id"]]), t)
ok("reordenar y sacar una foto", s == 200 and [f["id"] for f in rep["fotos"]] == [f3["id"], f2["id"]])
ok("la foto sacada se borró de verdad (404)", pedir("GET", f1["detalle"])[0] == 404)
ok("las que quedan siguen sirviéndose (200)", pedir("GET", f3["detalle"])[0] == 200 and pedir("GET", f2["detalle"])[0] == 200)
s, r = api("POST", "/api/v1/productos", producto("Otro con la misma foto", 50, 0, fotos=[f3["id"]]), t)
ok("una foto ya usada por otro producto → 409", s == 409)
fb = foto(tb, (100, 100, 100))
s, r = api("POST", "/api/v1/productos", producto("Foto ajena", 50, 0, fotos=[fb["id"]]), t)
ok("la foto de otra tienda → 404", s == 404)
s, r = api("DELETE", f"/api/v1/imagenes/{f2['id']}", token=t)
s, tras = api("GET", f"/api/v1/productos/{conf['id']}", token=t)
ok("borrar una imagen suelta se lleva su vínculo (cascada) y el producto sigue bien",
   [f["id"] for f in tras["fotos"]] == [f3["id"]])

# 8. Archivar
s, a = api("POST", f"/api/v1/productos/{creados['Llavero sin costo']['id']}/archivar", token=t)
ok("archivar", s == 200 and a["archivado"] is True)
ok("archivado no aparece en el catálogo", "Llavero sin costo" not in nombres(""))
ok("archivados=true lista solo los archivados", nombres("?archivados=true") == ["Llavero sin costo"])
s, a = api("POST", f"/api/v1/productos/{creados['Llavero sin costo']['id']}/desarchivar", token=t)
ok("desarchivar lo devuelve", s == 200 and "Llavero sin costo" in nombres(""))

# 9. Aislamiento
s, lb = api("GET", "/api/v1/productos", token=tb)
ok("otra tienda ve el catálogo vacío", s == 200 and lb["items"] == [])
ok("otra tienda no ve el detalle (404)", api("GET", f"/api/v1/productos/{pid}", token=tb)[0] == 404)
ok("otra tienda no lo edita (404)", api("PUT", f"/api/v1/productos/{pid}", producto("Robado", 1, 0), tb)[0] == 404)
ok("otra tienda no lo archiva (404)", api("POST", f"/api/v1/productos/{pid}/archivar", token=tb)[0] == 404)
ok("sigue intacto", api("GET", f"/api/v1/productos/{pid}", token=t)[1]["nombre"] == "Cartera de cuero Mía")

print(f"--- fallas: {fallas}")
sys.exit(1 if fallas else 0)
