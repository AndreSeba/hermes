"""Prueba de punta a punta de M12 (conexión con Instagram) contra la API real, con un Meta FALSO (scripts/meta_falso.py).

Recorre todo por HTTP: dirección de autorización → "Meta" redirige con el código → la API lo canjea (token corto, largo,
perfil) → sincronizar trae publicaciones con fotos a la bandeja → no repite → permiso revocado en Meta → hay que reconectar →
reconectar → desconectar. Y que el token NUNCA aparezca en lo que devuelve la API, y que otra tienda no pueda conectarse con
el `state` de ésta. Necesita la API (5013) arrancada con la configuración que dice meta_falso.py y el Meta falso en 5099
(si no está corriendo, lo levanta este script). Uso: python -I scripts/e2e_instagram_conexion.py
"""
import json, os, random, sys, time, urllib.parse, urllib.request, urllib.error

sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import meta_falso  # noqa: E402

API, META = "http://localhost:5013", "http://localhost:5099"
fallas = 0


def ok(desc, cond):
    global fallas
    print(("OK    " if cond else "FALLA ") + desc)
    if not cond:
        fallas += 1


class SinRedirigir(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *a, **k):
        return None


def pedir(base, metodo, ruta, obj=None, token=None, seguir=True):
    h = {"Content-Type": "application/json"} if obj is not None else {}
    if token:
        h["Authorization"] = "Bearer " + token
    req = urllib.request.Request(base + ruta, json.dumps(obj).encode() if obj is not None else None, h, method=metodo)
    abrir = urllib.request.urlopen if seguir else urllib.request.build_opener(SinRedirigir).open
    try:
        r = abrir(req)
        b = r.read()
        return r.status, (json.loads(b) if b and b[:1] in b"{[" else None), dict(r.headers), b
    except urllib.error.HTTPError as e:
        b = e.read()
        return e.code, (json.loads(b) if b and b[:1] in b"{[" else None), dict(e.headers), b


api = lambda m, r, o=None, t=None: pedir(API, m, r, o, t)[:2]

try:
    urllib.request.urlopen(META + "/_prueba/pedidos")
except Exception:
    meta_falso.levantar()
    time.sleep(0.5)


def registrar(prefijo):
    n = random.randint(100000, 999999)
    s, ses = api("POST", "/api/v1/auth/registro", {"nombre": prefijo, "email": f"{prefijo}{n}@ejemplo.com", "password": "secreta123",
                                                   "nombreTienda": f"{prefijo} {n}", "slug": f"{prefijo}-{n}"})
    assert s in (200, 201), s
    return ses["accessToken"]


def conectar(token):
    _, u = api("GET", "/api/v1/instagram/conexion/url", t=token)
    s, _, cab, _ = pedir("", "GET", u["url"], seguir=False)  # "Meta" redirige con el código
    destino = urllib.parse.urlparse(cab.get("Location", ""))
    q = urllib.parse.parse_qs(destino.query)
    return s, destino, q


t = registrar("igconexion")
tb = registrar("igotra")

# 1. Estado y dirección
s, e = api("GET", "/api/v1/instagram/conexion", t=t)
ok("con la app configurada: Configurada y todavía no conectada", s == 200 and e["configurada"] is True and e["conectada"] is False)
s, u = api("GET", "/api/v1/instagram/conexion/url", t=t)
ok("la dirección de autorización lleva client_id, redirect_uri, scope y state", s == 200 and "client_id=111222333" in u["url"]
   and "scope=instagram_business_basic" in u["url"] and "redirect_uri=http%3A%2F%2Flocalhost%3A5173%2Finstagram%2Fconectado" in u["url"] and "state=" in u["url"])

# 2. Conectar
s, destino, q = conectar(t)
ok("Meta redirige a /instagram/conectado con el código y el mismo state", s == 302 and destino.path == "/instagram/conectado" and q["code"] == ["codigo-falso"])
estado_a = q["state"][0]
ok("el state de esta tienda NO sirve para conectar otra", api("POST", "/api/v1/instagram/conexion", {"code": "codigo-falso", "state": estado_a}, tb)[0] == 400)
ok("un código inválido: Meta lo rechaza y se explica (400)", api("POST", "/api/v1/instagram/conexion", {"code": "otro", "state": estado_a}, t)[0] == 400)
s, c = api("POST", "/api/v1/instagram/conexion", {"code": "codigo-falso#_", "state": estado_a}, t)
ok("conectar: 200, conectada como @tienda.falsa", s == 200 and c["conectada"] and c["usuario"] == "tienda.falsa")
ok("el token nunca aparece en lo que devuelve la API", "IGQ" not in json.dumps(c) and "IGQ" not in json.dumps(api("GET", "/api/v1/instagram/conexion", t=t)[1]))
ok("la otra tienda sigue sin conexión", api("GET", "/api/v1/instagram/conexion", t=tb)[1]["conectada"] is False)

