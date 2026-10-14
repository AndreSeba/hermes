# Plan de implementación — módulo por módulo

> Fecha: 2026-10-14. Complementa `docs/CONTEXTO.md` (qué es el producto) y los `CLAUDE.md` de
> `Hermes` y `Hermes.Web` (cómo está construido). Se trabaja **un módulo a la vez, de punta a
> punta** (backend + front + pruebas) y no se empieza el siguiente hasta cerrar el anterior.

## Cómo se trabaja cada módulo

Cada módulo pasa por los mismos pasos. Un módulo está **terminado** cuando cumple todo esto:

**Backend (`Hermes`)**
1. Entidad en `Domain` (implementa `IPerteneceATienda` si es de negocio).
2. `IEntityTypeConfiguration` en `Infrastructure` (largos, índices, `decimal(12,2)`, FKs `Restrict`).
3. DTOs + interfaz en `Application`; errores esperables como `DominioException`.
4. Servicio en `Infrastructure/Services` (toda la lógica acá; nunca filtrar por tienda a mano).
5. Controller en `/api/v1/...` con `[Authorize]` y `ProducesResponseType`.
6. Migración (`dotnet ef migrations add`) aplicada en `hermes_dev`.
7. Pruebas: **aislamiento entre tiendas** (otra tienda no lo ve ni lo edita) + reglas de negocio del módulo.

**Front (`Hermes.Web`)**
8. `pnpm api:tipos` (nunca escribir tipos de la API a mano).
9. Pantallas según el prototipo, **celular primero (390 px)** y luego escritorio (1440 px).
10. Sin nombre de producto visible (todo sale de `src/marca.ts`).

**Cierre**
11. Prueba de punta a punta contra la API real (flujo feliz + errores principales).
12. `dotnet test`, `pnpm build`, `pnpm lint` y `pnpm audit` limpios.
13. `CLAUDE.md` de cada repo actualizado (reglas nuevas, trampas encontradas).
14. Un commit por repo.

Tamaños: **S** = una sesión corta, **M** = una sesión, **L** = dos o más sesiones.

---

## Fase 0 — Base ✅ (hecho)

- Esqueleto de los dos repos con la estructura de CMP, Postgres local, `hermes_dev`.
- Multi-tienda: filtro global + `TiendaId` automático. Manejo de errores (`ProblemDetails`).
- Autenticación en el backend: registro (usuario + tienda), login con bloqueo, JWT de 15 min,
  refresh token rotativo en cookie HttpOnly, logout, `/yo`.
- Módulo de referencia Categoría (solo backend).

---

## Fase 1 — Núcleo usable

### M1 · Sesión y armazón del front — **M** ✅ (2026-10-14)
Hecho con `react-router`. e2e de 17 verificaciones (`pnpm e2e` en Hermes.Web). De paso se corrigió en
el backend la carrera de dos pestañas renovando a la vez y se separó el límite por IP del refresh.

Para que todo lo demás tenga dónde vivir.
- Front: ruteo, pantallas **Bienvenida, Registro, Iniciar sesión**; renovación automática del
  token (al cargar la app y al recibir 401, con reintento del pedido); cerrar sesión.
- Armazón: barra inferior en celular / menú lateral en escritorio, con las secciones vacías.
- Componentes base propios (botón, campo, tarjeta, diálogo, aviso de error) con los tokens de `index.css`.
- **Decisión**: librería de ruteo (recomendado: `react-router`, una sola dependencia conocida).
- Listo cuando: me registro desde el celular, recargo la página y sigo adentro, cierro sesión y no puedo volver atrás.

### M2 · Imágenes — **M**
Infraestructura que usan Catálogo, Mi tienda e Instagram.
- Backend: interfaz `IAlmacenArchivos`; los servicios nunca saben dónde se guardan los archivos.
  **Implementación actual: en la base de datos** (ver "Dónde se guardan las fotos" abajo).
