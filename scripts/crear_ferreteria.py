"""Crea una tienda de DEMOSTRACIÓN "Ferretería El Tornillo" en la base de desarrollo y simula movimiento: catálogo con
variantes, proveedores, compras (recibidas, pagadas, en camino), ventas con distintos métodos de pago (una anulada),
gastos y pedidos de la tienda pública (confirmado, entregado, cancelado, nuevo). Deja casos límite a propósito: producto
sin costo, con pérdida, con margen bajo, agotado, por agotarse y "por encargo".
Es solo para desarrollo. Las credenciales se guardan en `scripts/ferreteria.local.json` (ignorado por git, no se imprimen).
Necesita la API (5013) y el sitio público (5014). Uso: python -I scripts/crear_ferreteria.py
Si el archivo ya existe, solo vuelve a entrar y agrega más ventas (no repite el catálogo).
"""
import io, json, os, random, secrets, sys, uuid, urllib.request, urllib.error
from PIL import Image, ImageDraw

sys.stdout.reconfigure(encoding="utf-8")
API, WEB = "http://localhost:5013", "http://localhost:5014"
ARCHIVO = os.path.join(os.path.dirname(__file__), "ferreteria.local.json")
random.seed()


def pedir(base, metodo, ruta, cuerpo=None, token=None, cabeceras=None):
    h = dict(cabeceras or {})
    if token:
        h["Authorization"] = "Bearer " + token
    req = urllib.request.Request(base + ruta, data=cuerpo, headers=h, method=metodo)
    try:
        r = urllib.request.urlopen(req)
        return r.status, r.read()
    except urllib.error.HTTPError as e:
        return e.code, e.read()


def api(metodo, ruta, obj=None, token=None, base=API):
    s, b = pedir(base, metodo, ruta, json.dumps(obj).encode() if obj is not None else None, token,
                 {"Content-Type": "application/json"} if obj is not None else {})
    return s, (json.loads(b) if b and b[:1] in b"{[" else None)


def exigir(r, que):
    if r[0] not in (200, 201, 204):
        sys.exit(f"Falló {que}: estado {r[0]} {r[1]}")
    return r[1]


def foto(token, color, texto):
    img = Image.new("RGB", (900, 900), color)
    d = ImageDraw.Draw(img)
    d.rectangle([200, 200, 700, 700], fill=(255, 255, 255))
    d.text((430, 440), texto, fill=color)
    buf = io.BytesIO()
    img.save(buf, "JPEG", quality=85)
    limite = "----hermes" + str(random.randint(10**8, 10**9))
    cuerpo = (f"--{limite}\r\nContent-Disposition: form-data; name=\"archivo\"; filename=\"f.jpg\"\r\nContent-Type: image/jpeg\r\n\r\n").encode() \
        + buf.getvalue() + f"\r\n--{limite}--\r\n".encode()
    s, b = pedir(API, "POST", "/api/v1/imagenes", cuerpo, token, {"Content-Type": f"multipart/form-data; boundary={limite}"})
    return json.loads(b)["id"]


# ── Sesión ──
if os.path.exists(ARCHIVO):
    cred = json.load(open(ARCHIVO, encoding="utf-8"))
    s, ses = api("POST", "/api/v1/auth/login", {"email": cred["email"], "password": cred["password"]})
    nuevo = False
else:
    cred = {"email": f"ferreteria{random.randint(1000, 9999)}@ejemplo.com", "password": secrets.token_urlsafe(12) + "a1",
            "slug": f"ferreteria-{random.randint(100, 999)}"}
    s, ses = api("POST", "/api/v1/auth/registro", {"nombre": "Don Marcelo", "email": cred["email"], "password": cred["password"],
                                                   "nombreTienda": "Ferretería El Tornillo", "slug": cred["slug"]})
    json.dump(cred, open(ARCHIVO, "w", encoding="utf-8"))
    nuevo = True
if s not in (200, 201):
    sys.exit(f"No se pudo entrar o crear la cuenta (estado {s}). ¿La API está corriendo?")
t = ses["accessToken"]
exigir(api("PUT", "/api/v1/tienda", {"nombre": "Ferretería El Tornillo", "descripcion": "Herramientas, tornillería, pintura y materiales eléctricos · Cochabamba",
                                      "whatsapp": "71234567", "color": "#B45309", "logoImagenId": None}, t), "datos de la tienda")
