"""Prueba de carga: varias tiendas y muchos usuarios a la vez contra la API real (y el sitio público).

Mide tiempos (p50 / p95 / máximo), cuenta errores 5xx y, al final, comprueba que el stock CIERRA: lo que se vendió es
exactamente lo que bajó el stock (con ventas simultáneas sobre los mismos productos, que es donde se rompería).
Necesita la API (5013) y el sitio público (5014). Solo para desarrollo: crea tiendas de prueba en hermes_dev.
Uso: python -I scripts/carga.py [segundos] [usuarios]       (por defecto 60 s y 16 usuarios)
"""
import json, random, statistics, sys, threading, time, uuid, urllib.request, urllib.error
from collections import defaultdict
from concurrent.futures import ThreadPoolExecutor

sys.stdout.reconfigure(encoding="utf-8")
API, WEB = "http://localhost:5013", "http://localhost:5014"
SEGUNDOS = int(sys.argv[1]) if len(sys.argv) > 1 else 60
USUARIOS = int(sys.argv[2]) if len(sys.argv) > 2 else 16
TIENDAS, PRODUCTOS, STOCK_INICIAL = 4, 12, 500

tiempos = defaultdict(list)
codigos = defaultdict(lambda: defaultdict(int))
candado = threading.Lock()


def pedir(base, metodo, ruta, cuerpo=None, token=None, etiqueta=None):
    h = {"Content-Type": "application/json"} if cuerpo is not None else {}
    if token:
        h["Authorization"] = "Bearer " + token
    req = urllib.request.Request(base + ruta, json.dumps(cuerpo).encode() if cuerpo is not None else None, h, method=metodo)
    inicio = time.perf_counter()
    try:
        r = urllib.request.urlopen(req, timeout=30)
        b = r.read()
        estado, datos = r.status, (json.loads(b) if b and b[:1] in b"{[" else None)
    except urllib.error.HTTPError as e:
        b = e.read()
        estado, datos = e.code, (json.loads(b) if b and b[:1] in b"{[" else None)
    except Exception:
        estado, datos = 0, None
    if etiqueta:
        with candado:
            tiempos[etiqueta].append((time.perf_counter() - inicio) * 1000)
            codigos[etiqueta][estado] += 1
    return estado, datos


# ── Preparación: tiendas con productos y stock ──
print(f"Preparando {TIENDAS} tiendas con {PRODUCTOS} productos y {STOCK_INICIAL} unidades cada uno…")
tiendas = []
n = random.randint(10000, 99999)
for i in range(TIENDAS):
    s, ses = pedir(API, "POST", "/api/v1/auth/registro", {"nombre": "Carga", "email": f"carga{n}-{i}@ejemplo.com", "password": "secreta123",
                                                          "nombreTienda": f"Carga {n} {i}", "slug": f"carga-{n}-{i}"})
    assert s in (200, 201), f"registro: {s}"
    t = ses["accessToken"]
    pedir(API, "PUT", "/api/v1/tienda", {"nombre": f"Carga {n} {i}", "descripcion": None, "whatsapp": "70000003", "color": "#F2A33A", "logoImagenId": None}, t)
    variantes = []
    for j in range(PRODUCTOS):
        s, p = pedir(API, "POST", "/api/v1/productos", {"nombre": f"Producto {j}", "descripcion": None, "precio": 50 + j, "costo": 20, "categoriaId": None,
                                                       "visibleEnTienda": True, "colores": None, "tallas": None, "fotoIds": None, "controlaStock": True}, t)
        assert s == 201, f"producto: {s} {p}"
        v = p["variantes"][0]["id"]
        pedir(API, "POST", "/api/v1/stock/ingresos", {"productoId": p["id"], "lineas": [{"varianteId": v, "cantidad": STOCK_INICIAL}]}, t)
        variantes.append((p["id"], v))
    tiendas.append({"token": t, "slug": f"carga-{n}-{i}", "variantes": variantes, "vendidas": defaultdict(int), "candado": threading.Lock()})

fin = time.time() + SEGUNDOS


