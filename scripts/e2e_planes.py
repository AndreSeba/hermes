"""Prueba de punta a punta de M13 (planes, límites y panel de administración) contra la API real y Postgres.

Cubre lo que las pruebas unitarias no pueden: el rol de administrador en el JWT real (403 para quien no lo es), los 402 por
HTTP, el comprobante subido por multipart y las peticiones simultáneas.
Necesita la API corriendo (perfil http, puerto 5013). Solo para desarrollo.
El administrador sale de scripts/admin.local.json (gitignored); si no existe se crea la cuenta y se la promueve con el comando
`promover-admin` (el rol NUNCA se da por la web). Uso: python -I scripts/e2e_planes.py
"""
import json, os, random, secrets, struct, subprocess, sys, uuid, zlib, urllib.request, urllib.error
from concurrent.futures import ThreadPoolExecutor

sys.stdout.reconfigure(encoding="utf-8")
BASE = "http://localhost:5013"
RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
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


def png(ancho=120, alto=90):
    def trozo(t, d):
        c = struct.pack(">I", len(d)) + t + d
        return c + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)
    filas = b"".join(b"\x00" + bytes(random.randrange(256) for _ in range(ancho * 3)) for _ in range(alto))
    return b"\x89PNG\r\n\x1a\n" + trozo(b"IHDR", struct.pack(">IIBBBBB", ancho, alto, 8, 2, 0, 0, 0)) + trozo(b"IDAT", zlib.compress(filas)) + trozo(b"IEND", b"")


def subir(token, comprobante, ancho=120, alto=90):
    frontera = "----e2e" + uuid.uuid4().hex
    cuerpo = (f"--{frontera}\r\nContent-Disposition: form-data; name=\"archivo\"; filename=\"foto.png\"\r\nContent-Type: image/png\r\n\r\n").encode() \
        + png(ancho, alto) + f"\r\n--{frontera}--\r\n".encode()
    ruta = "/api/v1/imagenes" + ("?comprobante=true" if comprobante else "")
    req = urllib.request.Request(BASE + ruta, cuerpo, {"Content-Type": "multipart/form-data; boundary=" + frontera, "Authorization": "Bearer " + token}, method="POST")
    try:
        r = urllib.request.urlopen(req)
        return r.status, json.loads(r.read())
    except urllib.error.HTTPError as e:
        b = e.read()
        return e.code, (json.loads(b) if b and b[:1] in b"{[" else None)


def producto(nombre):
    return {"nombre": nombre, "descripcion": None, "precio": 100, "costo": 0, "categoriaId": None,
            "visibleEnTienda": True, "colores": None, "tallas": None, "fotoIds": None, "controlaStock": False}


def registrar(prefijo):
    n = random.randint(100000, 999999)
    email, clave = f"{prefijo}{n}@ejemplo.com", secrets.token_urlsafe(12) + "a1"
    s, ses = api("POST", "/api/v1/auth/registro", {"nombre": prefijo, "email": email, "password": clave, "nombreTienda": f"{prefijo} {n}", "slug": f"{prefijo}-{n}"})
    assert s in (200, 201), s
    return email, clave, ses["accessToken"], f"{prefijo}-{n}"


# ── El administrador de pruebas ──
ruta_admin = os.path.join(RAIZ, "scripts", "admin.local.json")
if os.path.exists(ruta_admin):
    cred = json.load(open(ruta_admin, encoding="utf-8"))
else:
    email, clave, _, _ = registrar("admin")
    subprocess.run(["dotnet", "run", "--no-build", "--project", os.path.join(RAIZ, "src", "Hermes.Api"), "--", "promover-admin", email], check=True, capture_output=True)
    cred = {"email": email, "password": clave}
    json.dump(cred, open(ruta_admin, "w", encoding="utf-8"))
s, ses = api("POST", "/api/v1/auth/login", cred)
ok("el administrador inicia sesión y la sesión lo marca como administrador", s == 200 and ses["usuario"]["esAdmin"] is True)
ta = ses["accessToken"]

_, _, td, slug_d = registrar("duena")
_, _, tb, slug_b = registrar("otra")


def id_de(slug):
    s, p = api("GET", f"/api/v1/admin/tiendas?Buscar={slug}", token=ta)
    return p["items"][0]["id"]


tienda_d, tienda_b = id_de(slug_d), id_de(slug_b)
plan_ids = {p["codigo"]: p["id"] for p in api("GET", "/api/v1/admin/planes", token=ta)[1]}