- Procesamiento con **SkiaSharp** (decidido 2026-10-14): valida que sea una imagen real (JPG/PNG/WEBP),
  tope de tamaño y de píxeles, corrige la orientación del celular, genera **3 tamaños** (miniatura
  ~200 px, catálogo ~600 px, detalle ~1200 px) en **WEBP** y descarta los metadatos (GPS incluido).
  **No se guarda la foto original.**
- Listo cuando: subo una foto y se ve en sus tres tamaños; un archivo falso con extensión `.jpg` se rechaza.

#### Dónde se guardan las fotos (decidido 2026-10-14)

**Ahora: en PostgreSQL**, decisión del usuario: por el momento la plataforma la usa una sola
persona y no vale la pena una cuenta en la nube. En una tabla propia (nunca dentro de `productos`,
la lección de CMP: con la foto adentro cada listado arrastraba megas) y servidas por la API con
caché larga.

**Después (cuando haya más tiendas, a más tardar en M14): Cloudflare R2**, con un subdominio
propio para las fotos y su CDN. Opciones evaluadas (precios a confirmar al momento de migrar):

| Opción | Gratis | Después | Descarga (clientes viendo fotos) | Veredicto |
|---|---|---|---|---|
| Base de datos | — | crece con la base y sus respaldos | pasa por la API, sin CDN | ✅ ahora (un usuario) |
| Disco del VPS | — | — | consume el ancho de banda del VPS | ❌ se pierde si se rompe el servidor |
| **Cloudflare R2** | 10 GB | ~US$0,015/GB-mes | **gratis, sin límite** | ✅ **producción** |
| Backblaze B2 | 10 GB | ~US$0,006/GB-mes | gratis hasta 3× lo guardado | alternativa |
| DigitalOcean Spaces | — | US$5/mes (250 GB) | 1 TB incluido | alternativa |
| AWS S3 | — | ~US$0,023/GB-mes | ~US$0,09/GB: **caro** | ❌ |

Tamaño estimado: ~0,5 MB por foto (3 tamaños WEBP), ~1,5 MB por producto con 3 fotos, ~75 MB por
tienda de 50 productos; **1.000 tiendas ≈ 75 GB ≈ US$1/mes en R2**. En una tienda pública lo que
más pesa son las descargas, por eso R2 (descarga gratis).

**Cómo será la migración**: cada archivo ya tiene una **clave** con forma de ruta
(`{tienda}/imagenes/{id}/{tamaño}.webp`), igual a la que tendrá en el bucket. Migrar = copiar las
filas al bucket con la misma clave + cambiar la implementación de `IAlmacenArchivos` por configuración.
Ningún servicio ni pantalla cambia.

### M3 · Mi tienda — **S**
- Backend: ver/editar datos de la tienda (nombre, descripción, WhatsApp, color, logo); el slug se
  valida igual que en el registro y solo se cambia con confirmación (rompe enlaces compartidos).
- Front: **Configurar mi tienda** (sin "Personalizar diseño" todavía, eso va con la Tienda pública).
- Listo cuando: cambio el color y el WhatsApp y quedan guardados.

### M4 · Catálogo — **L**
El corazón del producto.
- Backend: `Producto` (nombre, descripción, categoría, precio, costo directo, stock mínimo, visible en
  tienda, archivado), `Variante`, `ProductoFoto` (orden, portada). Categorías ya existen.
  - Todo producto tiene al menos una variante ("Único").
  - Archivar en vez de borrar (puede tener ventas).
  - Listado con búsqueda, filtro por categoría y paginación por cursor.
- Front: **Catálogo** (grilla con fotos, búsqueda, chips de categoría), **Nuevo producto**,
  **Editar producto**, gestión de categorías dentro del catálogo.
- **Decisión**: modelo de variantes. Recomendado: cada variante con `Color` y `Talla` opcionales
  (cubre "Colores: …" y "Tallas: …" del lector de Instagram) en lugar de un texto libre.
- Listo cuando: cargo un producto con 3 colores y 2 fotos desde el celular y lo veo en el catálogo.

---

## Fase 2 — Operación diaria

