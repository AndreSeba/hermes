# Hermes — contexto del proyecto

> Nombre en clave: **Hermes** (provisorio). Documento para retomar el proyecto en cualquier sesión (por ejemplo, copiarlo como `docs/CONTEXTO.md` del repo nuevo).
> Última actualización: 2026-10-14.

## 1. Qué es

Un SaaS **simple y atractivo** para emprendedores bolivianos, pensado para cualquier tipo de negocio (producto general, no un rubro).

La persona debe sentir satisfacción al administrar su negocio. La idea fuerza es la **sencillez**: una sola persona, sin doble trabajo, rápido de usar.

**Propuesta central:** si publicás un producto en Instagram, se carga solo en Hermes y el catálogo queda actualizado. Además:

- Catálogo con variantes (colores, tallas) y fotos.
- Costos y **precio de venta sugerido** para asegurar ganancia.
- Stock liviano (no es un inventario empresarial).
- Compras y proveedores (para saber el costo real).
- Ventas simples.
- **Tienda pública** que los clientes ven sin registrarse (pedido por WhatsApp).

## 2. Restricciones y decisiones del usuario

- **Solo Bolivia** por ahora (moneda Bs, WhatsApp, pago por QR/transferencia).
- **Stack: .NET (backend) + React (frontend)**. Se consultó si se puede volver app: sí, como PWA y/o Capacitor.
- **Sin IA** (casi sin presupuesto). La lectura de los textos de Instagram se hace con un **parser por reglas** y un formato acordado con el cliente ("probá tu formato").
- **Sencillez ante todo:** el stock se separó del catálogo para no duplicar trabajo; el stock sale de movimientos y ventas, con atajo "Llegaron más".
- Es un proyecto **distinto** del InventoryPlatform de Nestlé Bolivia, aunque parecido en espíritu. Repos y datos no se mezclan.
- Todo lo que se publica (artifacts) queda privado; el usuario decide qué compartir.

## 3. Entregables hechos hasta ahora

1. **Prototipo navegable (lienzo de diseño)** (privado, versión 30). 31 pantallas: celular (390×844) y web de escritorio (1440).
   - Celular: Bienvenida, Inicio, Nuevos desde Instagram, Revisar producto, Catálogo, Calculadora de precio, Nueva venta, Tienda pública, Producto (vista del cliente), Configurar mi tienda, Probá tu formato, Nuevo producto + publicar en Instagram, Personalizar diseño, Stock, Proveedores, Compras, Nueva compra.
   - Web: equivalentes de la mayoría (Inicio, Instagram, Catálogo, Precio, Tienda, Venta, Config tienda, Nuevo producto, Producto, Diseño, Stock, Proveedores, Compras, Nueva compra).
   - Límite: las pantallas del lienzo son independientes (sin estado compartido). No se probó en navegador, solo revisiones estáticas.
2. **`hermes.html`**: app funcional de un solo archivo (guarda en `localStorage`, clave `hermes-v1`). Incluye catálogo, stock, ventas, proveedores y compras, costeo y precio sugerido. Probada con Playwright.
3. **Análisis de mercado** (conversación): competencia, tabla comparativa y propuesta de precios (abajo).

Ubicación local de los archivos en la sesión: `scratchpad/hermes/hermes.html` (respaldos `hermes.backup.html`, `hermes.pre-compras.html`) y scripts de prueba en `scratchpad/smoke/`.

## 4. Funcionalidad definida

### Catálogo
- Producto con variantes (color/talla), fotos, categoría, precio, costo.
- Costeo opcional: material, empaque, mano de obra, costo fijo. El **precio sugerido** se calcula; no se guarda.
- Nuevos productos desde Instagram: bandeja de revisión antes de publicar en el catálogo.

### Lector de Instagram (reglas, sin IA)
- Nombre = primera línea del texto.
- Precio: "Bs 120" o "Precio: 120".
- Variantes: "Colores: …", "Tallas: …".
- Stock: "Quedan N".
- Categoría: hashtag.
- Carruseles: líneas tipo "1. Cartera Bs 120".
- Ofertas: "ahora Bs …".
- Idea futura: publicar desde Hermes hacia Instagram.

### Stock
- Libro de movimientos; el stock se calcula y se descuenta solo con cada venta.
- Atajo "Llegaron más" para sumar.

### Proveedores y compras
- Compra = encabezado `{fecha, proveedor, líneas[{producto, variante, cantidad, costo}], recibida, pagada, notas}`.
- Al **recibir** una compra: suma stock, actualiza el costo del producto (si tiene costeo, cambia `costeo.mat`; si no, el costo directo) y **avisa si el margen queda bajo o en pérdida**.
- Último precio pagado por proveedor y comparador de proveedores.
- "Por pagar" (deuda con proveedores) y "En camino" (pedidos pendientes).
- Eliminar una compra revierte el stock y deja registro.

