"""Prueba de punta a punta del carrito y los pedidos contra el sitio público real, la API real y Postgres.

Cubre lo que las pruebas unitarias no pueden: las rutas JSON del sitio público (/{slug}/carrito/revisar y /{slug}/pedido),
el campo trampa, que un formulario común (no JSON) no pueda mandar pedidos, el enlace de WhatsApp, pedidos simultáneos
por HTTP (números distintos) y el ciclo en el panel (confirmar, entregar = venta + stock, cancelar, 409, otra tienda).
Necesita la API (5013) y el sitio público (5014). Solo para desarrollo. Uso: python -I scripts/e2e_pedidos.py
"""
import json, random, sys, uuid, urllib.parse, urllib.request, urllib.error
from concurrent.futures import ThreadPoolExecutor

sys.stdout.reconfigure(encoding="utf-8")
API = "http://localhost:5013"
WEB = "http://localhost:5014"
fallas = 0


def ok(desc, cond):
    global fallas
    print(("OK    " if cond else "FALLA ") + desc)
    if not cond:
        fallas += 1


def pedir(base, metodo, ruta, cuerpo=None, token=None, tipo="application/json"):
    h = {"Content-Type": tipo} if cuerpo is not None else {}
    if token:
        h["Authorization"] = "Bearer " + token
    datos = None if cuerpo is None else (cuerpo.encode() if isinstance(cuerpo, str) else json.dumps(cuerpo).encode())
    req = urllib.request.Request(base + ruta, datos, h, method=metodo)
    try:
        r = urllib.request.urlopen(req)
        b = r.read()
        return r.status, (json.loads(b) if b and b[:1] in b"{[" else None)
    except urllib.error.HTTPError as e:
        b = e.read()
        return e.code, (json.loads(b) if b and b[:1] in b"{[" else None)


def api(metodo, ruta, obj=None, token=None):
    return pedir(API, metodo, ruta, obj, token)


def producto(nombre, precio, colores=None, controla=True):
    return {"nombre": nombre, "descripcion": None, "precio": precio, "costo": 50, "categoriaId": None, "visibleEnTienda": True,
            "colores": colores, "tallas": None, "fotoIds": None, "controlaStock": controla}


def registrar(prefijo):
    n = random.randint(100000, 999999)
    s, ses = api("POST", "/api/v1/auth/registro", {"nombre": prefijo, "email": f"{prefijo}{n}@ejemplo.com", "password": "secreta123",
                                                   "nombreTienda": f"{prefijo.title()} {n}", "slug": f"{prefijo}-{n}"})
    assert s in (200, 201), s
    return ses["accessToken"], f"{prefijo}-{n}", f"{prefijo.title()} {n}"


def pedido(articulos, **extra):
    d = {"claveIdempotencia": str(uuid.uuid4()), "articulos": [{"varianteId": v, "unidades": u} for v, u in articulos],
         "nombre": "Ana Prueba", "celular": "7000 0001", "entrega": "Retiro", "direccion": None, "referencia": None, "nota": None, "sitio": ""}
    d.update(extra)
    return d


t, slug, nombre = registrar("carrito")
tb, slug_b, _ = registrar("otracarrito")
api("PUT", "/api/v1/tienda", {"nombre": nombre, "descripcion": None, "whatsapp": "70000002", "color": "#F2A33A", "logoImagenId": None}, t)

_, cartera = api("POST", "/api/v1/productos", producto("Cartera", 180, ["café", "negro"]), t)
_, aretes = api("POST", "/api/v1/productos", producto("Aretes", 45.5), t)
_, collar = api("POST", "/api/v1/productos", producto("Collar por encargo", 90, controla=False), t)
negro, cafe, ar = cartera["variantes"][1]["id"], cartera["variantes"][0]["id"], aretes["variantes"][0]["id"]
api("POST", "/api/v1/stock/ingresos", {"productoId": cartera["id"], "lineas": [{"varianteId": negro, "cantidad": 5}, {"varianteId": cafe, "cantidad": 5}]}, t)
_, ajeno = api("POST", "/api/v1/productos", producto("Ajeno", 10), tb)