# 3. Sincronizar
pedir(META, "POST", "/_prueba/publicar", {"id": "ig-1", "caption": "Cartera Aurora\nBs 150\nColores: café, negro\n#carteras", "fotos": 1})
pedir(META, "POST", "/_prueba/publicar", {"id": "ig-2", "caption": "Novedades\n1. Aretes Luna Bs 90\n2. Collar Sol Bs 120", "tipo": "CAROUSEL_ALBUM", "fotos": 2})
pedir(META, "POST", "/_prueba/publicar", {"id": "ig-3", "caption": "¡Gracias por los 1000 seguidores!"})
s, r = api("POST", "/api/v1/instagram/conexion/sincronizar", t=t)
ok("sincronizar: 3 publicaciones, 3 productos, 1 sin productos", s == 200 and (r["publicaciones"], r["productos"], r["sinProductos"]) == (3, 3, 1))
_, bandeja = api("GET", "/api/v1/instagram/borradores", t=t)
nombres = {b["nombre"]: b for b in bandeja["items"]}
ok("la bandeja tiene los 3 con origen Instagram y su enlace", set(nombres) == {"Cartera Aurora", "Aretes Luna", "Collar Sol"}
   and all(b["origen"] == "Instagram" and b["enlace"].startswith("https://www.instagram.com/p/") for b in bandeja["items"]))
ok("cada uno trae su foto (bajada de Instagram y guardada en 3 tamaños)", all(len(b["fotos"]) == 1 for b in bandeja["items"])
   and urllib.request.urlopen(API + nombres["Cartera Aurora"]["fotos"][0]["catalogo"]).status == 200)
ok("nada llegó al catálogo solo", api("GET", "/api/v1/productos", t=t)[1]["items"] == [])
time.sleep(1)
s, r2 = api("POST", "/api/v1/instagram/conexion/sincronizar", t=t)
ok("sincronizar enseguida otra vez no pregunta a Meta ni repite nada", s == 200 and r2["publicaciones"] == 0)

# 4. Publicar uno con su foto
cartera = nombres["Cartera Aurora"]
cuerpo = {"producto": {"nombre": "Cartera Aurora", "descripcion": None, "precio": 150, "costo": 0, "categoriaId": None, "visibleEnTienda": True,
                       "colores": ["café", "negro"], "tallas": None, "fotoIds": [f["id"] for f in cartera["fotos"]], "controlaStock": True}, "stock": None}
s, pub = api("POST", f"/api/v1/instagram/borradores/{cartera['id']}/publicar", cuerpo, t)
ok("publicar el borrador crea el producto CON la foto de Instagram", s in (200, 201) and pub["producto"]["fotos"][0]["id"] == cartera["fotos"][0]["id"])

# 5. Meta revoca el permiso → reconectar
pedir(META, "POST", "/_prueba/revocar", {})
time.sleep(61)  # la sincronización manual se puede repetir al minuto
s, e = api("POST", "/api/v1/instagram/conexion/sincronizar", t=t)
ok("con el permiso revocado: 400 con un mensaje para reconectar", s == 400 and "Volvé a conectar" in e["detail"])
_, est = api("GET", "/api/v1/instagram/conexion", t=t)
ok("queda marcada para reconectar", est["necesitaReconectar"] is True)
ok("y no se puede sincronizar hasta reconectar (409)", api("POST", "/api/v1/instagram/conexion/sincronizar", t=t)[0] == 409)
s, _, q2 = conectar(t)
s, c2 = api("POST", "/api/v1/instagram/conexion", {"code": "codigo-falso", "state": q2["state"][0]}, t)
ok("reconectar lo arregla", s == 200 and c2["necesitaReconectar"] is False and c2["conectada"])

# 6. Desconectar
s, _ = api("DELETE", "/api/v1/instagram/conexion", t=t)
ok("desconectar: 204 y ya no está conectada", s == 204 and api("GET", "/api/v1/instagram/conexion", t=t)[1]["conectada"] is False)
ok("sin conexión no se sincroniza (409)", api("POST", "/api/v1/instagram/conexion/sincronizar", t=t)[0] == 409)

pedidos = json.loads(urllib.request.urlopen(META + "/_prueba/pedidos").read())["pedidos"]
ok("la API habló con Meta por las rutas documentadas", {"POST /oauth/access_token", "GET /access_token", "GET /v23.0/me", "GET /v23.0/me/media"} <= set(pedidos))

print(f"\n{'TODO BIEN' if fallas == 0 else str(fallas) + ' FALLAS'}")
sys.exit(1 if fallas else 0)