def duena(k):
    """Una dueña que mira su panel y vende (todas las de una tienda venden los MISMOS productos a la vez)."""
    tienda = tiendas[k % TIENDAS]
    t = tienda["token"]
    while time.time() < fin:
        accion = random.random()
        if accion < 0.35:
            producto, variante = random.choice(tienda["variantes"])
            unidades = random.randint(1, 3)
            s, _ = pedir(API, "POST", "/api/v1/ventas", {"claveIdempotencia": str(uuid.uuid4()), "lineas": [{"varianteId": variante, "cantidad": unidades}],
                                                         "metodoPago": "Efectivo", "cliente": None}, t, "POST venta")
            if s == 201:
                with tienda["candado"]:
                    tienda["vendidas"][variante] += unidades
        elif accion < 0.55:
            pedir(API, "GET", "/api/v1/productos?Limite=24", token=t, etiqueta="GET productos")
        elif accion < 0.75:
            pedir(API, "GET", "/api/v1/stock?Limite=30", token=t, etiqueta="GET stock")
        elif accion < 0.90:
            pedir(API, "GET", "/api/v1/inicio", token=t, etiqueta="GET inicio")
        else:
            pedir(API, "GET", "/api/v1/ventas/productos?Limite=30", token=t, etiqueta="GET para vender")


def cliente(k):
    """Un cliente que mira una tienda pública (el sitio público tiene límite por IP: los 429 son esperables acá)."""
    while time.time() < fin:
        tienda = random.choice(tiendas)
        if random.random() < 0.5:
            pedir(WEB, "GET", f"/{tienda['slug']}", etiqueta="WEB tienda")
        else:
            producto, _ = random.choice(tienda["variantes"])
            pedir(WEB, "GET", f"/{tienda['slug']}/p/{producto}", etiqueta="WEB producto")
        time.sleep(0.2)


print(f"Carga: {USUARIOS} dueñas + {USUARIOS // 4} clientes durante {SEGUNDOS} s…")
inicio = time.time()
with ThreadPoolExecutor(USUARIOS + USUARIOS // 4) as ex:
    for k in range(USUARIOS):
        ex.submit(duena, k)
    for k in range(USUARIOS // 4):
        ex.submit(cliente, k)
duracion = time.time() - inicio

# ── Resultados ──
print(f"\n{'qué':<18}{'pedidos':>9}{'p50 ms':>9}{'p95 ms':>9}{'máx ms':>9}   códigos")
total = errores = 0
for etiqueta in sorted(tiempos):
    ts = sorted(tiempos[etiqueta])
    p95 = ts[int(len(ts) * 0.95) - 1] if len(ts) >= 20 else ts[-1]
    total += len(ts)
    errores += sum(c for codigo, c in codigos[etiqueta].items() if codigo >= 500 or codigo == 0)
    print(f"{etiqueta:<18}{len(ts):>9}{statistics.median(ts):>9.0f}{p95:>9.0f}{ts[-1]:>9.0f}   {dict(codigos[etiqueta])}")
print(f"\n{total} pedidos en {duracion:.0f} s = {total / duracion:.0f} por segundo · errores 5xx/conexión: {errores}")

# ── ¿Cierra el stock? ──
descuadres = 0
for tienda in tiendas:
    _, lista = pedir(API, "GET", f"/api/v1/stock?Limite=60", token=tienda["token"])
    stock = {v["id"]: v["stock"] for p in lista["items"] for v in p["variantes"]}
    for _, variante in tienda["variantes"]:
        esperado = STOCK_INICIAL - tienda["vendidas"][variante]
        if stock.get(variante) != esperado:
            descuadres += 1
            print(f"DESCUADRE {tienda['slug']} {variante}: esperado {esperado}, hay {stock.get(variante)}")
vendidas = sum(sum(t["vendidas"].values()) for t in tiendas)
print(f"Stock: {vendidas} unidades vendidas en {sum(codigos['POST venta'].get(201, 0) for _ in [0])} ventas · descuadres: {descuadres}")
print("TODO BIEN" if errores == 0 and descuadres == 0 else "HAY PROBLEMAS")
sys.exit(0 if errores == 0 and descuadres == 0 else 1)
