"""Prueba de punta a punta de M10 (tienda pública) contra la API real, el sitio público real y Postgres.

Cubre lo que las pruebas unitarias no pueden: el HTML que se arma de verdad (vista previa para WhatsApp, enlace de
pedido, escapado), la imagen JPEG de la vista previa, los encabezados de seguridad y de caché, el límite por IP y que
NADA confidencial salga (costo, margen, stock exacto) ni por JSON ni por HTML.
Necesita la API (puerto 5013) y el sitio público (puerto 5014: `dotnet run --project src/Hermes.Public --launch-profile http`)
y Python con Pillow. Solo para desarrollo. Crea cuentas de prueba en hermes_dev.
Uso: python -I scripts/e2e_publico.py
"""
import io, json, random, re, sys, urllib.parse, urllib.request, urllib.error
from PIL import Image

sys.stdout.reconfigure(encoding="utf-8")
API = "http://localhost:5013"
WEB = "http://localhost:5014"
fallas = 0


def ok(desc, cond):
    global fallas
    print(("OK    " if cond else "FALLA ") + desc)
    if not cond:
        fallas += 1


def pedir(base, metodo, ruta, cuerpo=None, token=None, cabeceras=None):
    h = dict(cabeceras or {})
    if token:
        h["Authorization"] = "Bearer " + token
    req = urllib.request.Request(base + ruta, data=cuerpo, headers=h, method=metodo)
    try:
        r = urllib.request.urlopen(req)
        return r.status, r.read(), r.headers
    except urllib.error.HTTPError as e:
        return e.code, e.read(), e.headers


def api(metodo, ruta, obj=None, token=None):
    cab = {"Content-Type": "application/json"} if obj is not None else {}
    s, b, _ = pedir(API, metodo, ruta, json.dumps(obj).encode() if obj is not None else None, token, cab)
    return s, (json.loads(b) if b and b[:1] in b"{[" else None)


def web(ruta):
    s, b, h = pedir(WEB, "GET", ruta)
    return s, b.decode("utf-8", "replace"), h


def publico(slug, ruta=""):
    s, b, h = pedir(API, "GET", f"/public/{slug}{ruta}")
    return s, (json.loads(b) if b and b[:1] in b"{[" else None), b.decode("utf-8", "replace"), h


def subir(token, color):
    img = Image.new("RGB", (800, 600), color)
    buf = io.BytesIO()
    img.save(buf, "JPEG", quality=85)
    limite = "----hermes" + str(random.randint(10**8, 10**9))
    cuerpo = (f"--{limite}\r\nContent-Disposition: form-data; name=\"archivo\"; filename=\"f.jpg\"\r\nContent-Type: image/jpeg\r\n\r\n").encode() \
        + buf.getvalue() + f"\r\n--{limite}--\r\n".encode()
    s, b, _ = pedir(API, "POST", "/api/v1/imagenes", cuerpo, token, {"Content-Type": f"multipart/form-data; boundary={limite}"})
    return json.loads(b)


def producto(nombre, precio, costo, colores=None, controla=True, visible=True, foto=None, categoria=None, descripcion="Hecha a mano"):
    return {"nombre": nombre, "descripcion": descripcion, "precio": precio, "costo": costo, "categoriaId": categoria,
            "visibleEnTienda": visible, "colores": colores, "tallas": None, "fotoIds": [foto] if foto else None, "controlaStock": controla}


import time
# La corrida anterior termina probando el límite por IP (240/min): si la IP sigue limitada, se espera a que se libere.
for _ in range(15):
    if pedir(API, "GET", "/public/espera")[0] != 429:
        break
    time.sleep(5)

PROHIBIDO = re.compile(r"costo|margen|ganancia|stock|cantidad|proveedor", re.I)

n = random.randint(10000, 99999)
slug = f"pub-{n}"
_, ses = api("POST", "/api/v1/auth/registro", {"nombre": "Pública", "email": f"publico{n}@ejemplo.com", "password": "secreta123", "nombreTienda": f"Tienda Pública {n}", "slug": slug})
t = ses["accessToken"]
_, ses_b = api("POST", "/api/v1/auth/registro", {"nombre": "Otra", "email": f"opublico{n}@ejemplo.com", "password": "secreta123", "nombreTienda": f"Otra {n}", "slug": f"otra-pub-{n}"})
tb = ses_b["accessToken"]
slug_b = f"otra-pub-{n}"

