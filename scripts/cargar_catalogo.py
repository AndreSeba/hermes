"""Carga inicial del catálogo desde una planilla de Google (respuestas de un formulario de compras) a una tienda de Hermes.

Columnas que espera (por nombre, como las genera el formulario): CÓDIGO DE PRODUCTO, NOMBRE DEL PRODUCTO, CATEGORÍA DEL PRODUCTO,
SUBE UNA FOTO DEL PRODUCTO (enlace de Drive), PRECIO DE COMPRA DEL PRODUCTO (pasa a ser el COSTO) y PRECIO DE VENTA DEL PRODUCTO.
La planilla tiene que estar compartida como "cualquiera con el enlace". Las fotos de Drive también.

Qué hace con los datos:
  - Decimales con coma ("11,5") → 11.5. Códigos con errores de tipeo ("ENV.-010") → "ENV-010". Espacios sobrantes, afuera.
  - La categoría "ENV – Envases, tuppers y frascos" queda como "Envases, tuppers y frascos" (el prefijo es el código).
  - El código va al final de la descripción ("Código: ENV-001") para poder buscarlo.
  - Sin stock en la planilla: por defecto "por encargo" (sin control de stock). Con --stock N se carga N unidades de cada uno.
  - NO crea compras (la planilla no trae cantidades) ni toca lo que ya existe: un producto con el mismo nombre se saltea.
Se puede repetir sin duplicar. Uso:
  python -I scripts/cargar_catalogo.py <enlace-de-la-planilla> --simular                # solo mira y muestra, no guarda nada
  python -I scripts/cargar_catalogo.py <enlace-de-la-planilla>                          # crea una tienda NUEVA y carga
  python -I scripts/cargar_catalogo.py <enlace> --cuenta scripts/catalogo.local.json    # usa esa cuenta (email/password) en vez de crear otra
Las credenciales de la tienda nueva se guardan en scripts/catalogo.local.json (ignorado por git), nunca se imprimen.
"""
import argparse, csv, io, json, os, re, secrets, sys, unicodedata, urllib.error, urllib.parse, urllib.request, uuid

sys.stdout.reconfigure(encoding="utf-8")
API = os.environ.get("HERMES_API", "http://localhost:5013")
AQUI = os.path.dirname(os.path.abspath(__file__))


def limpiar(t):
    return re.sub(r"\s+", " ", (t or "").strip())


def monto(texto):
    t = limpiar(texto).replace(",", ".")
    if not re.fullmatch(r"\d+(\.\d{1,2})?", t):
        raise ValueError(f"monto inválido: {texto!r}")
    return float(t)


def codigo(texto):
    c = limpiar(texto).upper().replace(" ", "")
    return re.sub(r"^([A-Z]+)[.\-_]+(\d+)$", r"\1-\2", c)  # "ENV.-010" → "ENV-010"


def categoria(texto):
    t = limpiar(texto)
    return re.sub(r"^[A-ZÁÉÍÓÚ]{2,5}\s*[–—-]\s*", "", t) or "Sin categoría"


def leer_planilla(enlace):
    m = re.search(r"/d/([A-Za-z0-9_-]+)", enlace)
    if not m:
        sys.exit("No reconozco el enlace de la planilla (tiene que tener /d/<id>/).")
    url = f"https://docs.google.com/spreadsheets/d/{m.group(1)}/export?format=csv"
    try:
        crudo = urllib.request.urlopen(url, timeout=60).read().decode("utf-8-sig")
    except urllib.error.HTTPError as e:
        sys.exit(f"No pude leer la planilla ({e.code}): ¿está compartida como 'cualquiera con el enlace'?")
    filas = list(csv.DictReader(io.StringIO(crudo)))
    if not filas:
        sys.exit("La planilla está vacía.")
    return filas


def columna(fila, *claves):
    """Busca la columna por una parte de su nombre, sin importar tildes ni mayúsculas."""
    def plano(s):
        return "".join(c for c in unicodedata.normalize("NFD", s.lower()) if unicodedata.category(c) != "Mn")
    for nombre in fila:
        if all(plano(k) in plano(nombre) for k in claves):
            return fila[nombre]
    raise KeyError(" ".join(claves))