slug = api("GET", "/api/v1/tienda", token=t)[1]["slug"]


def productos_vendibles():
    out, cursor = [], None
    while True:
        s, r = api("GET", "/api/v1/ventas/productos?Limite=60" + (f"&Cursor={cursor}" if cursor else ""), token=t)
        out += r["items"]
        cursor = r.get("siguiente")
        if not cursor:
            return out


def vender(n):
    """n ventas al azar sobre lo que tiene stock; devuelve las que salieron."""
    hechas = []
    metodos = ["Efectivo"] * 5 + ["Qr"] * 3 + ["Transferencia", "Tarjeta"]
    for _ in range(n):
        vendibles = [v for v in productos_vendibles() if v.get("variantes")]
        con_stock = [(p, v) for p in vendibles for v in p["variantes"] if v.get("stock") is None or v["stock"] > 0]
        if not con_stock:
            break
        lineas = {}
        for p, v in random.sample(con_stock, min(len(con_stock), random.choice([1, 1, 2, 3]))):
            tope = 5 if v.get("stock") is None else min(5, v["stock"])
            lineas[v["id"]] = random.randint(1, tope)
        body = {"claveIdempotencia": str(uuid.uuid4()), "metodoPago": random.choice(metodos),
                "cliente": random.choice([None, None, "Constructora Andes", "Juan Pérez", "Maestro Luis", "Doña Carmen"]),
                "lineas": [{"varianteId": k, "cantidad": c} for k, c in lineas.items()]}
        s, v = api("POST", "/api/v1/ventas", body, t)
        if s in (200, 201):
            hechas.append(v)
    return hechas


if not nuevo:
    print(f"La ferretería ya existe (/{slug}). Agrego 25 ventas más.")
    print(f"Ventas nuevas: {len(vender(25))}")
    sys.exit(0)

# ── Catálogo ──
cats = {n: exigir(api("POST", "/api/v1/categorias", {"nombre": n}, t), "categoría " + n) for n in
        ["Herramientas", "Tornillería", "Pintura", "Eléctrico", "Plomería"]}
cats = {k: v["id"] for k, v in cats.items()}

# (nombre, categoría, precio, costo, colores, tallas, color de foto, stock minimo, control de stock, stock inicial)
CATALOGO = [
    ("Martillo de uña 16 oz", "Herramientas", 55, 34, None, None, (120, 60, 30), 3, True, 24),
    ("Destornillador Phillips", "Herramientas", 18, 9, None, ["PH1", "PH2", "PH3"], (150, 40, 40), 3, True, 30),
    ("Alicate universal 8\"", "Herramientas", 48, 29, None, None, (60, 60, 120), 3, True, 15),
    ("Taladro percutor 650 W", "Herramientas", 420, 330, None, None, (40, 40, 40), 2, True, 6),
    ("Llave inglesa ajustable", "Herramientas", 65, 40, None, ["8\"", "10\"", "12\""], (90, 90, 90), 3, True, 18),
    ("Cinta métrica 5 m", "Herramientas", 22, 12, None, None, (230, 190, 30), 3, True, 40),
    ("Tornillo autoperforante (caja 100)", "Tornillería", 25, 14, None, ["1\"", "1 1/2\"", "2\""], (130, 130, 140), 5, True, 60),
    ("Clavos de 2\" (kg)", "Tornillería", 14, 9, None, None, (100, 100, 100), 5, True, 50),
    ("Tarugos plásticos (bolsa 50)", "Tornillería", 8, 4, None, ["6 mm", "8 mm", "10 mm"], (200, 200, 190), 5, True, 45),
    ("Pintura látex 4 L", "Pintura", 160, 118, ["blanco", "crema", "gris"], None, (210, 210, 200), 3, True, 20),
    ("Rodillo para pintar 9\"", "Pintura", 28, 15, None, None, (70, 130, 180), 3, True, 16),
    ("Brocha 3\"", "Pintura", 15, 8, None, None, (180, 120, 60), 3, True, 25),
    ("Thinner (litro)", "Pintura", 20, 18, None, None, (100, 150, 100), 3, True, 12),   # margen bajo
    ("Cable eléctrico 2,5 mm (rollo 100 m)", "Eléctrico", 380, 300, ["rojo", "negro", "azul"], None, (200, 40, 40), 2, True, 9),
    ("Foco LED 12 W", "Eléctrico", 12, 6, None, None, (240, 220, 120), 5, True, 80),
    ("Tomacorriente doble", "Eléctrico", 16, 9, None, None, (230, 230, 230), 3, True, 35),
    ("Cinta aislante", "Eléctrico", 6, 3, None, None, (30, 30, 30), 5, True, 60),
    ("Tubo PVC 1/2\" (3 m)", "Plomería", 24, 15, None, None, (240, 240, 240), 3, True, 30),
    ("Llave de paso 1/2\"", "Plomería", 38, 40, None, None, (190, 150, 60), 3, True, 10),  # pierde
    ("Teflón", "Plomería", 3, 0, None, None, (250, 250, 250), 5, True, 100),              # sin costo
    ("Candado de seguridad 40 mm", "Herramientas", 45, 28, None, None, (200, 160, 30), 3, True, 4),  # por agotarse
    ("Sierra de mano", "Herramientas", 35, 21, None, None, (160, 60, 60), 3, True, 5),      # se agota con las ventas
    ("Corte de vidrio a medida", "Herramientas", 80, 0, None, None, (150, 200, 220), 3, False, 0),   # por encargo
]
ids = {}
for nombre, cat, precio, costo, colores, tallas, col, minimo, control, _ in CATALOGO:
    body = {"nombre": nombre, "descripcion": f"{nombre}. Disponible en nuestra ferretería.", "precio": precio, "costo": costo,
            "categoriaId": cats[cat], "visibleEnTienda": True, "colores": colores, "tallas": tallas,
            "fotoIds": [foto(t, col, nombre[:1])], "controlaStock": control, "stockMinimo": minimo}
    ids[nombre] = exigir(api("POST", "/api/v1/productos", body, t), "producto " + nombre)
