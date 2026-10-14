"""Prueba de punta a punta de M2 (imágenes) contra la API real.

Necesita la API corriendo (perfil http, puerto 5013) y Python con Pillow (`pip install pillow`).
Solo para desarrollo: Pillow NO es dependencia del proyecto. Crea dos cuentas de prueba en hermes_dev.
Uso: python -I scripts/e2e_imagenes.py   (31 verificaciones)
"""
import io, json, random, sys, urllib.request, urllib.error
from PIL import Image, ImageDraw

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
        return r.status, r.read(), r.headers
    except urllib.error.HTTPError as e:
        return e.code, e.read(), e.headers


def json_post(ruta, obj, token=None):
    s, b, h = pedir("POST", ruta, json.dumps(obj).encode(), token, {"Content-Type": "application/json"})
    return s, (json.loads(b) if b else None)


def subir(token, contenido, nombre="foto.jpg", tipo="image/jpeg"):
    limite = "----hermes" + str(random.randint(10**8, 10**9))
    cuerpo = (
        f"--{limite}\r\nContent-Disposition: form-data; name=\"archivo\"; filename=\"{nombre}\"\r\nContent-Type: {tipo}\r\n\r\n"
    ).encode() + contenido + f"\r\n--{limite}--\r\n".encode()
    s, b, h = pedir("POST", "/api/v1/imagenes", cuerpo, token, {"Content-Type": f"multipart/form-data; boundary={limite}"})
    return s, (json.loads(b) if b and b[:1] in b"{[" else None), b


def foto_de_celular():
    # 4000x3000 guardada "acostada", con orientación 6 (girar 90° a la derecha) y GPS de La Paz.
    # Bloque ROJO arriba a la izquierda de la imagen GUARDADA: tras girar debe quedar arriba a la DERECHA.
    # Textura en varias escalas (como una foto real: tela, cuero, sombras). Un solo color, o ruido
    # puro, no sirve para medir pesos: lo primero comprime a nada y lo segundo se promedia al achicar.
    img = Image.new("RGB", (4000, 3000), (30, 90, 200))
    for escala in (4, 8, 16, 32, 64, 128, 256):
        capa = Image.effect_noise((4000 // escala, 3000 // escala), 70).resize((4000, 3000), Image.BICUBIC)
        img = Image.blend(img, Image.merge("RGB", (capa, capa.rotate(90, expand=False), capa)), 0.22)
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, 1200, 900], fill=(230, 20, 20))
    exif = img.getexif()
    exif[0x0112] = 6
    gps = exif.get_ifd(0x8825)
    gps[1], gps[2], gps[3], gps[4] = "S", (16.0, 30.0, 0.0), "W", (68.0, 9.0, 0.0)
    out = io.BytesIO()
    img.save(out, "JPEG", quality=90, exif=exif.tobytes())
    return out.getvalue()


n = random.randint(10000, 99999)
s, sesion = json_post("/api/v1/auth/registro", {"nombre": "Foto Prueba", "email": f"foto{n}@ejemplo.com", "password": "secreta123", "nombreTienda": f"Fotos {n}", "slug": f"fotos-{n}"})
token = sesion["accessToken"]
tienda_a = sesion["tienda"]["id"]
s, sesion_b = json_post("/api/v1/auth/registro", {"nombre": "Otra", "email": f"otra{n}@ejemplo.com", "password": "secreta123", "nombreTienda": f"Otra {n}", "slug": f"otra-{n}"})
token_b = sesion_b["accessToken"]

original = foto_de_celular()
ok(f"la foto original trae GPS y pesa {len(original)//1024} KB", b"Exif" in original)

# 1. Subida sin sesión
s, _, _ = subir(None, original)
ok("sin sesión no se puede subir (401)", s == 401)

# 2. Subida correcta
s, dto, _ = subir(token, original)
ok("subir la foto responde 201", s == 201 and dto is not None)
ok("el DTO trae las tres direcciones de la tienda", dto and all(dto[k].startswith(f"/archivos/{tienda_a}/imagenes/{dto['id']}/") for k in ("miniatura", "catalogo", "detalle")))
ok(f"ancho/alto ya con la orientación corregida ({dto['ancho']}x{dto['alto']})", dto and (dto["ancho"], dto["alto"]) == (3000, 4000))