# 1. Las páginas
s, _ = pedir(WEB, "GET", f"/{slug}/carrito")
ok("la página del carrito existe (200)", s == 200)
html = urllib.request.urlopen(f"{WEB}/{slug}/p/{cartera['id']}").read().decode()
ok("el producto trae el botón Agregar al carrito y el enlace al carrito", 'id="agregar"' in html and f'href="/{slug}/carrito"' in html)

# 2. Revisar el carrito
s, r = pedir(WEB, "POST", f"/{slug}/carrito/revisar", {"variantes": [ar, negro, ajeno["variantes"][0]["id"], str(uuid.uuid4())]})
ok("revisar devuelve lo que existe, en orden, con precio y disponibilidad (sin lo de otra tienda)", s == 200 and r["recibePedidos"] is True
   and [(a["nombre"], a["variante"], a["precio"], a["disponible"]) for a in r["articulos"]] == [("Aretes", None, 45.5, False), ("Cartera", "negro", 180, True)])
texto = json.dumps(r).lower()
ok("revisar nunca trae costo ni stock", "costo" not in texto and "stock" not in texto and "cantidad" not in texto)

# 3. Pedir
s, e = pedir(WEB, "POST", f"/{slug}/pedido", pedido([(negro, 2), (ar, 1)]))
ok("con algo agotado (y la tienda muestra disponibilidad) el pedido da 409 con el nombre", s == 409 and "Aretes" in e["detail"])
dto = pedido([(negro, 2), (collar["variantes"][0]["id"], 1)], entrega="Envio", direccion="Av. América 123", referencia="Portón verde")
s, p1 = pedir(WEB, "POST", f"/{slug}/pedido", dto)
ok("pedido creado (201) con número 1", s == 201 and p1["numero"] == 1 and p1["nuevo"] is True and p1["total"] == 450)
msg = urllib.parse.unquote(p1["whatsapp"].split("text=")[1])
ok("el enlace de WhatsApp va al número de la tienda con el pedido escrito", p1["whatsapp"].startswith("https://wa.me/59170000002?")
   and "pedido #1" in msg and "2 × Cartera (negro) — Bs 360" in msg and "Total: Bs 450" in msg and "Envío a: Av. América 123" in msg)
s, p1b = pedir(WEB, "POST", f"/{slug}/pedido", dto)
ok("la misma clave devuelve el mismo pedido (200, no duplica)", s == 200 and p1b["numero"] == 1 and p1b["nuevo"] is False)
for desc, cuerpo in [("sin nombre", pedido([(negro, 1)], nombre=" ")), ("celular que no es de Bolivia", pedido([(negro, 1)], celular="1234")),
                     ("envío sin dirección", pedido([(negro, 1)], entrega="Envio")), ("100 unidades", pedido([(negro, 100)])),
                     ("carrito vacío", pedido([]))]:
    ok(f"rechaza {desc} (400)", pedir(WEB, "POST", f"/{slug}/pedido", cuerpo)[0] == 400)
ok("lo de otra tienda no se puede pedir (409)", pedir(WEB, "POST", f"/{slug}/pedido", pedido([(ajeno["variantes"][0]["id"], 1)]))[0] == 409)
ok("el campo trampa lleno se rechaza (400)", pedir(WEB, "POST", f"/{slug}/pedido", pedido([(negro, 1)], sitio="http://spam"))[0] == 400)
s, _ = pedir(WEB, "POST", f"/{slug}/pedido", "nombre=Ana&celular=70000001", tipo="application/x-www-form-urlencoded")
ok("un formulario común (no JSON) no puede mandar pedidos (400/415)", s in (400, 415))
ok("una tienda sin WhatsApp no recibe pedidos (409)", pedir(WEB, "POST", f"/{slug_b}/pedido", pedido([(ajeno["variantes"][0]["id"], 1)]))[0] == 409)