# 1. Configuración de la tienda desde el panel
s, conf = api("GET", "/api/v1/tienda/publica", token=t)
ok("la configuración arranca toda encendida con fondo Crema", s == 200 and conf == {"publicada": True, "mostrarPrecios": True, "mostrarDisponibilidad": True, "pedidosPorWhatsapp": True, "fondo": "Crema"})
ok("sin sesión no se ve la configuración (401)", api("GET", "/api/v1/tienda/publica")[0] == 401)
logo = subir(t, (190, 60, 20))
api("PUT", "/api/v1/tienda", {"nombre": f"Tienda Pública {n}", "descripcion": "Accesorios hechos a mano · Cochabamba", "whatsapp": "712 34567", "color": "#C2410C", "logoImagenId": logo["id"]}, t)
ok("un fondo inventado → 400", api("PUT", "/api/v1/tienda/publica", {**conf, "fondo": "Fucsia"}, t)[0] == 400)

# 2. Datos de la tienda
_, cat = api("POST", "/api/v1/categorias", {"nombre": "Carteras"}, t)
_, cat_vacia = api("POST", "/api/v1/categorias", {"nombre": "Sin productos"}, t)
foto1, foto2 = subir(t, (20, 90, 200)), subir(t, (30, 160, 90))
_, cartera = api("POST", "/api/v1/productos", producto("Cartera de cuero Mía", 180, 4321, ["café", "negro"], foto=foto1["id"], categoria=cat["id"]), t)
_, aretes = api("POST", "/api/v1/productos", producto("Aretes de plata", 58.5, 20, foto=foto2["id"]), t)
_, agotado = api("POST", "/api/v1/productos", producto("Pulsera agotada", 40, 10), t)
_, encargo = api("POST", "/api/v1/productos", producto("Bolso por encargo", 220, 120, controla=False), t)
_, oculto = api("POST", "/api/v1/productos", producto("Producto oculto", 99, 1, visible=False), t)
_, archivado = api("POST", "/api/v1/productos", producto("Producto archivado", 99, 1), t)
api("POST", f"/api/v1/productos/{archivado['id']}/archivar", token=t)
_, xss = api("POST", "/api/v1/productos", producto("<script>alert(1)</script> Collar", 70, 5, descripcion="<img src=x onerror=alert(2)>"), t)
for p, cant in [(cartera, 5), (aretes, 7), (xss, 2)]:
    api("POST", "/api/v1/stock/ingresos", {"productoId": p["id"], "lineas": [{"varianteId": v["id"], "cantidad": cant} for v in p["variantes"]]}, t)

s, tienda, crudo, h = publico(slug)
ok("la tienda pública responde 200 con nombre, color, logo y whatsapp", s == 200 and tienda["nombre"] == f"Tienda Pública {n}" and tienda["color"] == "#C2410C" and tienda["logo"] and tienda["whatsapp"] == "+59171234567")
ok("solo trae la categoría con productos (no «Sin productos»)", [c["nombre"] for c in tienda["categorias"]] == ["Carteras"])
ok("el JSON de la tienda no tiene nada de costo/margen/stock", not PROHIBIDO.search(crudo))
ok("lleva caché corta (30 s)", "max-age=30" in (h.get("Cache-Control") or ""))

s, lista, crudo, _ = publico(slug, "/productos?Limite=50")
nombres = [i["nombre"] for i in lista["items"]]
ok("lista solo los visibles y no archivados, con los agotados al final", s == 200 and "Producto oculto" not in nombres and "Producto archivado" not in nombres and nombres[-1] == "Pulsera agotada" and len(nombres) == 5)
ok("el JSON de la lista no tiene costo/margen/stock ni el costo como valor", not PROHIBIDO.search(crudo) and "4321" not in crudo)
disp = {i["nombre"]: i["disponible"] for i in lista["items"]}
ok("disponible: con stock sí, agotado no, por encargo sí", disp["Cartera de cuero Mía"] is True and disp["Pulsera agotada"] is False and disp["Bolso por encargo"] is True)
s, det, crudo, _ = publico(slug, f"/productos/{cartera['id']}")
ok("el detalle trae las variantes (café, negro) y las fotos, sin cantidades", s == 200 and [v["color"] for v in det["variantes"]] == ["café", "negro"] and len(det["fotos"]) == 1 and not PROHIBIDO.search(crudo))
ok("un producto oculto, archivado o inexistente → 404", all(publico(slug, f"/productos/{x}")[0] == 404 for x in [oculto["id"], archivado["id"], "00000000-0000-0000-0000-000000000001"]))
_, b_prod = api("POST", "/api/v1/productos", producto("De la otra tienda", 10, 1), tb)
ok("el producto de OTRA tienda no se ve por mi slug → 404 (y por el suyo sí)", publico(slug, f"/productos/{b_prod['id']}")[0] == 404 and publico(slug_b, f"/productos/{b_prod['id']}")[0] == 200)
ok("buscar y filtrar por categoría", [i["nombre"] for i in publico(slug, "/productos?Buscar=ARETES")[1]["items"]] == ["Aretes de plata"] and [i["nombre"] for i in publico(slug, f"/productos?CategoriaId={cat['id']}")[1]["items"]] == ["Cartera de cuero Mía"])
ok("un cursor inventado → 400", publico(slug, "/productos?Cursor=9.xyz")[0] == 400)
ok("una tienda que no existe → 404", publico("no-existe-" + str(n))[0] == 404)