def productos_de(filas):
    productos, avisos, vistos = [], [], {}
    for n, f in enumerate(filas, start=2):  # la fila 1 son los encabezados
        try:
            nombre = limpiar(columna(f, "nombre", "producto"))
            if not nombre:
                avisos.append(f"fila {n}: sin nombre, se saltea")
                continue
            cod_original = limpiar(columna(f, "codigo"))
            cod = codigo(cod_original)
            precio, costo = monto(columna(f, "precio", "venta")), monto(columna(f, "precio", "compra"))
        except (KeyError, ValueError) as e:
            avisos.append(f"fila {n}: {e}, se saltea")
            continue
        if cod != cod_original.upper().replace(" ", ""):
            avisos.append(f"fila {n}: el código {cod_original!r} se cargó como {cod!r}")
        if precio <= 0:
            avisos.append(f"fila {n} ({nombre}): precio de venta 0, se saltea")
            continue
        if costo > precio:
            avisos.append(f"fila {n} ({nombre}): el costo ({costo}) es mayor que el precio ({precio}): perdés plata")
        if nombre.lower() in vistos:
            avisos.append(f"fila {n}: {nombre!r} ya estaba en la fila {vistos[nombre.lower()]}, se saltea")
            continue
        vistos[nombre.lower()] = n
        foto = re.search(r"id=([A-Za-z0-9_-]+)", columna(f, "foto"))
        productos.append({"codigo": cod, "nombre": nombre[:120], "categoria": categoria(columna(f, "categoria")), "precio": precio, "costo": costo,
                          "foto_id": foto.group(1) if foto else None, "fila": n})
    return productos, avisos


# ───────────── API de Hermes ─────────────

def api(metodo, ruta, obj=None, token=None):
    h = {"Content-Type": "application/json"} if obj is not None else {}
    if token:
        h["Authorization"] = "Bearer " + token
    req = urllib.request.Request(API + ruta, json.dumps(obj).encode() if obj is not None else None, h, method=metodo)
    try:
        r = urllib.request.urlopen(req, timeout=60)
        b = r.read()
        return r.status, (json.loads(b) if b and b[:1] in b"{[" else None)
    except urllib.error.HTTPError as e:
        b = e.read()
        return e.code, (json.loads(b) if b and b[:1] in b"{[" else None)


def subir_foto(token, datos):
    frontera = "----carga" + uuid.uuid4().hex
    cuerpo = (f"--{frontera}\r\nContent-Disposition: form-data; name=\"archivo\"; filename=\"foto\"\r\nContent-Type: application/octet-stream\r\n\r\n").encode() \
        + datos + f"\r\n--{frontera}--\r\n".encode()
    req = urllib.request.Request(API + "/api/v1/imagenes", cuerpo, {"Content-Type": "multipart/form-data; boundary=" + frontera, "Authorization": "Bearer " + token}, method="POST")
    try:
        r = urllib.request.urlopen(req, timeout=120)
        return json.loads(r.read())["id"], None
    except urllib.error.HTTPError as e:
        try:
            return None, json.loads(e.read()).get("detail", f"error {e.code}")
        except Exception:
            return None, f"error {e.code}"


def bajar_foto(foto_id):
    """Una foto de Drive compartida con 'cualquiera con el enlace'. Devuelve (bytes, None) o (None, motivo)."""
    url = f"https://drive.google.com/uc?export=download&id={foto_id}"
    try:
        r = urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"}), timeout=90)
        tipo = r.headers.get("Content-Type", "")
        datos = r.read(20 * 1024 * 1024 + 1)
    except urllib.error.HTTPError as e:
        return None, f"Drive respondió {e.code}"
    except Exception as e:
        return None, f"no se pudo bajar ({type(e).__name__})"
    if len(datos) > 20 * 1024 * 1024:
        return None, "la foto pesa más de 20 MB"
    if "text/html" in tipo or datos[:15].lower().startswith((b"<!doctype", b"<html")):
        return None, "Drive pide permiso (la foto no está compartida con 'cualquiera con el enlace')"
    return datos, None


