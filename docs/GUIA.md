# Hermes — guía del proyecto

> Backend de Hermes: un SaaS simple para emprendedores bolivianos (catálogo, stock liviano,
> compras, ventas, precio sugerido y tienda pública con pedido por WhatsApp). Lee este archivo
> antes de tocar código. Si el código pide una decisión que no está documentada acá, preguntá
> antes de asumir. El contexto de producto completo está en `docs/CONTEXTO.md`.

**Este repo es solo el backend.** El frontend (React) vive en un repo hermano,
`../Hermes.Web`. La estructura copia la de `../InventoryPlatform` (CMP), pero es un proyecto
distinto: los repos y los datos no se mezclan.

## Stack (cerrado — no reabrir sin aprobación explícita)

| Capa | Tecnología |
|---|---|
| Runtime | .NET 10 |
| API | ASP.NET Core Web API — **Controllers MVC**, no Minimal API |
| Datos | EF Core + **PostgreSQL** (Npgsql), nombres en snake_case (`EFCore.NamingConventions`), esquema `hermes` |
| Documentación | OpenAPI nativo (`AddOpenApi`) + **Scalar** en `/scalar/v1` (solo Development) |
| Auth | JWT bearer corto + refresh token (pendiente), contraseña con BCrypt |
| Pruebas | xUnit + SQLite en memoria |

## Arquitectura en capas (estricta)

```
src/
├── Hermes.Domain/          # Entidades + enums. Sin dependencias de nada.
├── Hermes.Application/     # DTOs, interfaces de servicio, DominioException, Validacion. Sin EF Core.
├── Hermes.Infrastructure/  # HermesDbContext, IEntityTypeConfiguration por entidad, servicios, migraciones.
├── Compartido/             # Produccion.cs: logs, proxy y /salud, enlazado en Api y Public (no es un proyecto).
├── Hermes.Api/             # Controllers, ManejadorGlobalDeExcepciones, Program.cs (DI, JWT, CORS, Scalar).
└── Hermes.Public/          # La tienda pública (Razor Pages, sin login, puerto 5014). Usa IPublicoService directo.
tests/Hermes.UnitTests/     # xUnit + SQLite en memoria (no el proveedor InMemory de EF).
scripts/                    # Pruebas de punta a punta contra la API real (e2e_imagenes/productos/stock/ventas/compras/costeo/inicio/publico/instagram/planes/pedidos/instagram_conexion.py; meta_falso.py; carga.py).
deploy/                     # Producción: docker-compose, Dockerfiles, Caddyfile, respaldo, .env.example (docs/DESPLIEGUE.md).
docs/
├── CONTEXTO.md             # Qué es Hermes, decisiones, mercado, orden de construcción.
├── DESPLIEGUE.md           # Cómo poner todo en un servidor.
└── db-referencia/          # Scripts SQL del diseño previo. SOLO referencia del modelo, no se ejecutan.
```

- Nunca SQL en Controllers. Nunca lógica de negocio en Configurations. La lógica vive en
  `Hermes.Infrastructure/Services/`.
- **Las migraciones de EF son las dueñas del esquema.** Las reglas que en `docs/db-referencia`
  estaban en triggers (stock que se descuenta solo, costo que se actualiza al recibir una
  compra) se implementan en los servicios, no en la base.

## Multi-tienda (la regla más importante)

Cada tienda (tenant) ve solo sus datos. Mecanismo:

- Toda entidad de negocio implementa `IPerteneceATienda` (`TiendaId`).
- `HermesDbContext` le aplica un **filtro global** `TiendaId == tienda de la sesión` a todas
  esas entidades, automáticamente (no hay que registrar nada por entidad).
- Al insertar, `SaveChanges` **completa `TiendaId` solo** y prohíbe cambiarlo después.
  **Los servicios nunca asignan ni filtran por `TiendaId` a mano.**
- La tienda de la sesión la da `ITiendaActual`; en la API es `TiendaActualDesdeToken` (claim
  `tienda` del JWT). Sin tienda, el filtro no devuelve nada y crear lanza `SinTiendaEnSesionException` (403).
- `IgnoreQueryFilters()` solo en casos justificados (por ejemplo la tienda pública, que busca
  por slug) y documentados en el servicio.
- **Todo módulo nuevo lleva su prueba de aislamiento** (copiar `CategoriaTests`): otra tienda no lo
  ve ni lo edita.
- A futuro: Row Level Security en Postgres como segunda barrera.

## Convenciones

- **IDs**: `Guid` v7 (`Guid.CreateVersion7()` en el inicializador de la entidad).
- **Dinero**: `decimal` con precisión `(12,2)`, solo Bs.
- **Fechas**: `DateTimeOffset` (timestamptz).
- **Desactivar, no borrar**: catálogos con `Activa`/`Activo`; "eliminar" es ponerlo en false.
- **Errores**: los esperables son subclases de `DominioException` con `.Status` propio →
  `ProblemDetails`. Cualquier otra excepción sale como 500 genérico, sin `.Message` crudo.
  Errores de Postgres conocidos (único, largo, FK/CHECK, deadlock) se traducen a 400/409 por SQLSTATE.
- **Validación de texto**: `Validacion.Texto`/`TextoOpcional` (400 con mensaje claro, nunca un 500 de truncado).
- **Rutas**: privada `/api/v1/...` (JWT); pública `/public/{slug}/...` (sin login, DTOs separados que
  **nunca** incluyen costo, margen ni stock exacto).
- Idioma del código de negocio, mensajes y documentación: **español**.
- **Nada de emojis en la interfaz** (panel ni tienda pública): siempre íconos SVG de línea (en el panel `componentes/Icono.tsx`;
  en la tienda pública un `<svg>` en línea, como en `_EnlaceCarrito.cshtml`). Pedido del usuario.
- Teléfonos de demo: `7XXXXXXX`, no inventar números reales.

## Autenticación (2026-10-14)

`AuthService` + `AuthController` (`/api/v1/auth`). Probado con 9 pruebas unitarias (`AuthTests`) y de
punta a punta contra Postgres.

- **Registro** (`POST registro`): crea `Usuario` + su `Tienda` en un solo `SaveChanges`. Email en
  minúsculas; slug validado (`Validacion.Slug`: formato + lista de reservados como `api`, `admin`,
  `public`); contraseña 8–72 con letra y número; BCrypt costo 12.
- **Login** (`POST login`): mismo mensaje para email inexistente y contraseña mala, y se verifica
  contra un hash ficticio cuando el email no existe (mismo tiempo de respuesta). **Bloqueo**: 5 fallos
  por email en 10 min → 429 (`BloqueoDeLogin`, en memoria: con varias instancias pasarlo a caché
  compartida). Además, límite por IP (`Limites`): login y registro 20/min (`Auth`); refresh 120/min
  (`Renovacion`) porque lo hace sola cada pestaña abierta. Ojo: en Bolivia muchos celulares comparten la
  IP de la operadora (CGNAT); no bajar estos límites. Detrás de un proxy inverso hay que configurar
  `ForwardedHeaders`, si no todas las IPs son la del proxy.
- **Token de acceso**: JWT de 15 min con `sub` (usuario), `email`, `name` y **`tienda`**. Por ahora una
  persona opera su primera tienda (la de Id más chico = la más vieja, Guid v7); cambiar de tienda será
  un endpoint que emita otro token.
- **Refresh token**: 256 bits aleatorios en una **cookie HttpOnly** `hermes_refresh`
  (`SameSite=Strict`, `Path=/api/v1/auth`, `Secure` cuando el pedido es HTTPS). Nunca viaja en el cuerpo.
  En la base solo el **hash SHA-256** (`refresh_tokens.token_hash`). Dura 30 días (`Jwt:RefreshDias`).
  - **Rota en cada uso** (`POST refresh`): el viejo queda revocado con `reemplazado_por_id`. La revocación
    es un `UPDATE ... WHERE revocado_en IS NULL` dentro de una transacción: con dos pedidos simultáneos
    solo uno gana.
  - **Reuso = robo**: si llega un token ya revocado se revocan **todas** las sesiones del usuario.
    **Excepción** (`MargenRenovacionSimultanea`, 30 s): si fue rotado hace menos de 30 s, se rechaza
    con 401 sin revocar nada — son dos pestañas renovando a la vez (comparten la cookie). Costo
    aceptado: un ladrón que use el token dentro de esos 30 s no dispara la revocación general.
  - Si `refresh` falla **no se borra la cookie**: la pestaña que pierde la carrera borraría la cookie
    nueva y válida de la otra.
  - `POST logout` revoca y borra la cookie (idempotente). `GET yo` devuelve usuario + tienda del token.
- `TimeProvider` inyectado (en las pruebas, `FakeTimeProvider`). **Trampa**: `IMemoryCache` vence
  con el reloj del sistema, no con `TimeProvider`; por eso `BloqueoDeLogin` guarda su propio `Vence` y
  a la caché solo le da un plazo relativo para limpiar.
- **Pendiente**: verificación de email, recuperar contraseña. (Los refresh tokens vencidos ya se limpian solos: M14.)

## Imágenes (M2, 2026-10-14)

`IImagenService` + `ProcesadorImagenes` (SkiaSharp 4, MIT) + `IAlmacenArchivos`. Endpoints:
`POST /api/v1/imagenes` (multipart, campo `archivo`, con sesión), `DELETE /api/v1/imagenes/{id}`,
`GET /archivos/{clave}` (público, caché inmutable de un año).

- **Dónde se guardan: en Postgres, tabla `archivos`** (decisión del usuario: por ahora la usa una sola
  persona). Detrás de `IAlmacenArchivos`: ningún servicio sabe dónde viven. **Cuando haya varias
  tiendas → Cloudflare R2** (docs/PLAN.md, M2 y M14): otra implementación de la interfaz registrada en
  `DependencyInjection`, copiando cada fila con la **misma clave**. Los bytes van en su propia tabla a
  propósito (lección de CMP): nunca leer `Archivo.Datos` fuera de `ObtenerAsync`/`BorrarAsync`.
- `AlmacenEnBaseDeDatos.GuardarAsync`/`BorrarAsync` **solo marcan** en el contexto: se persisten con el
  `SaveChanges` del servicio, en la misma transacción que el registro `Imagen`. Foto y registro se
  guardan juntos o no se guarda nada (hay prueba).
- **Clave** = `{tiendaId}/imagenes/{imagenId}/{miniatura|catalogo|detalle}.webp` (`Imagen.ClaveDe`).
  Es la misma forma que tendrá en el bucket. `Imagen` (por tienda, filtro global) guarda medidas y
  `BytesGuardados` (para los límites por plan de M13); `Archivo` NO es por tienda: las fotos de producto
  son públicas y la protección es que la clave lleva dos GUID v7 imposibles de adivinar.
- **`ProcesadorImagenes`**: formato por los **primeros bytes** (JPG/PNG/WEBP; GIF y el resto, no), nunca por
  extensión ni Content-Type; máx. 15 MB y 64 MP (anti "bomba de descompresión"); mín. 50 px;
  **decodificación completa o nada** (`SKCodecResult.Success`: `SKBitmap.Decode(codec)` acepta un archivo
  cortado y rellena con gris, se guardaría una foto rota — lo encontró la prueba contra la API real);
  corrige la **orientación EXIF** (tabla 1–8, `Orientar`, probada una por una); 3 tamaños en WEBP
  (lado largo 200/600/1200, calidad 70/78/82), **nunca agranda**; al re-codificar **no pasan los
  metadatos (GPS incluido)**, hay prueba con un EXIF falso. **La foto original NO se guarda.**