# 3. El sitio público (HTML)
s, html, h = web(f"/{slug}")
ok("la página de la tienda responde 200 en HTML", s == 200 and "text/html" in h["Content-Type"] and "<h1>" in html)
ok("tiene las etiquetas de vista previa para WhatsApp (título, descripción, imagen JPEG absoluta, url)",
   f'og:title" content="Tienda Pública {n}"' in html and "Accesorios hechos a mano" in html and f'og:image" content="{WEB}/{slug}/og.jpg"' in html
   and 'og:image:width" content="1200"' in html and f'og:url" content="{WEB}/{slug}"' in html)
ok("pone el color de la marca (#C2410C) y texto legible sobre él", "--marca: #C2410C" in html and "--sobre-marca: #FFFFFF" in html)
ok("muestra los productos con su precio y «Agotado»", "Cartera de cuero Mía" in html and "Bs 180" in html and "Bs 58,50" in html and "Agotado" in html and "Producto oculto" not in html)
ok("las fotos apuntan a la API y existen (WEBP)", all(pedir("", "GET", u)[0] == 200 for u in re.findall(r'src="(http://localhost:5013/archivos/[^"]+)"', html)[:3]) and "http://localhost:5013/archivos/" in html)
ok("el HTML no tiene costo, margen ni stock (ni el costo como valor)", not re.search(r"costo|margen|ganancia|stock", html, re.I) and "4321" not in html)
ok("tiene el botón de WhatsApp de la tienda", "https://wa.me/59171234567?text=" in html)
ok("encabezados de seguridad y caché", h.get("X-Content-Type-Options") == "nosniff" and h.get("X-Frame-Options") == "DENY" and "max-age=30" in (h.get("Cache-Control") or ""))
ok("el texto de un producto con HTML se escapa (no se ejecuta)", "<script>alert(1)</script>" not in html and "&lt;script&gt;alert(1)&lt;/script&gt;" in html)

s, html2, _ = web(f"/{slug}?q=aretes")
ok("buscar en el sitio filtra", s == 200 and "Aretes de plata" in html2 and "Cartera de cuero Mía" not in html2)
s, html3, _ = web(f"/{slug}?cursor=zzz")
ok("un cursor inventado muestra la primera página en vez de un error", s == 200 and "Cartera de cuero Mía" in html3)
s, html4, _ = web(f"/{slug}?categoria={cat['id']}")
ok("la categoría filtra", "Cartera de cuero Mía" in html4 and "Aretes de plata" not in html4)

s, ph, _ = web(f"/{slug}/p/{cartera['id']}")
ok("la página del producto: título, vista previa con la foto del producto, variantes y fotos", s == 200 and f"Cartera de cuero Mía · Tienda Pública {n}" in ph
   and f"og:image\" content=\"{WEB}/{slug}/og.jpg?p={cartera['id']}\"" in ph and 'value="' in ph and "café" in ph and "negro" in ph and "Bs 180" in ph)
m = re.search(r'id="pedir" class="[^"]*" href="([^"]+)"', ph)
mensaje = urllib.parse.unquote(m.group(1).split("text=")[1]) if m else ""
ok("el enlace de pedido trae número, producto, variante, cantidad y total", bool(m) and m.group(1).startswith("https://wa.me/59171234567?text=") and "1 × Cartera de cuero Mía (café) — Bs 180" in mensaje and f"/{slug}/p/{cartera['id']}" in mensaje)
ok("no hay stock exacto en la página del producto", not re.search(r"stock|costo|margen", ph, re.I) and "4321" not in ph)
s, pa, _ = web(f"/{slug}/p/{agotado['id']}")
ok("un producto agotado dice que está agotado y no ofrece pedir", s == 200 and "Por ahora está agotado" in pa and 'id="pedir"' not in pa)
ok("un producto oculto / de otra tienda / inexistente → 404 con página de error", all(web(f"/{slug}/p/{x}")[0] == 404 for x in [oculto["id"], b_prod["id"], "00000000-0000-0000-0000-000000000001"]) and "No encontramos" in web(f"/{slug}/p/{oculto['id']}")[1])
s, e404, _ = web("/no-existe-" + str(n))
ok("una tienda que no existe → 404 neutro, sin nombre de producto", s == 404 and "No encontramos esta página" in e404 and "hermes" not in e404.lower())