# pedidos simultáneos: números distintos
with ThreadPoolExecutor(8) as ex:
    numeros = sorted(f.result()[1]["numero"] for f in [ex.submit(pedir, WEB, "POST", f"/{slug}/pedido", pedido([(cafe, 1)])) for _ in range(8)])
ok("8 pedidos simultáneos reciben números distintos y seguidos (2 a 9)", numeros == list(range(2, 10)))

# 4. El panel
s, lista = api("GET", "/api/v1/pedidos", token=t)
ok("el panel lista los 9 pedidos por atender, más nuevo primero", s == 200 and len(lista["items"]) == 9 and lista["items"][-1]["numero"] == 1)
ok("el resumen cuenta 9 nuevos", api("GET", "/api/v1/pedidos/resumen", token=t)[1] == {"nuevos": 9, "confirmados": 0})
ok("el Inicio avisa los pedidos pendientes", api("GET", "/api/v1/inicio", token=t)[1]["pedidosPendientes"] == 9)
id1 = lista["items"][-1]["id"]
s, d = api("GET", f"/api/v1/pedidos/{id1}", token=t)
ok("el detalle trae cliente, celular normalizado, entrega y líneas", s == 200 and d["clienteCelular"] == "+59170000001"
   and d["direccion"] == "Av. América 123" and [l["descripcion"] for l in d["lineas"]] == ["Cartera (negro)", "Collar por encargo"])
ok("otra tienda no lo ve (404) ni lo atiende", api("GET", f"/api/v1/pedidos/{id1}", token=tb)[0] == 404
   and api("POST", f"/api/v1/pedidos/{id1}/confirmar", token=tb)[0] == 404)
ok("confirmar: 200 y la segunda vez 409", api("POST", f"/api/v1/pedidos/{id1}/confirmar", token=t)[0] == 200
   and api("POST", f"/api/v1/pedidos/{id1}/confirmar", token=t)[0] == 409)
with ThreadPoolExecutor(5) as ex:
    estados = [f.result()[0] for f in [ex.submit(api, "POST", f"/api/v1/pedidos/{id1}/entregar", {"metodoPago": "Qr"}, t) for _ in range(5)]]
ok("5 entregas simultáneas no fallan feo (200/409)", all(c in (200, 409) for c in estados))
_, ventas = api("GET", "/api/v1/ventas?Limite=10", token=t)
ok("queda UNA sola venta de Bs 450 con el cliente", len(ventas["items"]) == 1 and ventas["items"][0]["total"] == 450 and ventas["items"][0]["cliente"] == "Ana Prueba")
_, st = api("GET", f"/api/v1/stock/{cartera['id']}", token=t)
ok("el stock del negro bajó de 5 a 3 (una sola vez)", next(v for v in st["variantes"] if v["id"] == negro)["stock"] == 3)
id2 = lista["items"][0]["id"]
s, c = api("POST", f"/api/v1/pedidos/{id2}/cancelar", {"motivo": "No respondió"}, t)
ok("cancelar con motivo (200) y después no se puede entregar (409)", s == 200 and c["estado"] == "Cancelado"
   and api("POST", f"/api/v1/pedidos/{id2}/entregar", {"metodoPago": "Efectivo"}, t)[0] == 409)
ok("vistas: 1 entregado, 1 cancelado, 7 por atender", len(api("GET", "/api/v1/pedidos?Vista=Entregados", token=t)[1]["items"]) == 1
   and len(api("GET", "/api/v1/pedidos?Vista=Cancelados", token=t)[1]["items"]) == 1
   and len(api("GET", "/api/v1/pedidos", token=t)[1]["items"]) == 7)

print(f"\n{'TODO BIEN' if fallas == 0 else str(fallas) + ' FALLAS'}")
sys.exit(1 if fallas else 0)