- Pesos medidos con una foto sintética de 12 MP / 1,2 MB: 3 + 20 + 70 KB (≈ 93 KB los tres). Una foto
  real con más detalle pesará más: confirmar con fotos reales cuando se pueda.
- SkiaSharp en Linux necesita `SkiaSharp.NativeAssets.Linux` (en Windows y macOS viene incluido). Anotado en M14.
- Una subida que nunca se usa (ni producto, ni logo, ni comprobante) la borra la limpieza periódica a las 24 h (M14). La
  cuota por tienda es de M13 (`BytesGuardados`).
- Probado: 23 pruebas unitarias (`ImagenTests`) y `scripts/e2e_imagenes.py` (31 verificaciones contra la API).

## Mi tienda (M3, 2026-10-14)

`ITiendaService`/`TiendaService` + `TiendaController` (`/api/v1/tienda`): `GET`, `PUT` (nombre, descripción,
WhatsApp, color, logo) y `PUT direccion`. **Siempre la tienda de la sesión**: no hay rutas con id de tienda.

- `Tienda` **no** tiene filtro global (es el tenant): cada consulta se acota a mano por `ITiendaActual`.
- **Trampa ya pisada (la encontró una prueba)**: leer `ITiendaActual.TiendaId` — o cualquier propiedad que
  pueda lanzar — *dentro* de una expresión LINQ hace que EF envuelva la excepción en
  `InvalidOperationException` y el 403 sale como **500**. Leerlo a una variable local antes de armar la
  consulta (`TiendaIdDeLaSesion()`).
- **WhatsApp** (`Validacion.Whatsapp`): celular de Bolivia, 8 dígitos que empiezan con 6 o 7, con o sin
  `+591`/espacios/guiones/paréntesis; se guarda SIEMPRE como `+591XXXXXXXX` (el enlace `wa.me` usa esos
  dígitos). Vacío = sin WhatsApp. **Color** (`Validacion.ColorHex`): `#RRGGBB`, guardado en mayúsculas;
  validado estricto porque llegará a estilos CSS de la tienda pública. Color por defecto de una tienda nueva:
  mostaza `#F2A33A` (`Tienda.ColorPorDefecto`, el del prototipo; las anteriores siguen con su violeta).
- **Logo**: `LogoImagenId` → `imagenes` (FK circular a propósito: `imagenes.tienda_id` ↔ `tiendas.logo_imagen_id`,
  borrar la imagen lo deja en null). Debe ser una imagen de **esta** tienda (el filtro global de `Imagen` lo
  garantiza: la de otra tienda da 404). **Al cambiarlo o quitarlo se borra la imagen anterior** y sus
  archivos, DESPUÉS de guardar la tienda con el logo nuevo (si algo falla queda una foto de más, nunca una
  tienda sin logo). `ImagenMapeo.ADto` arma el `ImagenDto` con `IAlmacenArchivos.Url`: usarlo siempre.
- **Dirección** (`/{slug}`): mismas reglas que en el registro (`Validacion.Slug`, reservados incluidos);
  ocupada → 409; la misma → sin cambios. Cambiarla **rompe los enlaces compartidos**: el front pide
  confirmación. **Pendiente**: guardar las direcciones anteriores para redirigir (no entró en M10).
- Probado: 29 pruebas (`TiendaTests`, aislamiento entre tiendas incluido) y el e2e del front.
- **No estaba en M3** (necesitaba la tienda pública): el enlace, "Ver tienda", "Qué ven tus clientes" y el fondo se
  hicieron en M10; el **código QR** sigue pendiente.

## Catálogo (M4, 2026-10-14)

`IProductoService`/`ProductoService` + `ProductosController` (`/api/v1/productos`): `GET` (lista), `GET {id}`,
`POST`, `PUT {id}`, `POST {id}/archivar` y `/desarchivar`. Tablas `productos`, `variantes`, `producto_fotos`.
Es el módulo de referencia más completo (filtros, paginación, hijos, fotos); copiar de acá.

- **Un producto nunca se borra: se archiva.** Tampoco una variante: se desactiva (en M5 tendrá movimientos de
  stock). `Variante.Activa`; si la dueña saca un color y lo vuelve a poner, se reactiva **la misma fila**.
- **Variantes = Colores × Tallas** (decisión: opción A del plan, `Color` y `Talla` opcionales). Sin ninguno =
  "Único" (ambos vacíos). Color/Talla se guardan como `""` (no null) para que el índice único
  `(producto, color, talla)` valga igual en Postgres y SQLite; el DTO los muestra como `null`. Máx. 60
  variantes y 8 fotos. **`Variante.Orden`** conserva el orden en que se escribieron: **no ordenar por `Id`**
  (los Guid v7 del mismo milisegundo salen en cualquier orden).
- **Fotos**: `FotoIds` ordenados, la primera es la portada. Una imagen solo en un producto (índice único) y
  no puede ser el logo; la de otra tienda "no existe" (filtro global). Las fotos que se sacan se **borran de
  verdad** DESPUÉS de guardar el producto sin ellas. FK `producto_fotos → imagenes` en cascada: borrar una
  imagen suelta se lleva su vínculo. Sin índice único en `Orden` (al reordenar se chocarían a mitad de camino).
- **TRAMPA (la encontró una prueba)**: los hijos NUEVOS de una entidad ya rastreada se agregan con
  `_db.Variantes.Add(...)`, no solo a la lista del padre. Como su `Id` viene asignado en el inicializador
  (Guid v7), EF los detecta por navegación como *modificados* (UPDATE de una fila inexistente). Con
  `_db.Productos.Add(producto)` en la creación sí funciona (marca todo el grafo como nuevo).
- **Margen** (`Hermes.Domain.MargenProducto`): la regla vive en un solo lugar. `SinCosto` (costo 0), `Pierde`
  (ganancia ≤ 0), `Bajo` (< 20 % del precio), `Ok`. El filtro `RevisarPrecio` de la lista usa la misma
  condición en SQL (`Costo > 0 && Precio − Costo < Precio × UmbralBajo`); hay una prueba que compara ambas.
  El front muestra el estado que manda el servidor: **no duplicar la regla en el front**.
- **Listado**: más nuevos primero, **paginación por cursor** (`Cursor` = `Siguiente` de la respuesta; `Id` v7 como
  clave de orden, sin OFFSET: no repite ni saltea aunque se creen productos mientras se pagina). `Limite` se acota
  a 1–60 (24 por defecto). Búsqueda por nombre sin distinguir mayúsculas (`ToLower().Contains`, los comodines de
  LIKE no se interpretan). **Pendiente**: búsqueda sin distinguir tildes (extensión `unaccent` de Postgres).
- **Dinero**: `Validacion.Monto` rechaza más de 2 decimales (no redondea en silencio), precio > 0, costo ≥ 0 y
  tope de 1.000.000; la base tiene los CHECK `ck_producto_precio`/`ck_producto_costo`.
- **JSON de la API** (`Program.cs`, `ConfigurarJson`): enums como texto y números estrictos, configurado en las
  opciones de MVC **y** en las "http" (el generador de OpenAPI lee estas últimas). Sin eso el esquema decía
  `EstadoPrecio: number` y `precio: number | string` y los tipos del front salían mal. Los parámetros de
  `[FromQuery]` de un record salen en el esquema con mayúscula inicial (`Buscar`, `CategoriaId`…).
- **Pendiente (otros módulos)**: stock y "Quedan N" (M5), proveedor del producto (M7), costeo (M8), insignia
  de Instagram e `instagram_media_id` (M11), stock mínimo (M5).
- Probado: 34 pruebas (`ProductoTests`, aislamiento entre tiendas incluido) + `scripts/e2e_productos.py`
  (contra Postgres: cursor sobre uuid, búsqueda, filtro decimal, cascadas) + el e2e del front.

## Stock (M5, 2026-10-14)

`IStockService`/`StockService` + `StockController` (`/api/v1/stock`): lista, `resumen`, `{productoId}`,
`{productoId}/movimientos`, `POST ingresos` ("Llegaron más") y `POST ajustes` (corregir). Tabla `movimientos_stock`.

- **El stock NO se guarda: es `SUM(Cantidad)` de los movimientos de las variantes ACTIVAS** (`ConsultasStock`).
  Nunca hay una cifra cacheada que se desincronice. `MovimientoStock` es un libro: **nunca se edita ni se borra**
  (corregir es otro movimiento `Ajuste`). `Saldo` = lo que quedó después; es solo una foto para el historial (CHECK
  `saldo >= 0`, `cantidad <> 0`), el stock real siempre es la suma.
- **Control por producto** (decisión del usuario): `Producto.ControlaStock` (por defecto **true**) y `StockMinimo`
  (por defecto 3, como el prototipo). Apagado = "por encargo" (piezas únicas, hecho a pedido, servicios): sin
  stock, siempre disponible, vender no descuenta ni bloquea. **Apagarlo no borra el historial** y encenderlo retoma
  la suma. Un producto sin control rechaza llegadas y correcciones (409). Los productos que ya existían quedaron
  con control encendido (la migración tiene `defaultValue` a mano: EF genera `false` para el bool, ¡revisar siempre
  los `AddColumn` de bool/int en migraciones!).
- **Regla del estado** (`Hermes.Domain.ReglaStock`): `SinControl`, `Agotado` (≤ 0), `Pocos` (≤ mínimo), `Ok`. La
  pantalla de Stock la usa también en SQL para ordenar y filtrar; hay pruebas que comparan las dos.
- **`IOperacionesStock`** (`OperacionesStock`) es lo que cambia el stock y lo que usa **M6 (ventas)**:
  `AplicarAsync(tipo, líneas, nota, costo, ignorarSinControl, ct, ventaId?)` y `FijarTotalesAsync`. **NO guarda ni hace commit:**
  exige una **transacción abierta** por quien la llama (si no, `InvalidOperationException`) y este hace
  `SaveChanges` + commit, así una venta y sus descuentos van juntos o no van. `ignorarSinControl` salta los
  productos sin control (venta mezclada). Una variante repetida en la operación se junta en una sola línea.
- **Candado por variante, la pieza crítica**: antes de leer el stock se toma el candado de la fila `variantes`
  con un `UPDATE` que no cambia nada (como `BloquearProductoAsync` de CMP), siempre **ordenado por Id** para que
  dos operaciones sobre las mismas variantes no se traben (deadlock). Se lee el stock DESPUÉS del candado.
  Verificado con Postgres real: 12 ventas simultáneas de las últimas 5 unidades → pasan exactamente 5. **Sin el
  candado pasaron 7** (stock negativo): se probó sacándolo a propósito. SQLite no tiene candados de fila, por eso
  esa prueba corre solo con `HERMES_TEST_PG` (sin la variable, sale sin probar nada; ver Comandos).