# 3. Los tres archivos se sirven sin login, livianos, en WEBP y con caché de un año
medidas = {}
for k, lado in (("miniatura", 200), ("catalogo", 600), ("detalle", 1200)):
    s, cuerpo, h = pedir("GET", dto[k])
    img = Image.open(io.BytesIO(cuerpo))
    medidas[k] = img.size
    ok(f"{k}: 200, image/webp, lado largo {lado} ({img.size[0]}x{img.size[1]}, {len(cuerpo)//1024} KB)",
       s == 200 and h["Content-Type"] == "image/webp" and max(img.size) == lado and img.format == "WEBP")
    ok(f"{k}: caché inmutable de un año y nosniff", "max-age=31536000" in h["Cache-Control"] and "immutable" in h["Cache-Control"] and h["X-Content-Type-Options"] == "nosniff")
    ok(f"{k}: sin metadatos ni GPS", b"EXIF" not in cuerpo and b"XMP" not in cuerpo and not img.info.get("exif"))
    if k == "detalle":
        detalle = img.convert("RGB")
ok("la proporción vertical se mantuvo (900x1200)", medidas["detalle"] == (900, 1200))
ancho = detalle.size[0]
arriba_der, arriba_izq = detalle.getpixel((ancho - 40, 40)), detalle.getpixel((40, 40))
ok(f"el bloque rojo giró a arriba-DERECHA {arriba_der} y no queda arriba-izquierda {arriba_izq}",
   arriba_der[0] > 180 and arriba_der[1] < 90 and not (arriba_izq[0] > 180 and arriba_izq[1] < 90))

# 4. Archivos que no son fotos
s, _, b = subir(token, b"esto es texto pero se llama foto.jpg")
ok("texto con extensión .jpg → 400 con mensaje", s == 400 and b"JPG, PNG o WEBP" in b)
s, _, b = subir(token, b"MZ\x90\x00\x03\x00\x00\x00\x04\x00\x00\x00", "virus.jpg")
ok("ejecutable disfrazado → 400", s == 400)
s, _, b = subir(token, b"")
ok("archivo vacío → 400", s == 400)
s, _, b = subir(token, original[:3000])
ok("foto cortada → 400", s == 400)
s, _, b = subir(token, b"\xff\xd8\xff" + b"\0" * (16 * 1024 * 1024))
ok("más de 15 MB → rechazada (400/413)", s in (400, 413))

# 5. Rutas de archivos: solo la forma esperada
ok("clave inventada → 404", pedir("GET", "/archivos/loquesea.webp")[0] == 404)
ok("clave con la forma pero inexistente → 404", pedir("GET", f"/archivos/{tienda_a}/imagenes/{tienda_a}/detalle.webp")[0] == 404)
ok("intento de salirse de la carpeta → 404", pedir("GET", f"/archivos/{tienda_a}/imagenes/..%2f..%2fappsettings.json")[0] == 404)

# 6. Aislamiento: otra tienda no puede borrar mi foto
s, _, _ = pedir("DELETE", f"/api/v1/imagenes/{dto['id']}", None, token_b)
ok("otra tienda no puede borrarla (404)", s == 404)
ok("mi foto sigue ahí", pedir("GET", dto["detalle"])[0] == 200)

# 7. Borrar
s, _, _ = pedir("DELETE", f"/api/v1/imagenes/{dto['id']}", None, token)
ok("la dueña la borra (204)", s == 204)
ok("sus tres archivos ya no existen (404)", all(pedir("GET", dto[k])[0] == 404 for k in ("miniatura", "catalogo", "detalle")))
s, _, _ = pedir("DELETE", f"/api/v1/imagenes/{dto['id']}", None, token)
ok("borrarla otra vez → 404", s == 404)

# 8. PNG con transparencia y WEBP también entran
png = io.BytesIO(); Image.new("RGBA", (800, 600), (0, 200, 0, 120)).save(png, "PNG")
s, d2, _ = subir(token, png.getvalue(), "logo.png", "image/png")
ok("PNG con transparencia → 201", s == 201)
wb = io.BytesIO(); Image.new("RGB", (800, 600), (200, 100, 0)).save(wb, "WEBP")
s, d3, _ = subir(token, wb.getvalue(), "foto.webp", "image/webp")
ok("WEBP → 201", s == 201)

print(f"--- fallas: {fallas}")
sys.exit(1 if fallas else 0)