print(f"Catálogo: {len(ids)} productos.")

# ── Proveedores ──
provs = {}
for n, tel, correo, notas in [("Importadora Cochabamba", "70011122", "ventas@importadora-cbba.com", "Entrega en 2 días"),
                              ("Distribuidora Andina", "70033344", "pedidos@andina.com.bo", None),
                              ("Pinturas del Valle", "70055566", None, "Pedido mínimo Bs 500")]:
    provs[n] = exigir(api("POST", "/api/v1/proveedores", {"nombre": n, "telefono": tel, "correo": correo, "notas": notas}, t), "proveedor " + n)
print(f"Proveedores: {len(provs)}.")

# ── Compras: la mayoría llega y se paga; algunas quedan en camino o por pagar ──
reparto = {"Importadora Cochabamba": ["Herramientas", "Tornillería"], "Distribuidora Andina": ["Eléctrico", "Plomería"], "Pinturas del Valle": ["Pintura"]}
resumen = {"recibidas": 0, "en camino": 0}
for prov, categorias in reparto.items():
    lineas = []
    for nombre, cat, precio, costo, _, _, _, _, control, inicial in CATALOGO:
        if cat in categorias and control and costo > 0:
            for v in ids[nombre]["variantes"]:
                lineas.append({"varianteId": v["id"], "cantidad": max(1, inicial // len(ids[nombre]["variantes"])), "costoUnitario": costo})
    c = exigir(api("POST", "/api/v1/compras", {"claveIdempotencia": str(uuid.uuid4()), "proveedorId": provs[prov]["id"], "lineas": lineas,
                                               "notas": "Reposición inicial"}, t), "compra " + prov)
    exigir(api("POST", f"/api/v1/compras/{c['id']}/recibir", None, t), "recibir " + prov)
    resumen["recibidas"] += 1
    if prov != "Distribuidora Andina":   # a Distribuidora Andina se le queda debiendo
        exigir(api("POST", f"/api/v1/compras/{c['id']}/pagar", None, t), "pagar " + prov)

# Productos sin costo o sin compra: stock directo ("Llegaron más").
for nombre in ["Teflón"]:
    p = ids[nombre]
    exigir(api("POST", "/api/v1/stock/ingresos", {"productoId": p["id"], "lineas": [{"varianteId": v["id"], "cantidad": 100} for v in p["variantes"]]}, t), "stock " + nombre)

# Un pedido que sigue en camino y otro viejo sin recibir.
c = exigir(api("POST", "/api/v1/compras", {"claveIdempotencia": str(uuid.uuid4()), "proveedorId": provs["Importadora Cochabamba"]["id"], "notas": "Pedido de la semana",
      "lineas": [{"varianteId": ids["Taladro percutor 650 W"]["variantes"][0]["id"], "cantidad": 4, "costoUnitario": 335}]}, t), "compra en camino")
resumen["en camino"] += 1
print(f"Compras: {resumen['recibidas']} recibidas (una sin pagar), {resumen['en camino']} en camino.")

# ── Ventas ──
vendidas = vender(50)
# Dejar agotada la sierra y casi agotado el candado.
for nombre, dejar in [("Sierra de mano", 0), ("Candado de seguridad 40 mm", 2)]:
    v = ids[nombre]["variantes"][0]["id"]
    stock = next(x for p in productos_vendibles() for x in p["variantes"] if x["id"] == v)["stock"]
    if stock - dejar > 0:
        s, venta = api("POST", "/api/v1/ventas", {"claveIdempotencia": str(uuid.uuid4()), "metodoPago": "Efectivo", "lineas": [{"varianteId": v, "cantidad": stock - dejar}]}, t)
        if s in (200, 201):
            vendidas.append(venta)
if vendidas:
    api("POST", f"/api/v1/ventas/{vendidas[3]['id']}/anular", None, t)   # una venta anulada
print(f"Ventas: {len(vendidas)} (una anulada).")

# ── Gastos ──
for concepto, monto in [("Alquiler del local", 1800), ("Luz y agua", 260), ("Sueldo ayudante", 1500), ("Bolsas y etiquetas", 85), ("Internet", 120)]:
    exigir(api("POST", "/api/v1/gastos", {"claveIdempotencia": str(uuid.uuid4()), "concepto": concepto, "monto": monto}, t), "gasto " + concepto)
print("Gastos: 5.")

# ── Pedidos desde la tienda pública ──
def pedido(articulos, nombre, celular, entrega="Retiro", direccion=None, referencia=None):
    return {"claveIdempotencia": str(uuid.uuid4()), "articulos": [{"varianteId": v, "unidades": u} for v, u in articulos], "nombre": nombre, "celular": celular,
            "entrega": entrega, "direccion": direccion, "referencia": referencia, "nota": None, "sitio": ""}


def vid(nombre, i=0):
    return ids[nombre]["variantes"][i]["id"]


enviados = []
for args in [([(vid("Martillo de uña 16 oz"), 2), (vid("Cinta métrica 5 m"), 1)], "Roberto Calle", "70100001"),
             ([(vid("Pintura látex 4 L"), 3), (vid("Rodillo para pintar 9\""), 2)], "Constructora Sol", "70100002", "Envio", "Av. Blanco Galindo km 4", "Portón azul"),
             ([(vid("Foco LED 12 W"), 10)], "Lucía Mamani", "70100003"),
             ([(vid("Tubo PVC 1/2\" (3 m)"), 4), (vid("Teflón"), 4)], "Plomería Rápida", "70100004", "Envio", "Calle Jordán 456", "Frente a la farmacia")]:
    s, r = api("POST", f"/{slug}/pedido", pedido(*args), base=WEB)
    if s in (200, 201):
        enviados.append(r)
s, lista = api("GET", "/api/v1/pedidos", token=t)
pedidos = sorted(lista["items"] if isinstance(lista, dict) else lista, key=lambda p: p["numero"])
if len(pedidos) >= 3:
    api("POST", f"/api/v1/pedidos/{pedidos[0]['id']}/confirmar", None, t)
    api("POST", f"/api/v1/pedidos/{pedidos[0]['id']}/entregar", None, t)   # se vuelve venta
    api("POST", f"/api/v1/pedidos/{pedidos[1]['id']}/confirmar", None, t)  # confirmado, sin entregar
    api("POST", f"/api/v1/pedidos/{pedidos[2]['id']}/cancelar", {"motivo": "El cliente ya no lo necesita"}, t)
print(f"Pedidos: {len(enviados)} (1 entregado, 1 confirmado, 1 cancelado, resto nuevos).")

print(f"\nListo. Tienda pública: {WEB}/{slug}\nPanel: http://localhost:5173  ·  credenciales en {ARCHIVO}")