- **Llegaron más** (`IngresoDto`): cantidades por variante (todas del mismo producto). `CostoUnitario` opcional: si
  viene y es > 0, pasa a ser el costo del producto ("el costo se actualiza cuando llega mercadería") y la
  ganancia se recalcula sola. **Corregir** (`AjusteDto`): se dice cuánto HAY (no la diferencia); el servidor calcula
  la diferencia dentro del candado, no inventa movimientos si no cambió nada (400 "No cambió ninguna cantidad") y el
  motivo es opcional ("Corrección de stock" por defecto). Las variantes de la operación tienen que ser ACTIVAS del
  producto indicado (una variante propia de OTRO producto, o de otra tienda, da 404).
- **No se puede sacar una variante con stock** al editar el producto: hay que corregirla a 0 antes (si no, esas
  unidades "desaparecerían" del total sin rastro). Solo si el producto lleva control; el contexto se limpia
  (`ChangeTracker.Clear()`) antes de lanzar para que nada quede a medias.
- **Listado** (`ListarAsync`): más urgente primero (agotado → por agotarse → bien → por encargo; dentro de cada
  grupo, más nuevo primero), con **cursor compuesto** `"{prioridad}.{id}"` (opaco; uno inválido da 400). Vistas
  `Todos`/`AReponer`/`PorEncargo`. Los archivados no cuentan. `ResumenAsync` valora la mercadería a costo y a precio
  de venta solo con productos con control. El historial se pide aparte, al abrir una fila (`Limite` 3 en la UI).
- **Pendiente**: proveedor en "Llegaron más" (no entró en M7), insignia de stock en el menú, alertas en Inicio (M9),
  idempotencia de los POST de stock (un doble toque suma dos veces: son hechos explícitos; ventas y compras sí la tienen).
- Probado: 37 pruebas (`StockTests`, aislamiento entre tiendas incluido), la de concurrencia con Postgres real y
  `scripts/e2e_stock.py` (la API completa contra Postgres: orden, cursor compuesto, resumen, cascadas de reglas).

## Ventas (M6, 2026-10-14)

`IVentaService`/`VentaService` + `VentasController` (`/api/v1/ventas`): `GET` (listado), `GET resumen`, `GET productos`
(qué ofrecer en "Nueva venta"), `GET {id}`, `POST` (registrar) y `POST {id}/anular`. Tablas `ventas` y `venta_lineas`;
`movimientos_stock.venta_id` enlaza cada descuento/devolución con su venta.

- **Venta rápida** (decisión del usuario, opción A): productos + cantidades + forma de pago (`Efectivo`, `Qr`,
  `Tarjeta`, `Transferencia`, guardado como texto) + cliente **opcional** (≤ 120). Sin descuentos, sin precio
  editable y sin fecha manual (no incluidos a propósito; la fecha es la del servidor).
- **Cada línea guarda una foto del momento**: `Descripcion` ("Cartera (café · M)"), `PrecioUnitario` y
  `CostoUnitario`. Editar o renombrar el producto después NO cambia la venta. `CostoUnitario = 0` significa "sin
  costo cargado": **no se inventa la ganancia** (`VentaDto.Ganancia` es `null` si alguna línea no tiene costo; el
  resumen suma solo lo que tiene costo y devuelve `UnidadesSinCosto` para avisar cuánto quedó afuera).
- **Todo o nada**: `RegistrarAsync` valida (clave ≠ vacía, método definido, cliente ≤ 120, 1..50 líneas, cantidad
  1..100000; las variantes repetidas se funden en una línea), abre una transacción, inserta la venta, llama a
  `IOperacionesStock.AplicarAsync(..., ventaId)` (candado por variante, ver Stock) y hace commit. Sin stock
  suficiente → 409 "No alcanza el stock de X: hay 1 y se necesitan 5" y **no se guarda nada** (ni las líneas que sí
  alcanzaban). Productos sin control de stock se venden siempre y no generan movimiento (`DescontoStock = false`).
  Archivado, variante que ya no se vende o de otra tienda → 400/404.
- **Idempotencia** (`ClaveIdempotencia`, un Guid que genera el celular al armar la venta): índice único
  `(TiendaId, ClaveIdempotencia)`. La misma clave devuelve la misma venta (200 en vez de 201) y **no vuelve a
  descontar**, aunque el reintento llegue con otro contenido. La venta se inserta primero (reserva la clave) y en
  una carrera el que pierde recibe la venta ganadora (`catch (DbUpdateException)` + `ChangeTracker.Clear()`).
  Cada tienda tiene sus propias claves. Verificado con Postgres real: 10 reintentos simultáneos → una sola venta.
- **Anular** (nunca se borra una venta): `UPDATE ... WHERE NOT Anulada` atómico (cinco anulaciones simultáneas →
  una gana, las otras 409 `VentaYaAnuladaException`). Devuelve el stock **solo de las líneas con `DescontoStock`**
  (movimiento `Devolucion`, nota "Venta anulada") y lo hace **aunque la variante se haya desactivado después**
  (`OperacionesStock` permite `Devolucion` sobre variantes inactivas, nada más). La venta conserva sus números pero
  deja de contar en listados y resumen.
- **Listado**: más nueva primero, cursor por Id, `Desde` (incluido) / `Hasta` (excluido) como instante UTC (el front
  manda los límites del día/semana/mes **en la hora de quien mira**), anuladas ocultas salvo `IncluirAnuladas`.
  `Detalle` resume "2 Cartera · 1 Aretes · y 1 más" (el orden entre líneas de la misma venta no está garantizado:
  comparten milisegundo). `Venta.Fecha` es `DateTime` UTC (no `DateTimeOffset`) porque SQLite no compara
  `DateTimeOffset` en SQL; los totales se suman **en memoria** porque SQLite tampoco suma decimales.
- **Productos para vender** (`GET productos`): los vendibles primero (más nuevo primero) y los agotados (con
  control y sin unidades) al final, con cursor compuesto `"{prioridad}.{id}"`; trae variantes con `Stock` por
  variante (null si el producto no lleva control) y `Portada`. Los archivados no se ofrecen. Búsqueda por nombre.
- **Pendiente**: descuentos / precio editable / fecha manual (no incluidos), búsqueda sin tildes (`unaccent`).
  (El medidor de gastos en la pantalla de venta se hizo en M9.)
- Probado: 27 pruebas (`VentaTests`) + 3 de concurrencia con Postgres real (12 ventas de las últimas 5 unidades →
  pasan 5; 10 reintentos con la misma clave → 1 venta; 5 anulaciones → 1) + `scripts/e2e_ventas.py` (la API completa
  por HTTP con peticiones simultáneas de verdad: 5 de 12, una sola venta con 10 reintentos, una anulación entre 5).

## Proveedores y compras (M7, 2026-10-14)

`IProveedorService`/`ProveedorService` + `ProveedoresController` (`/api/v1/proveedores`: `GET`, `GET {id}`, `POST`,
`PUT {id}`, `POST {id}/activar|desactivar`) e `ICompraService`/`CompraService` + `ComprasController`
(`/api/v1/compras`: `GET`, `GET resumen`, `GET comparador/{productoId}`, `GET {id}`, `POST`, `POST {id}/recibir`,
`/pagar`, `/anular`). Tablas `proveedores`, `compras`, `compra_lineas`; `movimientos_stock.compra_id` enlaza cada
llegada y reversión con su compra.

- **Dos ejes independientes** (no un solo estado): `Recibida` (llegó la mercadería) y `Pagada` (ya se le pagó).
  `EstadoCompra` del DTO = `EnCamino` / `Recibida` / `Anulada`; `Pagada` va aparte (se puede pagar antes o después).
  Una compra **nunca se borra: se anula**. El total sale de las líneas (`CompraLinea`: variante, cantidad, costo del
  momento, nombre del momento, `Orden`, `SumoStock`). **Las líneas se ordenan por `Orden`, no por `Id`** (misma
  trampa de los Guid v7 del mismo milisegundo que en `Variante.Orden`: lo encontró una prueba).
- **Registrar** valida (clave ≠ vacía, proveedor existente y ACTIVO, 1..50 líneas, cantidad 1..100000, costo > 0 con
  máx. 2 decimales, **la misma variante dos veces se rechaza**: con costos distintos sería ambiguo, notas ≤ 300) y no
  toca stock ni costo: queda **en camino**. Producto archivado o variante que ya no se vende → 400. `Recibida` /
  `Pagada` en el DTO permiten anotar una compra que ya llegó o ya se pagó (la API lo soporta; **el front registra en
  camino y después llama a `recibir`** para recibir los avisos de margen en la respuesta).
- **Idempotencia**: igual que ventas (`ClaveIdempotencia`, índice único `(TiendaId, ClaveIdempotencia)`, 201 / 200).
  Verificado con Postgres: 10 reintentos simultáneos con `Recibida = true` → una compra, stock sumado una vez.
- **Recibir** (una sola vez, transición atómica): `UPDATE ... WHERE NOT Recibida AND NOT Anulada` dentro de una
  transacción → `IOperacionesStock.AplicarAsync(Ingreso, ..., compraId)` (candado por variante) → **el costo de cada
  producto pasa a ser el de su línea** (también los que no llevan control de stock: no suman unidades, `SumoStock =
  false`) → devuelve `Avisos`: los productos cuyo margen con el costo nuevo es `Pierde` o `Bajo` (misma regla
  `MargenProducto` del catálogo; el front solo escribe el texto). Seis recepciones simultáneas → una gana, cinco 409.
  `LineaMovimiento` ahora lleva un `CostoUnitario` opcional **por línea** (queda en el movimiento; el costo del
  producto lo actualiza `CompraService`, no `OperacionesStock`).
- **Pagar**: `UPDATE ... WHERE NOT Pagada AND NOT Anulada`; una sola vez; también antes de recibir (anticipo).
- **Anular**: marcar `Anulada` TOMA el candado de la fila (una recepción simultánea espera y después su
  `UPDATE ... WHERE NOT Anulada` ya no pega); recién después se lee si estaba recibida. Si lo estaba, **resta lo que
  sumó** (`SumoStock`) con movimientos `Reversion` (nuevo `TipoMovimiento`, nota "Compra anulada"); si esas unidades ya
  se vendieron → `StockInsuficienteException` 409 y no se anula nada (todo en una transacción). **El costo del
  producto NO vuelve al anterior** (no hay forma de saber cuál era el bueno: pudo cambiar a mano en el medio); el
  diálogo del front lo dice. Recibir contra anular a la vez (5 vueltas en Postgres y por HTTP): nunca queda stock de
  una compra anulada.
- **Lecturas**: listado más nueva primero con cursor por Id y vistas `Todas` (incluye anuladas, marcadas),
  `EnCamino` (sin recibir ni anular) y `PorPagar` (sin pagar ni anular), filtro por proveedor; `resumen` = deuda
  total con proveedores, cuántas compras, y pedidos en camino con su valor; `comparador/{productoId}` = el ÚLTIMO
  costo de cada proveedor para ese producto (las anuladas no cuentan), el más barato primero (`EsElMasBarato`, con
  empate ambos); en el detalle del proveedor, "lo último que le pagaste" por producto. Los totales se suman en
  memoria (SQLite no suma decimales).