# 1. Quién entra al panel
ok("sin sesión no entra al panel (401)", api("GET", "/api/v1/admin/pagos")[0] == 401)
ok("una dueña común no entra al panel (403)", api("GET", "/api/v1/admin/pagos", token=td)[0] == 403)
ok("ni a las tiendas, ni a asignar planes (403)", api("GET", "/api/v1/admin/tiendas", token=td)[0] == 403
   and api("PUT", f"/api/v1/admin/tiendas/{tienda_d}/plan", {"planId": plan_ids["negocio"]}, td)[0] == 403)
ok("la sesión de una dueña no dice que es administradora", api("GET", "/api/v1/auth/yo", token=td)[1]["usuario"]["esAdmin"] is False)

# 2. Planes y límite
s, planes = api("GET", "/api/v1/planes", token=td)
ok("la lista de planes trae los 3, los meses y cómo pagar", s == 200 and [p["codigo"] for p in planes["planes"]] == ["gratis", "emprendedor", "negocio"] and planes["meses"] == [1, 3, 6, 12] and planes["instruccionesDePago"])
s, mi = api("GET", "/api/v1/plan", token=td)
ok("en desarrollo una tienda nueva tiene el plan por defecto de la configuración (Negocio)", s == 200 and mi["plan"]["codigo"] == "negocio")
s, a = api("PUT", f"/api/v1/admin/tiendas/{tienda_d}/plan", {"planId": plan_ids["gratis"]}, ta)
ok("el administrador la pasa al Gratis", s == 200 and a["plan"] == "Gratis")
for i in range(15):
    s, _ = api("POST", "/api/v1/productos", producto(f"P{i}"), td)
    if s != 201:
        break
ok("el plan Gratis deja crear 15 productos", s == 201)
s, e = api("POST", "/api/v1/productos", producto("El 16"), td)
ok("el 16 da 402 con el mensaje del límite", s == 402 and "15 productos" in (e or {}).get("detail", ""))
ok("el límite es por tienda: la otra sigue creando", api("POST", "/api/v1/productos", producto("Libre"), tb)[0] == 201)

# 3. Pedir un plan
ok("sin comprobante real el pedido se rechaza (400)", api("POST", "/api/v1/plan/solicitar", {"planId": plan_ids["emprendedor"], "meses": 1, "metodo": "Qr", "comprobanteImagenId": str(uuid.uuid4())}, td)[0] == 400)
s, img = subir(td, True)
ok("el comprobante se sube con ?comprobante=true", s in (200, 201) and img["id"])
pedido = {"planId": plan_ids["emprendedor"], "meses": 3, "metodo": "Transferencia", "comprobanteImagenId": img["id"]}
ok("el plan gratis no se puede pedir (400)", api("POST", "/api/v1/plan/solicitar", {**pedido, "planId": plan_ids["gratis"]}, td)[0] == 400)
ok("meses que no se ofrecen (400)", api("POST", "/api/v1/plan/solicitar", {**pedido, "meses": 2}, td)[0] == 400)
ok("el comprobante de otra tienda no sirve (400)", api("POST", "/api/v1/plan/solicitar", {**pedido, "comprobanteImagenId": subir(tb, True)[1]["id"]}, td)[0] == 400)
s, p = api("POST", "/api/v1/plan/solicitar", pedido, td)
ok("el pedido queda pendiente con el monto de 3 meses (201)", s == 201 and p["estado"] == "Pendiente" and p["monto"] == 117 and p["comprobante"])
ok("un segundo pedido mientras hay uno pendiente da 409", api("POST", "/api/v1/plan/solicitar", {**pedido, "comprobanteImagenId": subir(td, True)[1]["id"]}, td)[0] == 409)
mi = api("GET", "/api/v1/plan", token=td)[1]
ok("Mi plan muestra el pendiente y sigue en Gratis", mi["pendiente"]["id"] == p["id"] and mi["plan"]["codigo"] == "gratis" and mi["productos"] == 15)
ok("la otra tienda no ve ese pago", api("GET", "/api/v1/plan/pagos", token=tb)[1] == [] and api("GET", "/api/v1/plan", token=tb)[1]["pendiente"] is None)

# 4. El administrador resuelve
s, cola = api("GET", "/api/v1/admin/pagos", token=ta)
mio = next((x for x in cola["items"] if x["pago"]["id"] == p["id"]), None)
ok("el pedido aparece en la cola con tienda, email y comprobante", s == 200 and mio and mio["slug"] == slug_d and mio["email"] and mio["pago"]["comprobante"]["detalle"])
url_comp = mio["pago"]["comprobante"]["detalle"] if mio else None
ok("el administrador puede ver la foto del comprobante", url_comp and urllib.request.urlopen(BASE + url_comp if url_comp.startswith("/") else url_comp).status == 200)
s, r = api("POST", f"/api/v1/admin/pagos/{p['id']}/rechazar", {"motivo": " "}, ta)
ok("rechazar sin motivo da 400", s == 400)
s, r = api("POST", f"/api/v1/admin/pagos/{p['id']}/aprobar", None, td)
ok("una dueña no puede aprobar su propio pago (403)", s == 403)