def entrar(args):
    ruta = os.path.abspath(args.cuenta or os.path.join(AQUI, "catalogo.local.json"))
    if os.path.exists(ruta):
        cred = json.load(open(ruta, encoding="utf-8"))
    else:
        if args.cuenta:
            sys.exit(f"No existe {args.cuenta}.")
        sufijo = secrets.token_hex(2)
        cred = {"email": f"catalogo-{sufijo}@ejemplo.com", "password": secrets.token_urlsafe(12) + "a1", "slug": f"menaje-{sufijo}", "nombreTienda": "Menaje y Decoración"}
        s, r = api("POST", "/api/v1/auth/registro", {"nombre": "Catálogo", "email": cred["email"], "password": cred["password"], "nombreTienda": cred["nombreTienda"], "slug": cred["slug"]})
        if s not in (200, 201):
            sys.exit(f"No pude crear la tienda: {r}")
        json.dump(cred, open(ruta, "w", encoding="utf-8"))
        print(f"Tienda nueva creada: /{cred['slug']} (las credenciales quedaron en {os.path.relpath(ruta)})")
    s, ses = api("POST", "/api/v1/auth/login", {"email": cred["email"], "password": cred["password"]})
    if s != 200:
        sys.exit(f"No pude entrar a la cuenta ({s}).")
    return ses["accessToken"], cred


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("enlace")
    ap.add_argument("--simular", action="store_true")
    ap.add_argument("--cuenta")
    ap.add_argument("--stock", type=int, default=0, help="cargar esta cantidad de unidades de cada producto (0 = por encargo, sin control de stock)")
    ap.add_argument("--sin-fotos", action="store_true")
    args = ap.parse_args()

    productos, avisos = productos_de(leer_planilla(args.enlace))
    cats = sorted({p["categoria"] for p in productos})
    print(f"{len(productos)} productos en {len(cats)} categorías: {', '.join(cats)}")
    for a in avisos:
        print("  AVISO", a)
    if args.simular:
        for p in productos[:5]:
            print(f"  ej.: {p['codigo']} · {p['nombre']} · {p['categoria']} · costo {p['costo']} · precio {p['precio']}")
        print("Simulación: no se guardó nada.")
        return

    token, cred = entrar(args)
    ids_cat = {c["nombre"].lower(): c["id"] for c in (api("GET", "/api/v1/categorias", token=token)[1] or [])}
    for c in cats:
        if c.lower() not in ids_cat:
            s, r = api("POST", "/api/v1/categorias", {"nombre": c}, token)
            if s not in (200, 201):
                sys.exit(f"No pude crear la categoría {c!r}: {r}")
            ids_cat[c.lower()] = r["id"]
    existentes = set()
    cursor = None
    while True:
        _, pag = api("GET", "/api/v1/productos?Limite=60" + (f"&Cursor={cursor}" if cursor else ""), token=token)
        existentes |= {p["nombre"].lower() for p in pag["items"]}
        cursor = pag.get("siguiente")
        if not cursor:
            break

    creados = saltados = sin_foto = 0
    problemas = []
    for i, p in enumerate(productos, start=1):
        if p["nombre"].lower() in existentes:
            saltados += 1
            continue
        foto_ids = []
        if p["foto_id"] and not args.sin_fotos:
            datos, motivo = bajar_foto(p["foto_id"])
            if datos:
                fid, motivo = subir_foto(token, datos)
                if fid:
                    foto_ids = [fid]
            if not foto_ids:
                sin_foto += 1
                problemas.append(f"{p['codigo']} {p['nombre']}: sin foto ({motivo})")
        s, r = api("POST", "/api/v1/productos", {
            "nombre": p["nombre"], "descripcion": f"Código: {p['codigo']}", "precio": p["precio"], "costo": p["costo"],
            "categoriaId": ids_cat[p["categoria"].lower()], "visibleEnTienda": True, "colores": None, "tallas": None,
            "fotoIds": foto_ids or None, "controlaStock": args.stock > 0}, token)
        if s != 201:
            problemas.append(f"{p['codigo']} {p['nombre']}: no se creó ({(r or {}).get('detail', s)})")
            for f in foto_ids:
                api("DELETE", f"/api/v1/imagenes/{f}", token=token)
            continue
        creados += 1
        if args.stock > 0:
            api("POST", "/api/v1/stock/ingresos", {"productoId": r["id"], "lineas": [{"varianteId": v["id"], "cantidad": args.stock} for v in r["variantes"]]}, token)
        if i % 10 == 0:
            print(f"  {i}/{len(productos)}…")
    print(f"\nListo: {creados} creados, {saltados} ya existían, {sin_foto} sin foto.")
    for pr in problemas:
        print("  PROBLEMA", pr)
    print(f"Tienda pública: http://localhost:5014/{cred['slug']}")


main()