- **Proveedor**: nombre ≤ 120 único por tienda **sin distinguir mayúsculas** (409; el índice único corta la carrera),
  WhatsApp opcional normalizado a `+591XXXXXXXX` (`Validacion.Whatsapp`), notas ≤ 300. **Se desactiva, no se
  borra** (sus compras lo referencian); uno desactivado no recibe pedidos nuevos pero sus compras siguen. El listado
  trae lo que se le debe y cuántos pedidos tiene en camino.
- **No incluido** (decidido para no inflar M7): proveedor dentro de "Llegaron más" y proveedor por producto
  (`Producto.ProveedorId`), compras con fecha manual, pagos parciales (una compra está pagada o no), devolver el costo al
  anular. Costeo y precio sugerido son M8.
- Probado: 34 pruebas (`CompraTests`, aislamiento entre tiendas incluido) + 3 de concurrencia con Postgres real +
  `scripts/e2e_compras.py` (la API completa por HTTP con peticiones simultáneas de verdad).

## Costeo y precio sugerido (M8, 2026-10-14)

`ICosteoService`/`CosteoService` + `CosteoController` (`/api/v1/productos/{productoId}/costeo`: `GET`, `POST calcular`,
`PUT`, `DELETE`). Tabla `costeos` (uno por producto, índice único; migración `AgregarCosteo`, solo una tabla nueva).

- **Componentes por unidad** (≥ 0, 2 decimales, al menos uno > 0, total ≤ 1.000.000): `Material`, `Empaque`,
  `ManoDeObra`, `CostoFijo` (la parte que le toca de luz, alquiler…) + `MargenObjetivo` (entero 1..90, por defecto 40).
- **La regla vive en `Hermes.Domain.Entities.ReglaCosteo`** (el front solo muestra): `Total` = suma de componentes;
  `PrecioSugerido` = `costo ÷ (1 − margen)` **redondeado HACIA ARRIBA al boliviano entero** (cobrar Bs 63,17 no es
  práctico y redondear hacia abajo comería el margen; hay una prueba que lo comprueba). El margen es sobre el
  PRECIO, igual que `MargenProducto` del catálogo (40 % = de cada Bs 100 cobrados, Bs 40 son ganancia).
- **El precio sugerido NO se guarda y el precio de venta NUNCA se toca solo** (lo pide el plan): `PUT` deja
  `Producto.Costo` = total del costeo y no toca `Producto.Precio`. `CosteoDto` trae lo que se necesita para comparar
  (sugerido, ganancia con el sugerido, precio actual, ganancia actual, `MargenActual` %, `EstadoActual` con
  `MargenProducto`). `POST calcular` cuenta sin guardar (la calculadora mientras se escribe).
- **Invariante: con costeo, `Producto.Costo` = total de los componentes, siempre.** Se guarda en el producto porque
  ventas, stock y catálogo ya leen ese número. Por eso: (1) `ProductoService.ActualizarAsync` **ignora el costo
  directo** que llegue si hay costeo; (2) **`CostoDelProducto.FijarPagadoAsync`** es el único lugar donde "llega un
  costo nuevo" (compra recibida y "Llegaron más" con costo): con costeo el costo pagado pasa a ser el componente
  **Material** (y el costo del producto el nuevo total); sin costeo es el costo directo. Cubierto en pruebas y en
  `scripts/e2e_costeo.py`.
- **Quitar el costeo** (`DELETE`, idempotente, 204): el producto conserva el último total como costo directo y vuelve
  a poder editarse. Un producto no se borra, así que su costeo tampoco (FK Restrict). Sin costeo, `GET` devuelve
  `Tiene = false`, el costo directo como `CostoTotal` y el margen por defecto.
- **No incluido**: costeo por variante, costos que cambian con el volumen, redondeos distintos al entero, margen sobre
  costo (markup). Costos fijos del negocio y gastos son M9.
- Probado: 16 pruebas (`CosteoTests`, aislamiento entre tiendas incluido) + `scripts/e2e_costeo.py` (API completa por
  HTTP: validaciones, invariante del costo con compras y "Llegaron más", 8 guardados simultáneos, otra tienda).

## Gastos e Inicio (M9, 2026-10-14)

`IGastoService`/`GastoService` + `GastosController` (`/api/v1/gastos`: `GET`, `POST`, `POST {id}/anular`) e
`IInicioService`/`InicioService` + `InicioController` (`GET /api/v1/inicio?Desde&Hasta`). Tabla `gastos` (migración
`AgregarGastos`, solo una tabla nueva).

- **Gasto** = lo que se gasta en el negocio SIN contar mercadería (esa son las compras): `Concepto` (≤ 120), `Monto`
  (> 0, 2 decimales, tope 1.000.000), `Fecha` (cuándo se pagó: por defecto ahora; una pasada se acepta desde el 2000;
  futura → 400, con un día de margen por la zona horaria). **Nunca se borra: se anula** (`UPDATE ... WHERE NOT Anulado`,
  una sola gana) y deja de contar. Mismo esquema de **idempotencia** que ventas y compras (`ClaveIdempotencia`, 201 /
  200; 10 reintentos simultáneos → un solo gasto, verificado contra Postgres).
- **Listado**: más nuevo primero por `Id` (cursor), `Desde` incluido / `Hasta` excluido, anulados ocultos salvo
  `IncluirAnulados`. **`Total` es de TODO el período, no de la página** (se suma en memoria los montos vigentes).
  Ojo: ordena por creación, no por `Fecha`: un gasto cargado con fecha vieja sale primero si se cargó último.
- **El Inicio no tiene datos propios**: `InicioService` compone `IVentaService.ResumenAsync`, `IGastoService.TotalAsync`,
  `ICompraService.ResumenAsync`, `IStockService.ResumenAsync/ListarAsync` (vista `AReponer`, 5 más urgentes) y lo
  último de ventas/compras/gastos. Se piden de a uno (comparten el contexto de la base). El front manda el MES de
  quien mira (`rangoDelMes`).
- **"Ya cubriste el X % de los gastos"** = `floor(Ganancia ÷ Gastos × 100)`, **redondeado hacia abajo** (no se promete de
  más), mínimo 0 (ganancia negativa → 0), puede pasar de 100; `null` si no hay gastos. `Ganancia` es la bruta de las
  ventas del período, solo con líneas de costo conocido (`UnidadesSinCosto` avisa lo que quedó afuera);
  `Resultado = Ganancia − Gastos`. Ventas y gastos dependen del período; **`PorPagar`/`EnCamino` (compras) y
  `ParaReponer`/`Agotados`/`PorAgotarse` (stock) NO**: son lo que hay que atender hoy. `Recientes` = las 6 últimas
  actividades mezcladas por fecha (ventas, compras y gastos NO anulados), sin importar el período.
- **No incluido**: categorías de gasto, gastos recurrentes, editar un gasto (se anula y se vuelve a anotar), gráficos.
- Probado: 19 pruebas (`InicioTests`, aislamiento entre tiendas incluido) + `scripts/e2e_inicio.py` (la API completa por
  HTTP: totales y rangos en SQL real, 10 reintentos simultáneos, anulación, otra tienda).

## Tienda pública (M10, 2026-10-14)

Proyecto **`Hermes.Public`** (Razor Pages, puerto 5014, sin login) + rutas JSON `/public/{slug}` en la API. Los dos usan
`IPublicoService`. Migración `AgregarTiendaPublica` (4 columnas en `tiendas`).

- **Por qué un proyecto aparte con Razor Pages** (la opción recomendada del plan): el enlace que se comparte por WhatsApp
  necesita vista previa con foto, y el rastreador de WhatsApp **no ejecuta JavaScript**: una SPA de React no la genera. La
  página se arma en el servidor con `og:title`, `og:description`, `og:image` (absoluta), `og:url` y `canonical`. Comparte
  los secretos de la API (`UserSecretsId` igual: misma cadena de Postgres) y **no tiene JWT**.
- **Aislamiento entre tiendas, sin desactivar el filtro global**: `PublicoService` resuelve slug → tienda (`Tienda` no tiene
  filtro) y la **fija** en `TiendaPublicaContexto` (un objeto por pedido). Desde ahí el filtro global acota Producto,
  Variante, Imagen y Movimiento a esa tienda (un producto de otra tienda "no existe" aunque se pida su id). En la API,
  `TiendaActualDesdeToken` usa ese contexto primero **solo si `PublicoService` lo fijó en este pedido**; en `Hermes.Public`
  el contexto ES el `ITiendaActual`.
- **Lo que NUNCA sale**: los DTOs públicos (`PublicoDto.cs`) **no tienen dónde poner** costo, margen, ganancia ni stock
  exacto, solo `Disponible` (bool?). Hay dos pruebas: una por **reflexión** (ningún DTO público ni sus anidados tiene un
  campo cuyo nombre contenga costo/margen/ganancia/stock/cantidad/movimiento/proveedor/compra) y otra que **serializa**
  las respuestas y busca esas palabras y el costo como valor. `scripts/e2e_publico.py` lo repite sobre el JSON y el HTML
  reales. Si se agrega un campo a un DTO público, pensar primero si un cliente debe verlo.
- **Qué se muestra**: productos no archivados y `VisibleEnTienda`; variantes **activas**; categorías solo las que tienen algo
  visible. **Tienda apagada (`Tienda.Activa = false`), inexistente, producto oculto/archivado/de otra tienda → el mismo
  404** (`TiendaPublicaNoEncontradaException`): no se distingue "no existe" de "está apagada".
- **Lo que la dueña puede ocultar** (`GET/PUT /api/v1/tienda/publica`, `ConfiguracionPublicaDto`): `Publicada`,
  `MostrarPrecios` (`Precio` = null), `MostrarDisponibilidad` (`Disponible` = null) y `PedidosPorWhatsapp` (`Whatsapp` =
  null; sin esto no hay botón ni número), más `Fondo` (Claro / Crema / Oscuro). **Todo viene encendido** y las tiendas
  existentes quedaron así (la migración tiene los `defaultValue` puestos a mano: EF generó `false` y `""`; 84 tiendas
  verificadas). **Ocultar la disponibilidad también oculta el ORDEN**: si no, ordenar los agotados al final lo revelaría.
- **Listado**: lo disponible primero y los agotados al final, cursor compuesto `"{prioridad}.{id}"` (como Nueva venta);
  búsqueda por nombre; filtro por categoría; 24 por página (máx. 48).
- **Caché y límite**: respuestas con `Cache-Control: public, max-age=30` → **un cambio de la dueña (ocultar precios,
  despublicar) puede tardar hasta 30 s en verse**. Límite por IP **240/min** (política `publico` en la API; global en
  `Hermes.Public`; alto porque en Bolivia muchos celulares comparten la IP de la operadora). Verificado: pasadas ~240
  visitas, 429.
- **Seguridad del sitio**: Razor escapa todo (hay prueba con `<script>` en el nombre y `onerror` en la descripción); el
  codificador deja las letras con tilde legibles pero sigue escapando `< > & " '`; encabezados `X-Content-Type-Options:
  nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`; enlaces a WhatsApp con `rel="noopener noreferrer"`; la dirección se
  valida (`Slugs.Valido`) **antes** de ir a la base (bots pidiendo `/wp-login.php` no cuestan una consulta).