### M5 · Stock — **M**
- Backend: `MovimientoStock` (ingreso, ajuste, venta, devolución) por variante. **El stock se calcula
  sumando movimientos, nunca se guarda en una columna** (regla de CMP: evita stocks desincronizados).
  - **Candado por variante** antes de leer y descontar (como `BloquearProductoAsync` en CMP): sin eso,
    dos ventas simultáneas pueden dejar stock negativo.
  - Estado por producto: ok / pocos (≤ stock mínimo) / agotado.
- Front: **Stock** (lista con estado, "Para reponer"), atajo **"Llegaron más"**, ajuste con motivo,
  historial por producto.
- Listo cuando: "Llegaron más" suma, un ajuste exige motivo y el historial muestra todo.

### M6 · Ventas — **M**
- Backend: `Venta` + `VentaLinea` (variante, cantidad, precio y **costo del momento**), método de pago
  (efectivo, QR, transferencia, otro), cliente opcional. Descuenta stock en la misma transacción.
  Anular devuelve el stock (nunca se borra una venta).
- Front: **Nueva venta** (rápida, pensada para una mano), listado del día/mes, anular.
- Pruebas: venta sin stock suficiente → 409; ventas simultáneas sobre la última unidad → solo una pasa.
- Listo cuando: vendo 2 productos, el stock baja y la ganancia de la venta es correcta.

### M7 · Proveedores y compras — **L**
- Backend: `Proveedor`, `Compra` + `CompraLinea` (variante, cantidad, costo), estados **recibida** y
  **pagada**.
  - **Recibir** (una sola vez, transición atómica): suma stock, actualiza el costo del producto y
    **avisa si el margen queda bajo o en pérdida**.
  - Eliminar una compra recibida revierte el stock y deja registro.
  - Lecturas: "Por pagar", "En camino", último precio por proveedor, comparador de proveedores.
- Front: **Proveedores**, **Compras**, **Nueva compra**, "Pedirle a …" por WhatsApp.
- Listo cuando: registro una compra, la recibo, el stock y el costo se actualizan y veo el aviso de margen.

### M8 · Costos y precio sugerido — **M**
- Backend: costeo opcional por producto (material, empaque, mano de obra, costo fijo) y margen
  objetivo. **El precio sugerido se calcula, no se guarda.** Si hay costeo, recibir una compra
  actualiza el componente "material".
- Front: **Calculadora de precio** dentro del producto, con el precio sugerido y la ganancia por unidad.
- Listo cuando: cambio el costo de material y el precio sugerido cambia; el precio real no se toca solo.

### M9 · Inicio y gastos — **M**
- Backend: `Gasto` (concepto, monto, fecha). Resumen del mes: ventas, ganancia, gastos, "ya cubriste
  el X % de los gastos", productos para reponer, últimos movimientos.
- Front: **Inicio** (la primera pantalla después del login) y registro rápido de gastos.
- Listo cuando: el Inicio refleja las ventas y gastos cargados en los módulos anteriores.

---

## Fase 3 — Hacia afuera

### M10 · Tienda pública — **L**
- Backend: rutas **`/public/{slug}/...` sin login**, con DTOs propios que **nunca** incluyen costo,
  margen ni stock exacto (solo disponible/agotado). Caché y límite por IP.
- Página pública renderizada en el servidor (proyecto `Hermes.Public`, Razor Pages) para que el
  enlace compartido en WhatsApp muestre **vista previa con foto** (una SPA no la genera).
- Pedido por WhatsApp (`wa.me` con el mensaje armado: producto, variante, cantidad).
- Front (panel): **Personalizar diseño** (colores, fondo) y vista previa.
- **Decisión**: confirmar Razor Pages para la tienda pública (recomendado) vs. todo en React.
- Pruebas: ningún endpoint público expone costo ni stock; una tienda inactiva no se muestra.
- Listo cuando: comparto el enlace por WhatsApp, se ve la vista previa y el pedido llega armado.

