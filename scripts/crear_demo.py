"""Crea (o actualiza) una tienda de DEMOSTRACIÓN en la base de desarrollo, con productos de ejemplo y el WhatsApp real
del dueño del proyecto, para probar el panel y la tienda pública con datos que se parecen a los de verdad.
Es solo para desarrollo (localhost). Las credenciales se generan la primera vez y se guardan en `scripts/demo.local.json`
(ignorado por git): no se imprimen ni se suben.
Necesita la API corriendo (puerto 5013). Uso: python -I scripts/crear_demo.py [whatsapp]   (por defecto 76815206)
"""
import io, json, os, random, secrets, sys, urllib.request, urllib.error
from PIL import Image, ImageDraw

sys.stdout.reconfigure(encoding="utf-8")
API = "http://localhost:5013"
ARCHIVO = os.path.join(os.path.dirname(__file__), "demo.local.json")
WHATSAPP = sys.argv[1] if len(sys.argv) > 1 else "76815206"


def pedir(metodo, ruta, cuerpo=None, token=None, cabeceras=None):
    h = dict(cabeceras or {})
    if token:
        h["Authorization"] = "Bearer " + token
    req = urllib.request.Request(API + ruta, data=cuerpo, headers=h, method=metodo)
    try:
        r = urllib.request.urlopen(req)
        return r.status, r.read()
    except urllib.error.HTTPError as e:
        return e.code, e.read()


def api(metodo, ruta, obj=None, token=None):
    s, b = pedir(metodo, ruta, json.dumps(obj).encode() if obj is not None else None, token, {"Content-Type": "application/json"} if obj is not None else {})
    return s, (json.loads(b) if b and b[:1] in b"{[" else None)


def foto(token, color, texto):
    img = Image.new("RGB", (900, 900), color)
    d = ImageDraw.Draw(img)
    d.ellipse([250, 250, 650, 650], fill=(255, 255, 255))
    d.text((380, 440), texto, fill=color)
    buf = io.BytesIO()
    img.save(buf, "JPEG", quality=88)
    limite = "----hermes" + str(random.randint(10**8, 10**9))
    cuerpo = (f"--{limite}\r\nContent-Disposition: form-data; name=\"archivo\"; filename=\"f.jpg\"\r\nContent-Type: image/jpeg\r\n\r\n").encode() + buf.getvalue() + f"\r\n--{limite}--\r\n".encode()
    s, b = pedir("POST", "/api/v1/imagenes", cuerpo, token, {"Content-Type": f"multipart/form-data; boundary={limite}"})
    return json.loads(b)["id"]


if os.path.exists(ARCHIVO):
    cred = json.load(open(ARCHIVO, encoding="utf-8"))
    s, ses = api("POST", "/api/v1/auth/login", {"email": cred["email"], "password": cred["password"]})
    nuevo = False
else:
    cred = {"email": f"demo{random.randint(1000, 9999)}@ejemplo.com", "password": secrets.token_urlsafe(12) + "a1", "slug": f"demo-{random.randint(100, 999)}"}
    s, ses = api("POST", "/api/v1/auth/registro", {"nombre": "Demo", "email": cred["email"], "password": cred["password"], "nombreTienda": "Accesorios Demo", "slug": cred["slug"]})
    json.dump(cred, open(ARCHIVO, "w", encoding="utf-8"))
    nuevo = True
if s not in (200, 201):
    sys.exit(f"No se pudo entrar o crear la cuenta de demostración (estado {s}). ¿La API está corriendo?")
t = ses["accessToken"]

api("PUT", "/api/v1/tienda", {"nombre": "Accesorios Demo", "descripcion": "Accesorios hechos a mano · Cochabamba", "whatsapp": WHATSAPP, "color": "#C2410C", "logoImagenId": None}, t)
s, tienda = api("GET", "/api/v1/tienda", token=t)
print(f"WhatsApp de la tienda: {tienda['whatsapp']}  ·  dirección: /{tienda['slug']}")

if nuevo:
    _, cat = api("POST", "/api/v1/categorias", {"nombre": "Carteras"}, t)
    datos = [("Cartera de cuero Mía", 180, 110, ["café", "negro"], (160, 90, 40), "Hecha a mano en Cochabamba.", cat["id"], 6),
             ("Aretes de plata Killa", 95, 38, None, (90, 110, 150), "Plata 950, con cierre seguro.", None, 12),
             ("Collar Salar turquesa", 120, 58, ["azul", "verde"], (30, 140, 150), "Piedra natural.", None, 4)]
    for nombre, precio, costo, colores, color, desc, categoria, unidades in datos:
        _, p = api("POST", "/api/v1/productos", {"nombre": nombre, "descripcion": desc, "precio": precio, "costo": costo, "categoriaId": categoria,
                   "visibleEnTienda": True, "colores": colores, "tallas": None, "fotoIds": [foto(t, color, nombre[:1])], "controlaStock": True}, t)
        api("POST", "/api/v1/stock/ingresos", {"productoId": p["id"], "lineas": [{"varianteId": v["id"], "cantidad": unidades} for v in p["variantes"]]}, t)
    print("Se cargaron 3 productos de ejemplo con fotos y stock.")
print(f"Credenciales en: {ARCHIVO} (no se imprimen)")