- **Trampa ya pisada**: una restricción `regex` en la ruta (`{slug:regex(^[[a-z0-9]]…)}`) **no calzaba** y todo daba 404
  sin ningún log. Se usa `{slug}` y se valida en código. Y `dotnet run` deja un `Hermes.Public.exe` hijo: matar por NOMBRE
  del proceso (`Get-Process Hermes.Public`), no solo `dotnet.exe`, o el puerto sigue ocupado por la versión vieja.
- **Pedido por WhatsApp**: un producto a la vez (variante + cantidad), sin carrito. `wa.me/591XXXXXXXX?text=` con
  "Hola {tienda}! Quiero pedir: 3 × Cartera (negro) — Bs 540. Lo vi en {enlace del producto}". Sin JavaScript sale con la
  primera variante y cantidad 1; `publico.js` lo rearma al cambiar variante o cantidad (mismo texto que `Formato.MensajeDePedido`).
- **Imagen de la vista previa** (`/{slug}/og.jpg?p={producto}`): **JPEG** 1200×630 (WhatsApp y otros a veces no leen WEBP)
  con la foto del producto ENTERA (sin recortar) sobre el fondo de la tienda; sin producto usa el logo o la primera foto;
  sin ninguna, el color de la marca con el nombre. Caché 1 h. **Pendiente de verificar con WhatsApp de verdad** (acá solo se
  verificaron las etiquetas y que la imagen sea un JPEG de 1200×630).
- Las fotos siguen en la base y las sirve la API (`/archivos/...`): el sitio público arma la dirección con
  `Publico:ArchivosBaseUrl` (en producción, mismo dominio o R2).
- **No incluido**: código QR, redirección de direcciones viejas (cambiar la dirección rompe los enlaces: el panel lo
  advierte), carrito, sitemap/robots, vista previa embebida en el panel (hay un enlace "Ver mi tienda"), colores libres
  de fondo, idioma/moneda configurables.
- Probado: 20 pruebas (`PublicoTests`, incluido el aislamiento entre tiendas y las dos de "nunca sale") +
  `scripts/e2e_publico.py` (API + HTML + imagen + escapado + límite por IP; **espera sola** si la IP quedó limitada de la
  corrida anterior) + `Hermes.Web/scripts/publico.mjs` (en Edge de verdad: sin desborde horizontal a 390 px y el
  JavaScript del pedido).

## Lector de Instagram por reglas (M11, 2026-10-14)

`IInstagramService`/`InstagramService` + `InstagramController` (`/api/v1/instagram`: `POST leer`, `POST importar`,
`GET borradores`, `GET borradores/{id}`, `POST borradores/{id}/publicar`, `POST borradores/{id}/descartar`). Tabla
`borradores`, migración `AgregarBorradores`. **Sin Meta todavía** (M12): la dueña pega el texto de la publicación.

- **El lector es lógica pura** (`Hermes.Application/Instagram/ParserPublicacion.cs`): sin base de datos, sin red, sin IA.
  Reglas: **nombre** = primera línea con texto (sin emojis, hashtags ni precio); **precio** `Bs 120` / `120 Bs` / `Precio: 120` /
  `Bs. 58,50` / `Bs 1.200` (punto + 3 dígitos = miles); **oferta** `Antes Bs 200 · Ahora Bs 150` (precio = el de "ahora", el
  otro es `PrecioAnterior`); `Colores: rojo, negro y azul`, `Tallas: S-M-L` (`Talla: única` = ninguna); **stock** `Quedan 5` /
  `Última unidad` / `Stock: 3` / `8 unidades`; **categoría** = el primer hashtag que no sea genérico (`#carteras` sí;
  `#hechoamano`, `#bolivia`, `#cochabamba`… no); **carrusel** = 2 o más líneas numeradas con precio (`1. Cartera Bs 120`) →
  un producto por línea, y lo suelto (colores, tallas, stock, categoría) vale para todos. Ignora mayúsculas y tildes
  (`Plano()` devuelve un texto con el MISMO largo, así las posiciones coinciden con el original). Límites: 5000
  caracteres, 20 productos, nombre 120, descripción 1000, colores/talla largos se descartan con aviso.