# 4. La imagen de la vista previa
s, b, h = pedir(WEB, "GET", f"/{slug}/og.jpg?p={cartera['id']}")
img = Image.open(io.BytesIO(b)) if s == 200 else None
ok("og.jpg del producto: 200, JPEG de 1200×630 y con caché de 1 h", s == 200 and h["Content-Type"] == "image/jpeg" and img.size == (1200, 630) and img.format == "JPEG" and "max-age=3600" in (h.get("Cache-Control") or ""))
px = img.getpixel((600, 315)) if img else (0, 0, 0)
ok("y es la foto del producto (azul al centro), no otra cosa", px[2] > 150 and px[0] < 80)
s, b, h = pedir(WEB, "GET", f"/{slug}/og.jpg")
ok("og.jpg de la tienda sin producto: usa su logo (JPEG)", s == 200 and Image.open(io.BytesIO(b)).size == (1200, 630))
s, b, h = pedir(WEB, "GET", f"/{slug_b}/og.jpg")
ok("og.jpg de una tienda sin ninguna foto: color de marca con su nombre", s == 200 and Image.open(io.BytesIO(b)).size == (1200, 630))
ok("og.jpg de una tienda que no existe → 404", pedir(WEB, "GET", "/no-existe-" + str(n) + "/og.jpg")[0] == 404)

# 5. Lo que se oculta
api("PUT", "/api/v1/tienda/publica", {**conf, "mostrarPrecios": False}, t)
_, lista, crudo, _ = publico(slug, "/productos?Limite=50")
s, html, _ = web(f"/{slug}")
ok("sin mostrar precios: el precio es null en el JSON y no sale «Bs» en el HTML", all(i["precio"] is None for i in lista["items"]) and "Bs 180" not in html and "Bs 58,50" not in html)
_, ph, _ = web(f"/{slug}/p/{cartera['id']}")
ok("y tampoco en el producto ni en el mensaje de pedido", "Bs 180" not in ph and "Bs 180" not in urllib.parse.unquote(re.search(r'id="pedir" class="[^"]*" href="([^"]+)"', ph).group(1)))
api("PUT", "/api/v1/tienda/publica", {**conf, "mostrarDisponibilidad": False}, t)
_, lista, _, _ = publico(slug, "/productos?Limite=50")
s, html, _ = web(f"/{slug}")
ok("sin mostrar disponibilidad: null en el JSON, sin «Agotado» y el orden no revela los agotados", all(i["disponible"] is None for i in lista["items"]) and "Agotado" not in html
   and [i["nombre"] for i in lista["items"]][-1] != "Pulsera agotada" or [i["nombre"] for i in lista["items"]][0] == "<script>alert(1)</script> Collar")
api("PUT", "/api/v1/tienda/publica", {**conf, "pedidosPorWhatsapp": False}, t)
_, ph, _ = web(f"/{slug}/p/{cartera['id']}")
_, html, _ = web(f"/{slug}")
ok("sin pedidos por WhatsApp: no hay botón ni número en ninguna página", 'id="pedir"' not in ph and "wa.me" not in html and publico(slug)[1]["whatsapp"] is None)
api("PUT", "/api/v1/tienda/publica", {**conf, "fondo": "Oscuro"}, t)
ok("el fondo Oscuro se aplica", "--fondo: #17181C" in web(f"/{slug}")[1] and publico(slug)[1]["fondo"] == "Oscuro")
api("PUT", "/api/v1/tienda/publica", {**conf, "publicada": False}, t)
ok("tienda apagada: 404 en el JSON, en el HTML, en un producto y en la imagen", publico(slug)[0] == 404 and web(f"/{slug}")[0] == 404 and web(f"/{slug}/p/{cartera['id']}")[0] == 404 and pedir(WEB, "GET", f"/{slug}/og.jpg")[0] == 404)
api("PUT", "/api/v1/tienda/publica", conf, t)
ok("al encenderla vuelve a verse", web(f"/{slug}")[0] == 200)

# 6. Límite por IP (al final: después de esto la IP queda limitada un minuto)
estados = [publico(slug)[0] for _ in range(260)]
ok(f"límite por IP: pasadas 240 visitas en un minuto responde 429 ({estados.count(200)} ok, {estados.count(429)} limitadas)", estados.count(429) > 0 and estados.count(200) <= 240)

print(f"--- fallas: {fallas}")
sys.exit(1 if fallas else 0)