### Ventas
- Venta simple que descuenta stock y guarda costo y precio del momento.

### Tienda pública
- Sin registro. El cliente ve el catálogo y pide por WhatsApp (`wa.me`).
- Personalización: colores, fondo, etc.
- Datos públicos **nunca** incluyen costo, margen ni stock exacto (solo "disponible"/"agotado").

## 5. Mercado y precios (resumen)

- Hay competidores parciales (por ejemplo catálogos con WhatsApp, Mercagi y Tunta). Los datos de precios son referenciales y alguno es antiguo o contradictorio; **verificar antes de decidir**.
- Contexto Bolivia: salario mínimo Bs 3.300 (2026), tipo de cambio oficial Bs 11,73 (12-oct-2026), uso masivo de pagos por QR.
- **Plan propuesto (freemium):**
  - **Gratis:** hasta 15 productos.
  - **Emprendedor:** Bs 39/mes o Bs 390/año.
  - **Negocio:** Bs 79/mes (más adelante).
- Cobro inicial por QR/transferencia manual con comprobante y activación desde un panel admin.

## 6. Backend propuesto (a cerrar antes de codear)

| Tema | Propuesta |
|---|---|
| Forma | Monolito modular .NET 10 |
| Capas | Domain, Application, Infrastructure, Api y `Hermes.Public` (tienda en Razor Pages para SEO y vista previa en WhatsApp) |
| Base de datos | PostgreSQL (alternativa: SQL Server, **decisión pendiente**) |
| Multi-tenant | Una base, columna `TenantId`, filtro global de EF Core (luego Row Level Security) |
| IDs | GUID v7 (permite offline a futuro) |
| Dinero | `decimal(18,2)`, solo Bs |
| Imágenes | Almacenamiento tipo S3 (Cloudflare R2) con redimensionado |
| Segundo plano | Hangfire o `BackgroundService` |
| Auth | JWT corto + refresh token (email y contraseña) |
| Errores | `ProblemDetails`, paginación por cursor |

**Entidades:** `Tenant`, `Usuario`, `Plan`, `Suscripcion`, `Producto`, `Variante`, `Categoria`, `Imagen`, `Costeo`, `MovimientoStock`, `Proveedor`, `Compra`, `CompraLinea`, `Venta`, `VentaLinea`, `ConfigTienda`, `Pedido`, `CuentaInstagram`, `ImportacionIG`, `Auditoria`, `Notificacion`.

**API:** privada `/api/v1/...` (JWT) y pública `/public/{slug}/...` (DTOs separados, caché y límite por IP).

**Instagram (⚠️ verificar con Meta):**
- API de Instagram con Login de Meta, solo cuentas Business/Creator.
- Leer publicaciones es viable; **publicar exige revisión de la app** (semanas, política de privacidad, video).
- Se propone polling cada 15–30 min más botón "Sincronizar ahora" (no confiar en webhooks).
- Los tokens duran ~60 días: renovar con tarea programada y guardar cifrados.

**Seguridad y operación:** hash Argon2/BCrypt, pruebas de aislamiento entre tenants, respaldos diarios, Serilog + Sentry, términos y política de privacidad.

**Orden de construcción:** 1) núcleo (auth, tenant, productos, imágenes), 2) tienda pública, 3) stock, compras, proveedores y ventas, 4) costos y precio sugerido, 5) Instagram (lectura, parser, bandeja), 6) planes y panel admin, 7) publicar a Instagram si Meta lo aprueba.

## 7. Decisiones pendientes del usuario

1. **Postgres o SQL Server** (recomendado: Postgres por costo de hosting).
2. **Presupuesto mensual de hosting** (VPS ~US$5–10 vs administrado).
3. **Dónde va el código:** repo `Hermes` en GitHub (¿privado o público?) o carpeta local `C:\Users\Sebas\Documents\Proyectos\Hermes`. Desde la sesión en la nube no se puede acceder a esa ruta de Windows; hace falta una sesión local o un repo.
4. **Monorepo** con `/backend` y `/web` (recomendado) o repos separados.

## 8. Otros pendientes

- Verificar los requisitos vigentes de Meta/Instagram antes de depender de esa función.
- Confirmar qué sesión "ubuntu" mantener (candidata: "Análisis de archivo servidor", archivada).
- El control remoto lo activa el usuario desde su PC.
- Verificar precios de la competencia antes de fijar los planes.

## 9. Convenciones

- Idioma de la interfaz y de la documentación: **español**.
- Los números de teléfono en demos usan marcadores `7XXXXXXX` (no inventar números reales).
- No crear PRs ni compartir artifacts sin que el usuario lo pida.