# aprobaciones simultáneas
with ThreadPoolExecutor(5) as ex:
    codigos = sorted(f.result()[0] for f in [ex.submit(api, "POST", f"/api/v1/admin/pagos/{p['id']}/aprobar", None, ta) for _ in range(5)])
ok("cinco aprobaciones simultáneas: gana una (200) y el resto da 409", codigos == [200, 409, 409, 409, 409])
mi = api("GET", "/api/v1/plan", token=td)[1]
ok("el plan Emprendedor queda activo con vencimiento y sin pendiente", mi["plan"]["codigo"] == "emprendedor" and mi["hasta"] and mi["pendiente"] is None)
ok("ahora se pueden crear más de 15 productos", api("POST", "/api/v1/productos", producto("El 16 ahora sí"), td)[0] == 201)
ok("el historial de la dueña muestra el pago aprobado", api("GET", "/api/v1/plan/pagos", token=td)[1][0]["estado"] == "Aprobado")

# 5. Rechazo
s, img2 = subir(tb, True)
s, p2 = api("POST", "/api/v1/plan/solicitar", {"planId": plan_ids["negocio"], "meses": 1, "metodo": "Qr", "comprobanteImagenId": img2["id"]}, tb)
s, r = api("POST", f"/api/v1/admin/pagos/{p2['id']}/rechazar", {"motivo": "El monto no coincide"}, ta)
ok("rechazar con motivo: 200", s == 200 and r["pago"]["estado"] == "Rechazado")
h = api("GET", "/api/v1/plan/pagos", token=tb)[1]
ok("la dueña ve el motivo y puede pedir de nuevo", h[0]["motivo"] == "El monto no coincide" and api("GET", "/api/v1/plan", token=tb)[1]["pendiente"] is None)
ok("rechazar uno ya resuelto da 409", api("POST", f"/api/v1/admin/pagos/{p2['id']}/rechazar", {"motivo": "Otra vez"}, ta)[0] == 409)
ok("un pago que no existe da 404", api("POST", f"/api/v1/admin/pagos/{uuid.uuid4()}/aprobar", None, ta)[0] == 404)

# 6. Espacio de fotos y edición de planes
ok("editar un plan: el Gratis no puede dejar de ser gratis (400)", api("PUT", f"/api/v1/admin/planes/{plan_ids['gratis']}", {"precioMensual": 5, "maxProductos": 15, "maxAlmacenamientoMb": 100, "activo": True}, ta)[0] == 400)
s, u = api("PUT", f"/api/v1/admin/planes/{plan_ids['gratis']}", {"precioMensual": 0, "maxProductos": 15, "maxAlmacenamientoMb": 1, "activo": True}, ta)
ok("editar el espacio del Gratis: 200", s == 200 and u["maxAlmacenamientoMb"] == 1)
api("PUT", f"/api/v1/admin/tiendas/{tienda_b}/plan", {"planId": plan_ids["gratis"]}, ta)
resultados = []
while len(resultados) < 30 and 402 not in resultados:   # fotos de ruido de 600×450: cada una pesa decenas de KB
    resultados.append(subir(tb, False, 600, 450)[0])
comp = subir(tb, True)
if 402 not in resultados or comp[0] not in (200, 201):
    print("   subidas:", resultados, "comprobante:", comp)
ok("la tienda con 1 MB se queda sin espacio (402) y el comprobante igual se sube", 402 in resultados and comp[0] in (200, 201))
api("PUT", f"/api/v1/admin/planes/{plan_ids['gratis']}", {"precioMensual": 0, "maxProductos": 15, "maxAlmacenamientoMb": 100, "activo": True}, ta)

# 7. Tiendas
s, lista = api("GET", f"/api/v1/admin/tiendas?Buscar={slug_d}", token=ta)
t = lista["items"][0]
ok("el panel de tiendas muestra plan, vencimiento y uso", s == 200 and t["plan"] == "Emprendedor" and t["hasta"] and t["productos"] >= 16)

# Dejar a la dueña de pruebas como estaba la configuración (no queda ningún plan especial colgado en otras tiendas).
print(f"\n{'TODO BIEN' if fallas == 0 else str(fallas) + ' FALLAS'}")
sys.exit(1 if fallas else 0)