- **NUNCA inventa**: lo que no encuentra queda en `null` y se dice en `Avisos` ("No encontramos el precio", "Encontramos
  varios precios (Bs 95, Bs 120): revisá cuál es", "Solo encontramos un precio anterior"). Un texto sin precio igual entra a la
  bandeja, marcado `Faltan = ["precio"]`.
- **Nada llega al catálogo solo**: `importar` deja **borradores PENDIENTES** (`Borrador`, tope 200); recién `publicar`
  crea el producto, con lo que la dueña revisó (no con lo leído). Un borrador nunca se borra: pasa a `Publicado` (con su
  `ProductoId`) o `Descartado`.
- **Publicar** (una sola vez): primero **reserva** el borrador con un `UPDATE ... WHERE Estado = Pendiente` (6 pedidos
  simultáneos → uno gana, cinco 409 `BorradorYaResueltoException`; verificado con Postgres y por HTTP), después crea el
  producto con `IProductoService`; **si la creación falla (precio 0, foto que no existe…), el borrador vuelve a
  Pendiente** para corregirlo. Si el proceso se cae justo entre las dos cosas, el borrador queda `Publicado` sin
  producto (raro; no hay recuperación automática). **"Quedan N"** solo se carga como stock si el producto lleva control y
  tiene **una sola variante**; con varias no se reparte y la respuesta trae un `AvisoStock` ("cargá cuántas hay de cada
  una en Stock"). Si cargar el stock falla, el producto igual queda publicado y se avisa.
- **Los borradores de una publicación salen en su orden** (el 1 arriba): sus `Id` se escalonan 1 ms (los Guid v7 del mismo
  milisegundo no tienen orden). El `Id` sale del reloj inyectado: en pruebas hay que avanzar el `FakeTimeProvider`.
- **No incluido**: fotos (Instagram no las trae sin Meta: se agregan al revisar), conectar con Instagram (M12), reconocer
  descuentos en porcentaje, monedas distintas de Bs, idempotencia de `importar` (pegar dos veces el mismo texto crea dos
  borradores: se descartan), contador de pendientes en el Inicio.
- Probado: 19 pruebas del lector (`ParserPublicacionTests`: **28 textos tipo Instagram** en una tabla que lista todas las
  diferencias juntas + casos límite) + 18 de la bandeja (`InstagramTests`, aislamiento entre tiendas y la carrera en
  Postgres) + `scripts/e2e_instagram.py` (API completa por HTTP, 6 publicaciones simultáneas).
- **Bug real que encontró la tabla de textos**: la palabra "era" (de "antes… era") calzaba DENTRO de "cart**era**" y
  "puls**era**" y se leía como precio anterior. Las etiquetas de precio llevan límites de palabra `(?<![a-z])…(?![a-z])`.

## Conexión con Instagram (M12, 2026-10-15)

`IInstagramConexionService`/`InstagramConexionService` + `InstagramConexionController` (`/api/v1/instagram/conexion`: `GET`
estado, `GET url`, `POST` conectar, `DELETE`, `POST sincronizar`) + `SincronizacionInstagramPeriodica` (solo en la API). Todo en
`Hermes.Infrastructure/Instagram/`. Tablas `instagram_conexiones`, `instagram_publicaciones`, `borrador_fotos` (con RLS) y
`borradores.instagram_media_id` / `enlace`. Migración `AgregarConexionInstagram`.

- **API de Meta**: "Instagram API with Instagram Login" (cuentas profesionales, sin página de Facebook), verificada en la
  documentación oficial en octubre de 2026: autorizar en `instagram.com/oauth/authorize` (`scope=instagram_business_basic`),
  código → token corto (`POST api.instagram.com/oauth/access_token`) → token largo de 60 días (`GET graph.instagram.com/access_token`,
  `ig_exchange_token`) → `me?fields=user_id,username` → `me/media` con `children`. Renovar: `refresh_access_token` (solo si tiene
  más de 24 h; se renueva cuando faltan menos de 15 días). Error 190 = token inválido → `NecesitaReconectar`.
- **Sin app de Meta** (`Instagram:AppId/AppSecret/RedirectUri/ClaveCifrado` vacíos) todo queda apagado y el panel lo dice; las
  dueñas siguen pegando el texto (M11). Configurar: `docs/DESPLIEGUE.md` → "Conectar con Instagram". **Para servir a tiendas
  ajenas Meta exige "Advanced Access" (revisión de la app)**; sin eso solo conectan las cuentas agregadas como testers.
- **Seguridad**: el token se guarda **cifrado** (AES-GCM, `SecretosInstagram`) y nunca sale de la API; el `state` del OAuth va
  **firmado** (HMAC) con la tienda y vence en 15 min (si no, alguien podría atar su cuenta a la tienda de otro); las fotos se
  bajan solo por https (`SoloHttps`) con tope de 15 MB.
- **Sincronizar** (`SincronizarAsync`): trae hasta 25 publicaciones; las ya vistas (`instagram_publicaciones`, índice único por
  tienda) no se repiten. La **primera vez** solo entra lo de los últimos 30 días y lo viejo queda anotado como visto. **Solo
  entra como producto lo que tiene PRECIO** ("¡Gracias por los 1000 seguidores!" no llega a la bandeja; pegado a mano sí, como
  en M11). Fotos: un producto → todas (hasta 8); carrusel con N productos y N fotos → una cada uno; si no coinciden, ninguna
  (aviso); video → la portada. Las fotos se suben con `IImagenService` (cuentan para el plan; sin espacio, el borrador llega
  sin fotos y avisa). "Sincronizar ahora" seguido (< 1 min) no le pregunta a Meta. Nada llega al catálogo solo: todo va a la
  bandeja de M11.
- **Las fotos de un borrador son del borrador** (`borrador_fotos`): la limpieza no las toca mientras está Pendiente (si se
  descarta, sí) y el formulario de revisión del front **no las borra al salir** (antes borraba toda foto "sin guardar").
- **Tarea periódica**: recorre las conexiones activas (cruzando tiendas con `AccesoATodasLasTiendas`) y sincroniza cada una en
  su propio ámbito con la tienda **fijada en `TiendaPublicaContexto`** (el `ITiendaActual` de la API la usa cuando no hay
  sesión): filtro global y RLS valen igual que con la dueña.
- **Probar sin Meta**: `scripts/meta_falso.py` (puerto 5099) imita las respuestas documentadas; la API se arranca con
  `Instagram__Url*` apuntando ahí y `Instagram__SoloHttps=false` (ver el encabezado del script). `scripts/e2e_instagram_conexion.py`
  recorre todo por HTTP y el e2e del panel lo hace con clics si encuentra el Meta falso.
- Probado: 20 pruebas (`InstagramConexionTests` con un Instagram falso en memoria, `ClienteInstagramGraphTests` con respuestas de
  Meta armadas a mano) — 367 en total — + `e2e_instagram_conexion.py` (21 verificaciones) + el e2e del panel. **NO probado
  contra Meta real** (no hay app todavía).

## Producción (M14, 2026-10-14)

Cómo desplegar: **`docs/DESPLIEGUE.md`** (VPS + Docker: `deploy/docker-compose.yml`, Caddy con HTTPS automático, Postgres,
respaldo diario). El `docker compose` completo NO se pudo levantar en esta PC (no hay Docker): sí se probaron por separado la
API en modo Production, la publicación para Linux (`libSkiaSharp.so` incluido) y la Row Level Security con un rol sin privilegios
corriendo TODAS las pruebas de punta a punta.

- **Código compartido API + sitio público**: `src/Compartido/Produccion.cs`, ENLAZADO en los dos `.csproj` (`<Compile Include>`):
  `ConfigurarLogs` (Serilog: legible en desarrollo, JSON de una línea en producción; Sentry solo si hay `Sentry:Dsn`, errores
  ≥ Error, `SendDefaultPii = false`), `ConfigurarProxy` + `UsarProxyYLogs` (ForwardedHeaders confiando SOLO en `Proxy:Redes` /
  `Proxy:Ips`; va primero en el pipeline, antes del límite por IP) y `MapearSalud` (`GET /salud`: 200 si la base responde, 503 si
  no; sin límite de pedidos). HSTS + redirección a HTTPS fuera de desarrollo. Scalar/OpenAPI solo en desarrollo.
- **Secretos**: por variables de entorno (`Jwt__SecretKey`, `ConnectionStrings__HermesDb`, …), nunca en appsettings.
  `deploy/.env.example` lista todo.
- **Migraciones al arrancar** (`Base:MigrarAlArrancar`): con `ConnectionStrings:HermesDbMigraciones` (el DUEÑO de las tablas) si
  existe; `HermesDbContext.Configurar` es el único lugar con la configuración de Npgsql.
- **Row Level Security** (segunda barrera multi-tienda, `Persistence/SeguridadPorFila.cs` + migración `AgregarSeguridadPorFila`):
  política `por_tienda` en las 16 tablas de una tienda con `hermes.tienda_permitida(tienda_id)`, que lee la variable de sesión
  `hermes.tienda` (`*` todas, vacía ninguna, o el Id). Tres interceptores de EF la ponen al abrir cada conexión, antes de cada
  comando si cambió (la tienda pública la fija a mitad del pedido) y la olvidan si una transacción se deshace. Se enciende con
  `Base:SeguridadPorFila` (en producción; en desarrollo la app entra como `postgres`, superusuario, y RLS no le aplica). **No
  es FORCE**: el dueño (migraciones, respaldos) no queda limitado; la app entra con `hermes_app` (lo crea
  `deploy/postgres-init/01-rol-de-la-app.sh`). Lo que cruza tiendas a propósito va dentro de
  `using (_db.AccesoATodasLasTiendas())`: **AdminService** (pagos, tiendas, asignar) y **Limpieza**. **Toda tabla nueva de una
  tienda** tiene que sumarse a `AgregarSeguridadPorFila.Tablas` (o a una migración nueva): `SeguridadPorFilaTests` compara el
  modelo con `pg_policies` y falla si falta alguna.
- **Limpieza periódica** (`Mantenimiento/Limpieza.cs`, `LimpiezaPeriodica` solo en la API, cada `Limpieza:IntervaloMinutos`):
  refresh tokens vencidos y fotos sin usar (ni producto, ni logo, ni comprobante) de más de 24 h, por lotes. Las fechas se
  comparan por el **Id (Guid v7)** para que la consulta corra igual en SQLite: `Guid.CreateVersion7(fecha)` como corte.
- **Fotos en Cloudflare R2** (listo, sin activar): `Almacenamiento:Tipo` = `Base` (hoy) o `R2` (`Imagenes/AlmacenEnObjetos.cs`).
  Sube en el momento y **borra recién después del SaveChanges** del servicio (evento `SavedChanges`; si falla, no borra nada).
  `IAlmacenArchivos.ClaveDeUrl` reemplaza al viejo `Urls.ClaveDe` (la vista previa lo usa). Pasar las fotos: comando
  `copiar-fotos-a-r2` y después cambiar el tipo. `ClienteR2` NO se probó contra R2 real (no hay cuenta).
- **Sitio público**: `app.UseRouting()` explícito DESPUÉS de `UseStaticFiles` (**trampa ya pisada**: si no, la página `/{slug}` se
  quedaba con `/robots.txt`). `robots.txt` (se indexan las tiendas, no el carrito) y `/privacidad` (para los clientes de los pedidos).
  Slugs reservados nuevos: `salud`, `terminos`, `privacidad`, `manifest`, `favicon`, `robots`, `sitemap`.
- **Carga** (`scripts/carga.py`): con RLS, en Release, en esta PC: 16 dueñas + 4 clientes durante 60 s → 16.842 pedidos (280/s),
  **0 errores**, 5.680 ventas simultáneas y el stock **cierra exacto**; p95 < 170 ms en todo el panel. El sitio público da 429 a
  propósito pasados 240/min por IP.
- Probado: 9 pruebas nuevas (`LimpiezaTests`, `AlmacenEnObjetosTests`, `SeguridadPorFilaTests` con Postgres) — 347 en total —, las
  11 pruebas de punta a punta de la API y el e2e del panel con la app entrando como `hermes_app` y RLS encendida, y el script de
  inicio de Postgres simulado en una base nueva.

## Carrito y pedidos (2026-10-14, pedido del usuario)

Carrito en la tienda pública + sección **Pedidos** en el panel. `IPublicoService.RevisarCarritoAsync`/`PedirAsync` (los usa
`Hermes.Public`), `IPedidoService`/`PedidoService` + `PedidosController` (`/api/v1/pedidos`: `GET`, `GET resumen`, `GET {id}`,
`POST {id}/confirmar|cancelar|entregar`). Tablas `pedidos` y `pedido_lineas`, migración `AgregarPedidos`.

- **Decisión del usuario**: al confirmar el carrito el pedido **queda guardado en el panel** y además se abre WhatsApp con el
  detalle; se piden **nombre, celular y entrega** (retiro o envío con dirección y referencia), sin cuenta del cliente.
- **El carrito vive en el navegador** (`localStorage`, clave `carrito:{slug}`), no en la base. `publico.js` lo maneja y antes de
  mostrarlo pregunta cómo está hoy (`POST /{slug}/carrito/revisar`): lo que ya no está a la venta se saca avisando. Sin
  JavaScript no hay carrito: queda el pedido directo por WhatsApp de cada producto (como antes).
- **Rutas JSON del sitio público** (`Program.cs` de `Hermes.Public`): `POST /{slug}/carrito/revisar` y `POST /{slug}/pedido`. Solo
  JSON (un formulario de otro sitio no puede mandar `application/json` sin CORS, por eso no hay token antiforgery; un form
  común da 400). **Campo trampa** `sitio` (invisible; si viene lleno, 400). Límite propio `pedidos`: **30 cada 10 min por IP Y
  POR TIENDA** (solo por IP, los clientes de una tienda le gastarían el cupo a los de otra: CGNAT). La respuesta trae el enlace
  `wa.me` armado (`Formato.MensajeDeCarrito`). Las fotos del carrito pasan por `Urls.Foto` (como en las páginas).
- **Lo que nunca sale** sigue valiendo: los DTOs del carrito dicen `Unidades`, no `Cantidad` (la prueba prohíbe esa palabra en
  todo DTO público) y están en la lista explícita de la prueba de reflexión.
- **Pedir** (`PedirAsync`): la tienda tiene que recibir pedidos (`PedidosPorWhatsapp` + WhatsApp cargado; si no, 409
  `TiendaSinPedidosException`). Variantes ACTIVAS de productos visibles y no archivados (si no, 409 `ProductoNoPedibleException`);
  la misma variante repetida se junta; 1..30 líneas, 1..99 unidades. **Lo agotado se rechaza solo si la tienda muestra la
  disponibilidad** y nunca se compara con las unidades pedidas (eso dejaría adivinar el stock exacto): pedir 50 de algo que
  tiene 2 se acepta y lo resuelve la dueña. **No toca el stock.** Cada línea guarda foto de nombre y precio. Idempotencia por
  `ClaveIdempotencia` (la genera el navegador). **Número correlativo por tienda** (`Pedido #12`): candado de la fila de la
  tienda (UPDATE que no cambia nada) y recién después `MAX + 1`, dentro de una transacción. **Trampa ya pisada**: sin el
  candado, con reintentos, 8 pedidos simultáneos chocaban en el índice único (lo encontró la prueba con Postgres).
- **Estados**: `Nuevo → Confirmado → Entregado`, o `Cancelado` (con motivo opcional). Cada paso es un `UPDATE` condicional (409
  si ya estaba resuelto). **Entregar** registra la venta con `IVentaService` usando **el Id del pedido como clave de
  idempotencia** (reintentar no duplica ni descuenta dos veces) y recién después marca `Entregado`; si alguien lo canceló en el
  medio, anula la venta recién creada. Sin stock suficiente → 409 de ventas y el pedido no cambia. **La venta se cobra al
  precio ACTUAL** (las ventas no tienen precio editable): `PedidoLineaDto.PrecioActual` deja que el panel avise si cambió.
- El Inicio trae `PedidosPendientes` (Nuevos + Confirmados; un pedido también saca al Inicio de "primeros pasos").
- **No incluido**: pago en línea, costo de envío, zonas de entrega, cuenta del cliente / historial de sus pedidos, aviso a la
  dueña fuera de WhatsApp (email, notificación), reservar stock al pedir, editar un pedido.
- Probado: 19 pruebas (`PedidoTests`: aislamiento, agotado vs. disponibilidad oculta, idempotencia y 3 de concurrencia en
  Postgres: 8 pedidos → números 1..8, 6 reintentos con la misma clave → 1 pedido, 5 entregas → 1 venta) +
  `scripts/e2e_pedidos.py` (sitio público + API por HTTP) + el e2e del panel + `publico.mjs`.

## Planes y panel admin (M13, 2026-10-14)

`IPlanService`/`PlanService` + `PlanesController` (`GET /api/v1/planes`, `GET /api/v1/plan`, `POST /api/v1/plan/solicitar`,
`GET /api/v1/plan/pagos`) e `IAdminService`/`AdminService` + `AdminController` (`/api/v1/admin`: `GET pagos`,
`POST pagos/{id}/aprobar|rechazar`, `GET tiendas`, `PUT tiendas/{id}/plan`, `GET planes`, `PUT planes/{id}`). Tablas `planes`
(sembrada con `HasData`), `suscripciones`, `pagos_plan` y `usuarios.es_administrador`. Migración `AgregarPlanes`. Todo en
`Hermes.Infrastructure/Planes/`.

- **Planes** (precios PROVISORIOS, se editan desde el panel): Gratis 0 (15 productos, 100 MB), Emprendedor 39 (150, 1000 MB),
  Negocio 89 (sin límite de productos, 5000 MB). `null` en un límite = sin límite. Ids fijos en `PlanConfiguration`.
- **`PlanesDeTienda` es lo único que decide qué plan rige**: la suscripción vigente (`Desde <= ahora < Hasta`, `Hasta` null = no
  vence) de comienzo más reciente; sin ninguna, `Planes:PlanSinSuscripcion` (si el código no existe, "gratis"). **En
  Development es "negocio"** (las cuentas de prueba tienen decenas de productos); las pruebas de límites asignan el Gratis.
  Recibe el id de tienda explícito e ignora el filtro global (el admin mira tiendas ajenas). `Suscripcion` y `PagoPlan` NO
  son `IPerteneceATienda` por eso: los servicios de la dueña filtran a mano con la tienda de la sesión (leída a una variable).
- **Límites** (`ILimitesDePlan` → 402 `LimiteDePlanException`): crear y desarchivar productos (`ProductoService`, cuentan los no
  archivados) y subir fotos (`ImagenService`, suma de `BytesGuardados`). **Al vencer no se borra ni oculta nada.** El
  comprobante (`POST /imagenes?comprobante=true`) **no cuenta** en el espacio. Los dos servicios reciben `ILimitesDePlan`
  opcional (null en pruebas viejas = sin límites).
- **Pedir un plan**: plan activo y pago, meses de `Planes:MesesPermitidos` (1/3/6/12), método `Qr`/`Transferencia`, comprobante
  = imagen de la tienda; monto = precio × meses fijado al pedir. **Un solo pendiente por tienda**: índice único parcial
  `ux_pago_plan_pendiente` (la carrera da `DbUpdateException` → 409). Verificado con 5 pedidos simultáneos en Postgres.
- **Aprobar/rechazar**: `UPDATE ... WHERE Estado = Pendiente` en una transacción (5 aprobaciones simultáneas → una, y una sola
  suscripción). Rechazar exige motivo (≤ 300). `ActivarAsync`: mismo plan vigente con vencimiento → **se alarga la misma fila**
  (si se creara una que empieza en el futuro, "vigente hasta" mostraría la fecha vieja: lo encontró una prueba); mismo plan
  sin vencimiento → no cambia; otro plan → la vigente se cierra ahora y la nueva empieza ahora (sin prorrateo). Quedan
  `ResueltoPorId` / `AsignadaPorId`.
- **Rol de administrador**: `Usuario.EsAdministrador` → claim `rol=admin` en el JWT (solo si lo es) → política `Admin`
  (`Politicas.cs`). **Solo se da por comando**: `dotnet run --project src/Hermes.Api -- promover-admin <email>` (o
  `quitar-admin`); hay que volver a iniciar sesión. `UsuarioSesionDto.EsAdmin` le dice al front si mostrar el panel.
- **Trampa ya pisada**: un `int[]` de opciones con valor inicial se DUPLICA al leer la configuración (el binder agrega, no
  reemplaza): salía `[1,3,6,12,1,3,6,12]`. `MesesPermitidos` arranca vacío y `Meses` pone el valor por defecto.
- `Planes:InstruccionesDePago` es el texto de "cómo pagar": **poner los datos reales de cobro antes de producción**.
- **No incluido**: cobro automático, prorrateo, avisos de vencimiento por email/WhatsApp, descuentos por varios meses.
- Probado: 27 pruebas (`PlanesTests`, aislamiento, rol en el token y 2 de concurrencia en Postgres) + `scripts/e2e_planes.py`
  (API por HTTP: 401/403 del panel, 402, comprobante multipart, 5 aprobaciones simultáneas; crea su administrador y guarda las
  credenciales en `scripts/admin.local.json`, ignorado por git) + el e2e del front.

## Tablero del Inicio (2026-10-19, pedido del usuario con un mockup)

`GET /api/v1/inicio` (se amplió) + `GET /api/v1/ventas/por-dia` y `GET /api/v1/ventas/mas-vendidos`. Sin tablas ni migraciones nuevas.

- **Período** Hoy / Semana / Mes lo manda el front (`util/periodo.ts`). La comparación "+X % vs. {anterior}" usa el período anterior
  de **igual duración transcurrida** (el mes en curso contra el mismo tramo del mes pasado), solo si el anterior tuvo ganancia.
- **`por-dia`**: devuelve **todos** los días del rango (también los de 0), agrupados por fecha LOCAL: el front manda
  `DesfaseMinutos = getTimezoneOffset()` (Bolivia 240). Valida 1..60 días y desfase ±840.
- **`mas-vendidos`**: agrupa líneas por producto (nombre actual); `Ganancia` solo suma líneas con costo cargado. `Limite` 1..20.
- **`InicioDto`** suma `PedidosPendientes`, `BorradoresListos`, `BorradoresSinPrecio` (la bandeja de Instagram: solo cuentan los
  que tienen o no precio) y `UnidadesDeLaTienda` (unidades de pedidos de la tienda web ya **entregados** en el período).
- "Ya cubriste el X %" se muestra tope **"100 %+"** en el front para no marear; el servidor sigue mandando el número real.
- Probado: 7 pruebas (`TableroTests`) — 374 en total — y el e2e del panel (bloque 8h reescrito; `E2E_BASE` permite otro puerto).

## Equipo, roles y permisos (2026-10-23, pedido del usuario)

Un negocio puede tener gente a cargo (vendedores) y el dueño ve cómo va desde otro lado. Mismo patrón que InventoryPlatform (CMP).
`RolesController` (`/api/v1/roles`), `EquipoController` (`/api/v1/equipo`), `POST /api/v1/auth/cambiar-password`, `GET ventas/por-vendedor`.
Migración `AgregarRolesYEquipo` (tablas `roles`, `rol_permisos`, `miembros`; `usuarios.debe_cambiar_password`, `ventas.usuario_id`,
`planes.max_usuarios`; las tres con RLS). **Pendiente aplicarla en Neon/VPS** junto con las otras dos migraciones sin subir.

- **Permisos** = códigos en `Hermes.Domain/Seguridad/Permisos.cs` (`ventas.registrar`, `costos.ver`…). Viajan como claims `permiso` en el JWT
  y se usan como nombre de policy (`[Authorize(Policy = Permisos.X)]`, varios con `a|b`): `PermisoPolicyProvider` las resuelve sin registrar
  una por una. Agregar uno: la constante + una línea en `Catalogo`. **Toda acción de un controller exige un permiso o figura en las listas
  de `PermisosEnLosEndpointsTests`** (la prueba recorre los controllers por reflexión) y todo permiso del catálogo tiene que usarse.
- **Dueño** = `Tienda.DuenoId`, no un rol guardado: tiene siempre todos los permisos del catálogo. **Roles** por tienda, editables desde el
  día 1; se crean al primer listado (Encargado, Vendedor, Solo ver). Nadie da ni administra más de lo que tiene; no se puede uno
  desactivar ni cambiarse el rol a sí mismo.
- **El dueño crea la cuenta** de cada persona (email + contraseña provisional); `Usuario.DebeCambiarPassword` obliga a elegir una propia antes de
  usar el panel (el front muestra `CambiarPassword` en vez del panel). Plan limita personas (cuenta al dueño): Gratis 1, Emprendedor 3,
  Negocio 10 (`ILimitesDePlan.ExigirPoderSumarPersonaAsync`, 402).
- **Los permisos se leen de la base en cada refresh** (`AuthService.ResolverAccesoAsync`); cambiar un rol, desactivar o restablecer la
  contraseña llama a `ISesionesRevocadas`: el token de acceso vigente se rechaza (claim `emi` en milisegundos) y el front renueva. Memoria
  del servidor: con varias instancias pasarlo a caché compartida.
- **Costos solo con `costos.ver`**: el controller los pone en 0/null con `Enmascarar.Costos(bool)` e `InicioDto.SegunPermisos`; una prueba
  exige que todo DTO con `costo|ganancia|margen` figure en una lista conocida. Sin `ventas.ver` (ni `inicio.ver`) alguien ve **solo las
  ventas que registró** (`Venta.UsuarioId`).
- **Front**: `sesion.acceso` (`esDueno`, `rol`, `permisos`), `usePermisos().tiene('a|b')`, `Seccion.permiso` en el menú, `RequierePermiso`
  en las rutas y a dónde entrar (`rutaDeEntrada`: un vendedor sin Inicio abre Nueva venta). La API sigue siendo la que manda.
- Probado: `RolesTests`, `PermisosEnLosEndpointsTests` (407 pruebas en total), 45 verificaciones contra la API real y el panel en el
  navegador (vendedora: cambio de contraseña, menú reducido, sin costos; dueño: Equipo y Roles).
- **No incluido**: invitación por email, cambiar de negocio con una misma cuenta, auditoría de quién cambió qué, permisos por producto.

## Módulo de referencia: Categoría

`Categoria` → `CategoriaConfiguration` → `CategoriaService` → `CategoriasController` →
`CategoriaTests`. Es el patrón a copiar para cada módulo nuevo.

## Comandos

```bash
dotnet build Hermes.slnx
dotnet test Hermes.slnx
# Con la prueba de concurrencia en Postgres real (crea y borra su propia base):
#   $env:HERMES_TEST_PG = "Host=localhost;Port=5432;Username=postgres;Password=<la de user-secrets>"
#   dotnet test Hermes.slnx --filter "FullyQualifiedName~simultaneas"
# OJO: `dotnet test` NO recompila Hermes.Api (las pruebas no lo referencian). Antes de `dotnet run --no-build`
# o de `pnpm api:tipos`: `dotnet build src/Hermes.Api`, o el esquema del front sale viejo.
dotnet run --project src/Hermes.Api --launch-profile http    # http://localhost:5013/scalar/v1
dotnet run --project src/Hermes.Public --launch-profile http # la tienda pública: http://localhost:5014/{slug}
dotnet ef migrations add <Nombre> --project src/Hermes.Infrastructure --startup-project src/Hermes.Api --output-dir Persistence/Migrations
dotnet ef database update --project src/Hermes.Infrastructure --startup-project src/Hermes.Api
```

`dotnet-ef` es herramienta local (`dotnet-tools.json`): `dotnet tool restore` la instala.

En desarrollo la API va por **HTTP** (sin `UseHttpsRedirection`): el front la usa a través del
proxy de Vite y así no hace falta confiar en el certificado de desarrollo. En producción HTTPS
es obligatorio.

Herramientas en esta PC: instaladas **por usuario, sin permisos de administrador** (.NET SDK en
`%LOCALAPPDATA%\Microsoft\dotnet`; Node, Git (MinGit) y pnpm en `%LOCALAPPDATA%\Programs`),
agregadas al PATH del usuario.

### Secretos (nunca en appsettings)

- `Jwt:SecretKey` (≥ 32 caracteres) → `dotnet user-secrets set "Jwt:SecretKey" "..." --project src/Hermes.Api`.
  Sin ella la API no arranca, a propósito.
- Contraseña de Postgres → `dotnet user-secrets set "ConnectionStrings:HermesDb" "Host=localhost;Port=5432;Database=hermes_dev;Username=postgres;Password=..." --project src/Hermes.Api`.

### Postgres local

Portable en `C:\Users\Sebas\postgres` (ver `docs/db-referencia/README.md`), no arranca solo con
Windows. La app usa la base **`hermes_dev`**; la base `hermes` tiene el esquema viejo de los
scripts SQL y comparte nombres de tabla, así que no apuntes la app ahí.

## Plan de trabajo

**`docs/PLAN.md`**: módulos M1–M14 en orden, qué incluye cada uno, cuándo está terminado y las
decisiones pendientes. Se trabaja un módulo a la vez, de punta a punta; al cerrar uno, marcarlo ahí.

## Estado

- 2026-10-14: esqueleto creado. Capas, filtro multi-tienda, manejo de errores, JWT (solo
  validación; falta emitir tokens), Scalar, CORS para `http://localhost:5173`, módulo Categoría
  y migración `Inicial` (usuarios, tiendas, categorias).
- 2026-10-14: autenticación completa (registro, login, refresh rotativo, logout, yo) + migración
  `AgregarAutenticacion`. Ambas migraciones **aplicadas** en `hermes_dev` (Postgres local); la
  cadena de conexión con contraseña está en user-secrets.
- 2026-10-14: M1 (sesión y armazón del front) hecho.
- 2026-10-14: **M2 hecho** — imágenes (SkiaSharp, 3 tamaños WEBP, almacén en Postgres) + migración
  `AgregarImagenes`. 39 pruebas unitarias.
- 2026-10-14: **M3 hecho** — Mi tienda (datos, color, WhatsApp, logo, dirección) + migración
  `AgregarLogoYColorDeTienda`. 68 pruebas unitarias.
- 2026-10-14: **M4 hecho** — catálogo: productos con variantes (color × talla), fotos, categorías, búsqueda,
  filtros, paginación por cursor y archivado + migración `AgregarProductos`. 102 pruebas unitarias.
- 2026-10-14: **M5 hecho** — stock como suma de movimientos con candado por variante, control por producto
  (por encargo), Llegaron más, corregir, historial y resumen + migración `AgregarStock`. 139 pruebas unitarias.
- 2026-10-14: **M6 hecho** — ventas (venta rápida, foto de precio/costo, idempotencia, anulación atómica que devuelve
  el stock) + migración `AgregarVentas`. 166 pruebas unitarias (27 de ventas + concurrencia en Postgres).
- 2026-10-14: **M7 hecho** — proveedores y compras (en camino → recibida, pagada aparte, anulación que revierte el
  stock, comparador de precios, aviso de margen al recibir) + migración `AgregarCompras`. 200 pruebas unitarias.
- 2026-10-14: **M8 hecho** — costeo opcional por producto y precio sugerido calculado (nunca guardado ni aplicado
  solo), con el costo del producto siempre igual al total del costeo + migración `AgregarCosteo`. 216 pruebas unitarias.
- 2026-10-14: **M9 hecho** — gastos (anulables, idempotentes) y resumen del Inicio (ventas, ganancia, gastos, "ya
  cubriste el X %", para reponer, lo último que pasó) + migración `AgregarGastos`. 235 pruebas unitarias.
- 2026-10-14: **M10 hecho** — tienda pública (Razor Pages en `Hermes.Public` con vista previa para WhatsApp, JSON
  `/public/{slug}`, "nunca sale costo/margen/stock", qué ve el cliente configurable, pedido por WhatsApp, imagen JPEG de
  vista previa) + migración `AgregarTiendaPublica`. 255 pruebas unitarias.
- 2026-10-14: **M11 hecho** — lector de publicaciones de Instagram por reglas (nombre, precio, ofertas, colores, tallas,
  stock, categoría por hashtag, carruseles), bandeja de revisión y publicación atómica + migración `AgregarBorradores`.
  292 pruebas unitarias. Siguiente: M12 Conexión con Instagram (Meta): **verificar antes los requisitos vigentes de Meta**.
- 2026-10-14: **M13 hecho** (M12 salteado por ahora: necesita la cuenta de Meta) — planes Gratis/Emprendedor/Negocio con
  límites (402), pedido con comprobante, panel de administración (pagos, tiendas, planes) y rol de admin por comando +
  migración `AgregarPlanes`. 319 pruebas unitarias. Siguiente: M12 (Meta) o M14 (producción).
- 2026-10-14: **Carrito y pedidos hecho** (pedido del usuario, fuera del plan original) — carrito en la tienda pública,
  pedido guardado + aviso por WhatsApp, sección Pedidos en el panel (confirmar, entregar = venta, cancelar) + migración
  `AgregarPedidos`. 338 pruebas unitarias.
- 2026-10-14: **M14 hecho** — producción: Docker + Caddy (HTTPS) + respaldos, Serilog/Sentry, proxy, `/salud`, Row Level
  Security con rol sin privilegios, limpieza periódica, fotos a R2 (listo sin activar), PWA, términos y privacidad, prueba de
  carga + migración `AgregarSeguridadPorFila`. 347 pruebas unitarias. Guía: `docs/DESPLIEGUE.md`.
- 2026-10-15: **M12 hecho** (salvo conectar una app de Meta real) — conexión con Instagram: OAuth con state firmado, token
  cifrado y renovado solo, sincronización cada 30 min a la bandeja con fotos, reconectar/desconectar + migración
  `AgregarConexionInstagram`. 367 pruebas unitarias. **Todos los módulos del plan (M1–M14) están hechos.**
- 2026-10-19: **Tablero del Inicio** (ventas por día, más vendidos, comparación con el período anterior).
- 2026-10-22: **Pedidos: confirmar aparta el stock** (movimientos `Reserva`/`LiberaReserva`; migración `ReservaDeStockEnPedidos`) y
  **correo del proveedor** (migración `AgregarCorreoProveedor`).
- 2026-10-23: **Equipo, roles y permisos** (migración `AgregarRolesYEquipo`, 407 pruebas): el dueño crea cuentas con contraseña
  provisional, roles editables, costos solo con `costos.ver`, un vendedor ve solo sus ventas, límite de personas por plan. Panel con
  menú, rutas y pantallas por permiso, pantallas Equipo y Roles, cambio de contraseña obligatorio y **barra móvil con botón Menú**.
- 2026-10-23: **"Desde Instagram" oculto** en el panel (`INSTAGRAM_LISTO = false` en `src/diseno/secciones.ts`: la pantalla dice que se
  está construyendo) hasta que haya una app de Meta aprobada. La bienvenida ya no lo promete.
- 2026-10-23: **Producción actualizada**: las 3 migraciones nuevas aplicadas en Neon São Paulo (producción) y en Virginia (QA), y la pila
  reconstruida en el VPS de Hostinger (el servidor no usa git: se sube `git archive` de `main` y se corre
  `docker compose -f docker-compose.yml -f docker-compose.neon.yml up -d --build`, conservando el `.env`). El panel recibe la
  dirección pública de las tiendas al compilar (`TIENDA_URL` → `VITE_TIENDA_URL`, derivado de `DOMINIO_TIENDA`): sin eso el enlace y
  el QR de "Mi tienda" salían con `localhost:5014`.
- 2026-10-23: **Ambiente de demostración en el servidor** (para mostrar el sistema en vivo): segunda copia del sistema en el VPS, con su propia
  base (rama `demo` de Neon São Paulo), clave de sesiones y direcciones demo.200-141-21-142.sslip.io / 	ienda-demo.200-141-21-142.sslip.io`. Ver
  `docs/DESPLIEGUE.md` → *Ambiente de demostración*.
- 2026-10-23: **Tienda de demostración "Urbano Wear"** (ropa, 20 productos, 6 meses de movimiento: 1.131 ventas, 103 pedidos, 36
  compras, 99 gastos, equipo con roles) en `scripts/demo-ropa/` (SQL puro; fotos dibujadas con GDI+, SVG dentro de la base o fotos
  reales de Unsplash con `-v reales=`). Está en la base local y en QA (con dibujos SVG). Cuentas en `scripts/demo-ropa.local.json`.
- `scripts/crear_demo.py` crea una tienda de DEMOSTRACIÓN en la base de desarrollo (3 productos con foto y stock) con el
  WhatsApp del dueño del proyecto; las credenciales quedan en `scripts/demo.local.json` (ignorado por git).

## Pendiente (al 2026-10-23)

**Para salir con el dominio propio**
- **`micasero.tech`** (gratis el primer año en Hostinger; renueva a ~US$ 64): falta crear en el DNS dos registros A hacia el VPS
  (`@` y `app` → `200.141.21.142`). Después: `DOMINIO_PANEL=app.micasero.tech` y `DOMINIO_TIENDA=micasero.tech` en el `.env` del
  servidor, reiniciar (Caddy saca el HTTPS) y revisar `/salud`, el login y una tienda. Dejar un tiempo el dominio sslip.io. Apagar la
  renovación automática y recordar el cambio a un `.com` unos 10 meses después.
- Cambiar la contraseña de `hermes_app` en Neon (se compartió por chat) y rotar los secretos que hayan pasado por ahí.
- Revisar la marca **Mi Casero** en SENAPI (el nombre ya está en la interfaz desde el 2026-10-24).

**Datos de la tienda real (`menaje-7eb0`)**
- Le falta el **WhatsApp**: sin él la tienda pública no muestra carrito ni botón de pedir. (La tienda de demostración ya tiene el 76815206.)
- Los 47 productos no tienen fotos (se cargaron de una planilla): subirlas, o mostrar un ícono neutro en lugar del cuadro blanco.

**Producto**
- **Tienda pública, primera tanda de mejoras** (analizada, no hecha): portada con patrón del color de la marca, chips con el color de
  la tienda, precio destacado, botón "+" en la tarjeta (directo si hay una sola variante), barra flotante "Ver pedido", "Cargar más
  productos" con los enlaces de página como respaldo. Segunda tanda: portada con foto propia y tipografía.
- **Instagram**: conectar una app de Meta real (Advanced Access) y poner `INSTAGRAM_LISTO = true`.
- Verificación de email y recuperar contraseña; invitar al equipo por email; auditoría de quién cambió qué; redirección de
  direcciones viejas de tienda; búsqueda sin tildes (`unaccent`); idempotencia de los POST de stock; cobro automático y avisos de
  vencimiento de planes; "Mi plan" sigue oculto (`MOSTRAR_MI_PLAN = false`).
- Sesiones revocadas y bloqueo de login viven en memoria: con más de una instancia de la API, pasarlos a una caché compartida.

**Pruebas y despliegue**
- Los e2e del panel (`Hermes.Web/scripts/*.mjs`) no se actualizaron para los permisos, la barra móvil ni el Instagram oculto: pueden
  fallar. La API sí tiene su e2e de equipo (45 verificaciones) y 407 pruebas unitarias.
- **QA (Render)**: las ramas `qa` de los dos repos están 2 commits atrás de `main`; Render pide elegir el espacio de trabajo y no se
  pudo revisar. La base de QA (Virginia) ya tiene las migraciones y la tienda de demostración. Para mostrar en vivo se usa el ambiente de
  demostración del servidor (arriba), no Render.
- La demo de ropa en QA usa dibujos SVG: las fotos reales (7,6 MB) necesitan Cloudflare R2 o el comando `copiar-fotos-a-r2`. Dos fotos
  reales de poleras traen texto de otra marca.
- Respaldos: Neon guarda 6 horas de historia; falta un respaldo programado fuera del servidor.
- Repos: la rama `dev` está al día con `main`; el historial se limpió de menciones a Claude y falta que GitHub deje de mostrar al
  colaborador "claude" (caché de la página). El repo `catalogo` sigue pendiente de borrar (lo decide el usuario).