### M11 · Lector de Instagram por reglas — **M**
Se puede hacer **sin Meta**: el parser es lógica pura.
- Backend: parser (nombre = primera línea, "Bs 120"/"Precio: 120", "Colores: …", "Tallas: …",
  "Quedan N", categoría por hashtag, carruseles "1. Cartera Bs 120", ofertas "ahora Bs …").
  Muchas pruebas con textos reales.
- Front: **Probá tu formato** (pego un texto y veo qué entendió) e importación manual: pego el texto
  de una publicación → **bandeja de revisión** → publicar al catálogo.
- Listo cuando: 20 textos reales de Instagram se leen bien o la bandeja muestra claramente qué falta.

### M12 · Conexión con Instagram (Meta) — **L**
- ⚠️ Antes de empezar: **verificar los requisitos vigentes de Meta** (cuentas Business/Creator, revisión de la app).
- Login de Meta, token **cifrado**, renovación programada (~60 días), sincronización cada 15–30 min
  + botón "Sincronizar ahora", nuevas publicaciones → bandeja de M11.
- Publicar desde la app hacia Instagram solo si Meta aprueba la app (queda para después).

---

## Fase 4 — Negocio y producción

### M13 · Planes y panel admin — **M**
- Plan Gratis (hasta 15 productos), Emprendedor y Negocio; límites aplicados en el backend.
- Cobro manual: el cliente sube el comprobante de QR/transferencia y se activa desde un **panel admin**
  (rol de plataforma, distinto del dueño de tienda).
- ⚠️ Verificar precios de la competencia antes de fijar los montos.

### M14 · Salida a producción — **M**
- Hosting (decidir VPS vs. administrado), HTTPS, `ForwardedHeaders` detrás del proxy, secretos por
  variables de entorno, respaldos diarios de Postgres, Serilog + Sentry, limpieza de refresh tokens
  vencidos, Row Level Security como segunda barrera multi-tienda.
- **Fotos a Cloudflare R2** si ya hay varias tiendas (ver "Dónde se guardan las fotos" en M2): crear
  la cuenta y el bucket, implementación R2 de `IAlmacenArchivos`, copiar las filas con la misma clave.
- SkiaSharp en Linux necesita el paquete `SkiaSharp.NativeAssets.Linux` (en Windows viene incluido).
- Términos y política de privacidad. PWA (instalable en el celular).
- Pruebas de carga con varios usuarios simultáneos (como se hizo en CMP).

---

## Decisiones pendientes (en el orden en que se necesitan)

| # | Decisión | Se necesita en | Recomendación |
|---|---|---|---|
| 1 | ~~Librería de ruteo del front~~ | M1 | ✅ `react-router` |
| 2 | ~~Librería de imágenes~~ | M2 | ✅ SkiaSharp (MIT) |
| 2b | ~~Dónde guardar las fotos~~ | M2 | ✅ Postgres ahora; Cloudflare R2 al crecer (ver M2) |
| 3 | Modelo de variantes | M4 | `Color` + `Talla` opcionales |
| 4 | Tienda pública: Razor Pages o React | M10 | Razor Pages (vista previa en WhatsApp) |
| 5 | Requisitos actuales de Meta | M12 | Verificar antes de empezar |
| 6 | Precios de los planes | M13 | Verificar competencia |
| 7 | Hosting | M14 | VPS chico (US$5–10) al inicio |
| 8 | **Nombre del producto** | antes de M10 (la tienda pública lo muestra) | — |

## Orden y dependencias

```
M1 Sesión ─┬─ M2 Imágenes ─┬─ M3 Mi tienda
           │               └─ M4 Catálogo ─┬─ M5 Stock ─┬─ M6 Ventas ──┐
           │                               │            └─ M7 Compras ─┼─ M8 Costos ── M9 Inicio
           │                               ├─ M10 Tienda pública       │
           │                               └─ M11 Lector IG ── M12 Meta│
           └──────────────────────────────────────────── M13 Planes ── M14 Producción
```

**Primer hito de valor** (se puede mostrar a emprendedores reales): **M1 → M4 + M10**, o sea
catálogo con fotos y tienda pública compartible por WhatsApp. El resto suma la parte de administración.
