# Decisiones tomadas sin consultar

Registro de lo que se decidió por criterio propio mientras se construía (sin que el usuario lo pidiera de forma
explícita). Cubre M0 a M11 y M13 (las de M6–M13 van primero; las de M0–M5 están en "Módulos anteriores"). Cada módulo nuevo
agrega acá las suyas. Cada una dice **por qué** y **cómo se cambia**. Las marcadas con ⚠️ son las que más conviene revisar
porque cambian lo que ve o paga la dueña, o son difíciles de revertir.

Lo que el usuario SÍ decidió (no está acá): repos separados, .NET + Scalar + React, pnpm y nunca npm, sin nombre en la
UI, fotos en la base por ahora, un solo usuario por ahora, variantes color+talla opcionales, control de stock por
producto (encendido por defecto) y "venta rápida" con cliente opcional.

---

## Reglas de negocio que cambian números o comportamiento

| # | Decisión | Módulo | Por qué | Cómo cambiarla |
|---|---|---|---|---|
| 1 ⚠️ | **Precio sugerido = costo ÷ (1 − margen), redondeado hacia ARRIBA al boliviano entero.** El margen es sobre el precio (no markup). Margen por defecto 40 %, permitido 1–90 %. | M8 | No encontré el prototipo con la fórmula. El margen sobre precio coincide con cómo ya muestra el catálogo "Ganás Bs X". Redondear hacia arriba evita que el redondeo se coma el margen. | `ReglaCosteo` en `Hermes.Domain/Entities/Costeo.cs` (un solo lugar). |
| 2 ⚠️ | **El costo del producto con costeo es siempre el total de los componentes**; si hay costeo, editar el costo directo se ignora, y una compra recibida o "Llegaron más" con costo actualiza el componente **Material**. | M8 | Ventas, stock y catálogo ya leen `Producto.Costo`; así no se desincronizan. | `CostoDelProducto.FijarPagadoAsync` + `ProductoService.ActualizarAsync`. |
| 3 ⚠️ | **Anular una compra recibida NO devuelve el costo del producto al anterior.** | M7 | No hay forma de saber cuál era el "bueno" (pudo cambiar a mano en el medio). El diálogo lo avisa. | Habría que guardar el costo anterior en la compra al recibir. |
| 4 ⚠️ | **"Ya cubriste el X % de los gastos" = ganancia ÷ gastos, redondeado hacia abajo**, mínimo 0 (ganancia negativa → 0), puede pasar de 100. La ganancia es la bruta y solo cuenta ventas con costo conocido. | M9 | Es lo más honesto con los datos que hay; no promete de más. | `InicioService.ResumenAsync`. |
| 5 | **Ventas y gastos del Inicio dependen del mes de quien mira; lo que se debe, lo que está en camino y lo que hay que reponer NO dependen del período.** | M9 | Son lo que hay que atender hoy, sin importar el mes. | `InicioService`. |
| 6 | **Si el costo no está cargado (0), no se inventa la ganancia**: la venta devuelve `null` y el resumen avisa cuántas unidades quedaron sin costo. | M6 | Contarlas como ganancia total mentiría. | `VentaService.Totales`. |
| 7 | **Una compra se anula, no se borra; si ya llegó, resta el stock que sumó** (movimiento `Reversion`). Si esas unidades ya se vendieron → 409 y no se anula nada. | M7 | Coherente con "nunca se borra"; el libro de stock no se edita. | `CompraService.AnularAsync`. |
| 8 | **Una venta, una compra y un gasto se anulan; nunca se borran.** | M6/M7/M9 | Mismo criterio del proyecto (desactivar, no borrar). | — |
| 9 | **Compra: "recibida" y "pagada" son dos ejes independientes**; se puede pagar antes o después de recibir. Sin pagos parciales. | M7 | Lo común en emprendimientos (anticipos). Parciales complican y no se pidieron. | `Compra.Recibida` / `Compra.Pagada`. |
| 10 | **Una compra no admite la misma variante dos veces** (400, "juntalos en una línea"). Las ventas sí las juntan solas. | M7 | Con costos distintos sería ambiguo. | `CompraService.Validar`. |
| 11 | **El costo de una compra es uno por producto en la pantalla** (vale para todos sus colores/talles), aunque el servidor guarda el costo por línea. | M7 | Más simple de usar; casi siempre cuesta igual. | `NuevaCompra.tsx`. |
| 12 | **Una compra puede registrarse "ya llegó" / "ya pagada"**; el front la registra en camino y después llama a `recibir`. | M7 | Así el servidor devuelve los avisos de margen. Si el segundo paso falla, la compra queda guardada y la pantalla lo dice. | `NuevaCompra.registrar`. |
| 13 | **Proveedor: nombre único por tienda sin distinguir mayúsculas; se desactiva, no se borra; uno desactivado no recibe pedidos nuevos.** | M7 | Evita duplicados y conserva el historial. | `ProveedorService`. |
| 14 | **El comparador usa el ÚLTIMO costo de cada proveedor por producto** (las compras anuladas no cuentan); en empate, ambos son "el más barato". | M7 | Es lo que sirve para decidir hoy. | `CompraService.CompararAsync`. |
| 15 | **Idempotencia (clave que genera el celular) en ventas, compras y gastos**, no en los movimientos de stock (llegadas/correcciones). | M6/M7/M9 | Un doble toque por mala señal no debe duplicar plata. Stock quedó pendiente. | `ClaveIdempotencia`. |
| 16 | **Gasto: fecha por defecto hoy; una pasada se acepta desde el 2000; una futura se rechaza (con un día de margen por zona horaria).** Monto > 0, tope 1.000.000. | M9 | Cargar gastos atrasados es normal; los futuros son errores. | `GastoService.RegistrarAsync`. |
| 17 | **Los gastos no se editan (se anula y se vuelve a anotar), sin categorías y sin recurrentes.** | M9 | Mantener M9 chico. | Ver "No incluido". |
| 18 | **El listado de gastos ordena por creación, no por fecha.** | M9 | Cursor simple por Id, igual que el resto. Un gasto cargado con fecha vieja sale primero si se cargó último. | Cursor compuesto por (Fecha, Id). |
| 19 | **Ventas: `Venta.Fecha` es la del servidor** (sin fecha manual), métodos de pago fijos: Efectivo, QR, Tarjeta, Transferencia. | M6 | Rapidez; son los métodos de Bolivia que se hablaron. | `MetodoPago`. |
| 20 | **Venta de un producto sin control de stock siempre se permite y no genera movimiento**; anular solo devuelve lo que se había descontado. | M6 | Coherente con "por encargo". | `VentaLinea.DescontoStock`. |
| 21 | **Venta: máximo 50 líneas y 100.000 unidades por línea; compra igual.** | M6/M7 | Tope contra errores de tipeo. | `Venta.MaximoLineas`. |

## Pantallas y textos (UX)

| # | Decisión | Módulo |
|---|---|---|
| 22 ⚠️ | **"Cliente (opcional)" escondido tras "+ Agregar cliente"** en la venta, para que no estorbe. | M6 |
| 23 | El método de pago se **recuerda** (localStorage) y arranca en el último usado. | M6 |
| 24 | El historial de ventas tiene chips **Hoy / Semana / Mes** (semana = hoy y los 6 días anteriores), calculados en la hora de quien mira. | M6 |
| 25 | La lista de "Nueva venta" pone **lo vendible primero y los agotados al final, sin contador**; dentro de cada grupo, lo último cargado primero. | M6 |
| 26 | "Registrar otra venta" vuelve a pedir los productos (el stock cambió) y la pantalla de éxito ofrece 3 salidas. | M6 |
| 27 | Pantallas nuevas con pestañas **Stock / Compras / Proveedores**; "Ventas" y "Gastos" entran al **menú lateral** pero no a la barra del celular (ahí está el botón central de venta). | M6/M7/M9 |
| 28 | Tarjeta "Mis ventas" en el Inicio (después reemplazada por el Inicio real). | M6 |
| 29 | **"Pedirle por WhatsApp"** abre `wa.me` con un mensaje ya escrito ("Hola X, ¿cómo estás? Quería hacerte un pedido."). | M7 |
| 30 | Aviso de margen al recibir: "Revisá tus precios" con enlace a `/catalogo?filtro=revisar`. | M7 |
| 31 ⚠️ | **La calculadora de precio está solo en Editar producto** (no en el producto nuevo) y debajo del formulario, no dentro. | M8 |
| 32 | "Usar este precio" lo escribe en el formulario y hay que Guardar; **remonta el formulario** (lo escrito sin guardar se pierde). | M8 |
| 33 | **Una tienda sin actividad ve los primeros pasos en vez de ceros** en el Inicio. | M9 |
| 34 | El Inicio muestra 5 productos para reponer y las 6 últimas actividades; accesos "Nueva venta" y "Anotar un gasto". | M9 |
| 35 | Sugerencias de gasto: Alquiler, Luz, Agua, Internet, Publicidad, Transporte. | M9 |
| 36 | El medidor de gastos va en la pantalla "¡Venta registrada!". | M9 |
| 37 | Tono y textos en español cercano ("Les debés a tus proveedores", "Cobrá Bs 167", "Ya llegó", "Ya la pagué"). | todos |

## Técnicas (no cambian lo que ve la dueña)

| # | Decisión |
|---|---|
| 38 | **Ventas, compras y gastos guardan fecha como `DateTime` UTC** (no `DateTimeOffset`): SQLite no compara `DateTimeOffset` en SQL y los listados filtran por rango. |
| 39 | **Los totales se suman en memoria**, no en SQL (SQLite no suma decimales). Son pocas filas por período. |
| 40 | **Cada línea de venta y de compra guarda una foto** de nombre, precio y costo del momento; editar el producto después no reescribe lo ya hecho. |
| 41 | **Transiciones atómicas con `UPDATE ... WHERE` condicional** (recibir, pagar, anular): con pedidos simultáneos solo uno gana. Verificado contra Postgres con peticiones simultáneas reales. |
| 42 | **Nuevo `TipoMovimiento.Reversion`** (resta por compra anulada) en vez de reutilizar `Ajuste`. |
| 43 | **`LineaMovimiento` lleva un costo opcional por línea**; el costo del producto lo actualiza `CompraService`/`CostoDelProducto`, no `OperacionesStock`. |
| 44 | **`CompraLinea.Orden`** (las líneas no se ordenan por Id: los Guid v7 del mismo milisegundo no tienen orden). |
| 45 | **El Inicio no tiene datos propios**: compone los servicios existentes, pidiendo de a uno (comparten el contexto). |
| 46 | Reuso de `GET /ventas/productos` para elegir productos en Nueva compra (en vez de un endpoint propio). |
| 47 | Migraciones nuevas solo agregan tablas (revisé que no haya `AddColumn` con valores por defecto peligrosos). |
| 48 | **Las claves de remontaje de formularios dependen de lo que llega del servidor**, no de contadores (bug real encontrado en M8). |
| 49 | Se hicieron correcciones de pruebas e2e viejas que eran frágiles (esperas fijas, lectura antes de cargar), sin cambiar lo que verifican. |
| 50 | Detuve y reinicié el proceso de la API en tu PC varias veces para compilar (solo el proceso de la API). |
| 51 | Creé cuentas de prueba y datos de prueba en `hermes_dev` con cada corrida de los scripts e2e (se acumulan; es la base de desarrollo). |
| 52 | Las capturas de pantalla de `docs/capturas/` se regeneran en cada e2e y se commitean. |

## Carrito y pedidos

Tus decisiones: el pedido queda guardado en el panel **y** se abre WhatsApp; se piden nombre, celular y entrega. El resto lo decidí yo:

| # | Decisión | Por qué / cómo cambiarla |
|---|---|---|
| 143 | **El carrito vive en el navegador del cliente** (no en la base) y se revisa contra el servidor al abrirlo. | Sin cuenta de cliente no hay a quién atarlo; así no se guarda basura de carritos abandonados. |
| 144 ⚠️ | **Pedir NO reserva ni descuenta stock**: recién "Entregar y cobrar" registra la venta. | Si reservara, cualquiera podría "vaciar" la tienda con pedidos falsos. La dueña confirma por WhatsApp antes. |
| 145 ⚠️ | **Solo se rechaza lo AGOTADO** (y solo si la tienda muestra disponibilidad). Pedir 50 de algo que tiene 2 se acepta. | Rechazar "no alcanza" dejaría adivinar el stock exacto probando cantidades. La dueña lo resuelve al confirmar. |
| 146 ⚠️ | **La venta se registra al precio de HOY** (no al del pedido). Si cambió, el detalle del pedido lo avisa. | Las ventas no tienen precio editable (decisión de M6). Si querés respetar el precio del pedido, hay que habilitar precio por línea en ventas. |
| 147 | **El carrito usa el interruptor "Pedidos por WhatsApp"** de Mi tienda: apagado, no hay carrito ni botón. | No agregué otro interruptor. |
| 148 | **Se mantiene "Pedir solo este por WhatsApp"** en cada producto, como alternativa al carrito (y lo único que hay sin JavaScript). | Hay clientes que prefieren escribir directo. |
| 149 | **Hasta 30 productos distintos y 99 unidades de cada uno** por pedido. | Topes contra abusos; se cambian en `Pedido`. |
| 150 | **Anti-spam**: campo trampa invisible + **30 pedidos cada 10 min por IP y por tienda**. Sin captcha. | Un captcha espanta clientes. Por tienda porque en Bolivia muchos celulares comparten IP. |
| 151 | **Número de pedido correlativo por tienda** (#1, #2…), para que la dueña y el cliente lo nombren igual. | |
| 152 | **Estados**: Nuevo → Confirmado (opcional) → Entregado, o Cancelado con motivo opcional. Un pedido nunca se borra. | Mismo criterio de todo el proyecto. |
| 153 | **"Pedidos" va en el menú lateral con un número** de pendientes, y en el Inicio ("Para atender"). No está en la barra inferior del celular. | La barra ya tiene sus 4 lugares; desde el Inicio se llega en un toque. |

## Planes y panel admin (M13)

| # | Decisión | Por qué / cómo cambiarla |
|---|---|---|
| 129 ⚠️ | **Precios provisorios**: Gratis Bs 0 (15 productos, 100 MB de fotos), Emprendedor **Bs 39**/mes (150 productos, 1000 MB), Negocio **Bs 89**/mes (sin límite de productos, 5000 MB). | No verifiqué la competencia (lo pedía el plan). Se cambian desde Administración → Planes, sin tocar código; el cambio rige al instante para todas las tiendas. |
| 130 | **Se cobra por mes, con 1, 3, 6 o 12 meses**, sin descuento por pagar más meses. | Lo más simple. Los meses salen de la configuración (`Planes:MesesPermitidos`). |
| 131 | **Cobro manual**: la dueña paga por QR o transferencia, sube la foto del comprobante y un administrador lo aprueba o rechaza (con motivo que ella ve). | Lo pedía el plan. El texto de "cómo pagar" es configurable (`Planes:InstruccionesDePago`): **hay que poner los datos reales de la cuenta/QR**. |
| 132 | **Un solo pedido esperando por tienda** (409 si pide otro). | Evita dos comprobantes del mismo pago. Índice único parcial en la base. |
| 133 | **El monto queda fijado al pedir**: si después cambia el precio del plan, ese pedido no cambia. | Es lo que ella vio y pagó. |
| 134 | **Pagar el mismo plan antes de que venza alarga la fecha** desde donde termina (no pierde días). Cambiar a otro plan cierra el actual **hoy, sin devolver la diferencia** (sin prorrateo). | Prorratear es complicado de explicar; si hace falta se compensa a mano con una asignación. |
| 135 ⚠️ | **Al vencer, nada se borra ni se oculta**: vuelve al Gratis, conserva todos sus productos y fotos, pero no puede crear productos nuevos (ni desarchivar) por encima de 15 ni subir fotos por encima de 100 MB. | Borrar o esconder productos de una clienta que se atrasó un día sería muy agresivo. La tienda pública sigue mostrando todo. |
| 136 | **El límite responde 402** con un mensaje que manda a "Mi plan" (el front agrega el enlace solo). | |
| 137 | **El comprobante no cuenta en el espacio de fotos.** | Si no, una tienda llena no podría subir el comprobante para pagar más espacio. |
| 138 | **El rol de administrador solo se da por comando en el servidor** (`dotnet run --project src/Hermes.Api -- promover-admin <email>`), nunca desde la web. | Sin verificación de email, dar admin por email sería una puerta abierta. El token lleva el rol; hay que volver a entrar después de promover. |
| 139 | **El administrador puede asignar cualquier plan, con o sin vencimiento** (cortesías, pruebas, compensaciones). Queda registrado quién lo hizo. | |
| 140 | **El plan Gratis siempre es gratis y está activo**; un plan pago no puede quedar en Bs 0. Desactivar un plan solo deja de ofrecerlo: quien lo tiene lo conserva. | Evita dejar a todos sin plan base. |
| 141 | **En desarrollo, una tienda sin plan tiene el Negocio** (`Planes:PlanSinSuscripcion = negocio` en `appsettings.Development.json`); en producción, el Gratis. | Las cuentas de prueba crean decenas de productos. Las pruebas de límites asignan el Gratis explícitamente. |
| 142 | **"Mi plan" es una sección más del menú** (no está en la barra inferior del celular) y "Administración" solo aparece para administradores. | La barra inferior ya tiene sus 4 lugares. |

## Lector de Instagram (M11)

| # | Decisión | Por qué / cómo cambiarla |
|---|---|---|
| 116 ⚠️ | **Nada llega al catálogo solo**: lo que se lee va a una bandeja de revisión y la dueña lo publica. | La propuesta central del producto era "se carga solo"; elegí revisión porque un lector por reglas se equivoca a veces y un precio mal en el catálogo es peor que un paso más. Se podría agregar "publicar directo si está completo". |
| 117 ⚠️ | **El lector nunca inventa**: lo que no encuentra queda vacío y avisa. Con varios precios sin marcar toma el primero y avisa. | Un dato inventado en una tienda es un error de plata. |
| 118 ⚠️ | **La categoría sale del primer hashtag que no sea genérico**; la lista de genéricos la armé yo (`#hechoamano`, `#bolivia`, ciudades, `#oferta`, `#nuevo`…) en `ParserPublicacion.HashtagsGenericos`. | Hay que ampliarla con textos reales de tus clientas. |
| 119 | **La categoría solo se asigna si coincide (sin tildes ni mayúsculas) con una que ya existe**; si no, queda "Sin categoría". No crea categorías solo. | Evita llenar el catálogo de categorías por hashtags sueltos. |
| 120 | **"1.200" se entiende como mil doscientos y "58,50" como cincuenta y ocho con cincuenta**; montos de más de Bs 1.000.000 se ignoran. | Formato boliviano de números. |
| 121 | **Un carrusel son 2 o más líneas numeradas que tengan precio**; una sola línea numerada no lo es. Lo suelto (colores, tallas, stock, categoría) vale para todos. | Lo que pide el plan. |
| 122 | **"Talla: única" no es una talla** (el producto queda como "Único"). | Coherente con cómo el catálogo trata "sin variantes". |
| 123 ⚠️ | **"Quedan N" solo se carga como stock si el producto lleva control y tiene UNA sola variante**; con varios colores/talles se avisa y se carga a mano. | No se sabe cuántas son de cada una. |
| 124 | **Topes**: texto de 5000 caracteres, 20 productos por publicación, 200 pendientes en la bandeja. | Contra abusos y errores. |
| 125 | **Pegar dos veces el mismo texto crea dos borradores** (sin idempotencia en `importar`). | Se descartan; es raro y no pierde plata. |
| 126 | **Las fotos no vienen en el texto**: se agregan al revisar. La conexión automática con Instagram (y sus fotos) es M12. | Sin Meta no hay fotos. |
| 127 | **Un borrador nunca se borra**: se publica o se descarta (queda registrado). | Mismo criterio del proyecto. |
| 128 | **Creé una tienda de demostración** (`scripts/crear_demo.py`) con tu WhatsApp **76815206** y 3 productos de ejemplo, porque la base de desarrollo no tenía ninguna cuenta real (las 98 eran de pruebas). Las credenciales las generé yo y están en `scripts/demo.local.json` (ignorado por git, no impreso). | Para que pruebes con datos parecidos a los tuyos. Cuando te registres con tu cuenta real, ahí cargás tu número en Mi tienda. |

## Tienda pública (M10)

| # | Decisión | Por qué / cómo cambiarla |
|---|---|---|
| 97 ⚠️ | **La tienda pública es un sitio aparte con Razor Pages (`Hermes.Public`, puerto 5014), no React.** | Era la opción recomendada del plan (la dejaste pendiente): WhatsApp no ejecuta JavaScript y una SPA no genera vista previa con foto. Implica desplegar un servicio más. |
| 98 ⚠️ | **Todo viene encendido y la tienda arranca PUBLICADA** (precios, disponibilidad, pedidos por WhatsApp). Las 84 tiendas existentes quedaron igual. | Para eso existe la tienda pública. Si preferís que arranque oculta hasta que la dueña la publique, cambia el valor por defecto de `Tienda.Activa` y de la migración. |
| 99 ⚠️ | **Un cambio de la dueña puede tardar hasta 30 segundos en verse** en la tienda pública (caché `max-age=30`), incluso ocultar precios o despublicar. | Evita ir a la base en cada visita. Se baja o se quita en `PublicoController` y en los `ResponseCache` de las páginas. |
| 100 ⚠️ | **Límite de 240 visitas por minuto por IP** en lo público. | Alto a propósito: en Bolivia muchos celulares comparten la IP de la operadora. |
| 101 ⚠️ | **Un solo 404 para todo**: tienda inexistente, apagada, producto oculto/archivado o de otra tienda. | No revelar qué existe. |
| 102 | **Lo que se oculta no se filtra por otro lado**: sin "mostrar disponibilidad" tampoco se ordena por agotados; sin "pedidos por WhatsApp" el número no sale en el JSON. | Ocultar de verdad. |
| 103 | **El público usa el filtro global de tienda** (la tienda se fija con el slug en un contexto por pedido) en vez de desactivarlo y filtrar a mano. | Un descuido no puede mezclar tiendas. |
| 104 | **Pedido por WhatsApp: un producto a la vez (variante + cantidad), sin carrito.** El mensaje: "Hola {tienda}! Quiero pedir: 3 × Cartera (negro) — Bs 540. Lo vi en {enlace}". | Lo mínimo que pide el plan. Un carrito es otro módulo. |
| 105 ⚠️ | **La imagen de la vista previa es un JPEG de 1200×630 con la foto ENTERA sobre el fondo** (no recortada); sin producto usa el logo o la primera foto; sin ninguna, el color de la marca con el nombre. | WhatsApp a veces no lee WEBP. **No se verificó con WhatsApp real**: hay que compartir un enlace y mirarlo. |
| 106 | **Fondos: Claro / Crema / Oscuro**; el texto sobre el color de la marca se elige solo (claro u oscuro) según su luminosidad. | "Personalizar diseño" del plan, en versión simple. |
| 107 | **Los interruptores del panel se guardan al tocarlos** (no hay botón Guardar en esa sección). | Son decisiones simples y reversibles. |
| 108 | **Slugs reservados ampliados**: `error`, `archivos`, `css`, `js`, `og`. | Que ninguna tienda tape una ruta del sitio público. |
| 109 | **La dirección se valida en código antes de ir a la base** (la restricción `regex` de la ruta no calzaba). | Los bots que piden `/wp-login.php` no cuestan una consulta. |
| 110 | **Letras con tilde legibles en el HTML** (no `M&#xED;a`), sigue escapando todo lo peligroso. | Vista previa y lectura del código. |
| 111 | **La tienda pública carga las tipografías de Google Fonts**, igual que el panel. | Consistencia; si no cargan, cae a la fuente del sistema. Es una dependencia externa. |
| 112 | **Las fotos las sigue sirviendo la API (`/archivos`)**; el sitio público arma la dirección con `Publico:ArchivosBaseUrl`. | Mientras estén en la base (tu pedido). |
| 113 | **La dirección base de la tienda pública en el panel** es `VITE_TIENDA_URL` (por defecto `http://localhost:5014`). | Hay que configurarla al salir a producción. |
| 114 | **No incluido en M10**: código QR, redirección de direcciones viejas, carrito, vista previa dentro del panel (hay un enlace "Ver mi tienda"), `robots.txt`/sitemap. | Para no inflar el módulo. Cambiar la dirección sigue rompiendo los enlaces ya compartidos. |
| 115 | **Si algo falla, se prefiere mostrar menos**: un cursor inventado muestra la primera página en vez de un error. | Experiencia del cliente. |

## Módulos anteriores (M0 a M5) y base del proyecto

Agregado después, repasando lo que se decidió en la primera parte. Mismas marcas ⚠️.

### Base y herramientas (M0)

| # | Decisión | Por qué |
|---|---|---|
| 53 | **Todo instalado por usuario, sin permisos de administrador** (.NET en `%LOCALAPPDATA%`, Node/Git/pnpm en `%LOCALAPPDATA%\Programs`), verificado por hash. Postgres **portable** en `C:\Users\Sebas\postgres`, base `hermes_dev`, puerto 5432. | `winget` se colgaba esperando el aviso de administrador y no estabas en la PC. |
| 54 | **Secretos en `user-secrets`** (clave JWT y cadena de Postgres), nunca en `appsettings`. La API no arranca sin clave JWT, a propósito. | Que no se suban al repo. |
| 55 | **En desarrollo la API va por HTTP** (puerto 5013) y el front la usa por proxy de Vite. | Evita confiar en el certificado de desarrollo. En producción HTTPS es obligatorio. |
| 56 | **Esquema `hermes`, nombres en snake_case, `Guid` v7 como Id, dinero `decimal(12,2)`, mensajes y código de negocio en español.** | Convenciones del proyecto hermano (CMP) y lectura directa en la base. |
| 57 | **Pruebas con SQLite en memoria** (no el proveedor InMemory de EF) y pruebas de concurrencia solo con `HERMES_TEST_PG` apuntando a Postgres real. | SQLite se parece más a una base real; los candados de fila solo existen en Postgres. |
| 58 | **Un e2e propio con Edge headless por DevTools Protocol** (sin dependencias) en vez de Playwright/Cypress. | Sin instalar paquetes pesados; Node trae WebSocket. |
| 59 | **Multi-tienda automático**: filtro global por tienda en el contexto y `TiendaId` asignado solo al insertar. Ningún servicio filtra a mano. | Una tienda nunca puede ver ni escribir en otra por un descuido. |

### Autenticación y sesión (M1)

| # | Decisión | Por qué |
|---|---|---|
| 60 ⚠️ | **Contraseña: 8 a 72 caracteres con al menos una letra y un número**, BCrypt costo 12. | 72 es el límite real de BCrypt. |
| 61 ⚠️ | **Bloqueo de login: 5 fallos por email en 10 minutos → 429**; además límite por IP (login y registro 20/min, refresh 120/min). No bajarlos: en Bolivia muchos celulares comparten la IP de la operadora. | Frena fuerza bruta sin castigar a usuarios reales. Se guarda en memoria (con varias instancias hay que pasarlo a caché compartida). |
| 62 | **Mismo mensaje para email inexistente y contraseña mala**, y se verifica contra un hash falso para que tarde igual. | No revelar qué cuentas existen. |
| 63 ⚠️ | **Token de acceso de 15 min en memoria; sesión larga con refresh token de 30 días en cookie HttpOnly** (`SameSite=Strict`, solo enviada a `/api/v1/auth`). El token de refresh se guarda **hasheado** y **rota en cada uso**. | Un script inyectado no puede leer ninguno de los dos. |
| 64 ⚠️ | **Reusar un refresh token ya rotado revoca todas las sesiones**, salvo que haya sido rotado hace menos de 30 s (dos pestañas renovando a la vez). | Detecta robo sin cerrar la sesión por una carrera normal. Costo aceptado: un ladrón dentro de esos 30 s no dispara la revocación. |
| 65 | **Si el refresh falla, no se borra la cookie.** | La pestaña que pierde la carrera borraría la cookie nueva de la otra. |
| 66 | **Registro crea el usuario y su tienda juntos.** Por ahora una persona opera su primera tienda (la más vieja). | Alcanza para un solo usuario. Cambiar de tienda será un endpoint nuevo. |
| 67 | **Slugs reservados** (`api`, `admin`, `public`, `login`…) y formato `a-z 0-9 -`, 2 a 40 caracteres. | Que ninguna tienda tape una ruta de la app. |
| 68 | El front renueva el token solo y reintenta una vez ante un 401; una sola renovación a la vez por pestaña. | Evita presentar un refresh ya rotado (se tomaría por robo). |

### Imágenes (M2)

| # | Decisión | Por qué |
|---|---|---|
| 69 | **SkiaSharp** (licencia MIT) para procesar fotos; tú preguntaste para qué era. | Redimensionar y convertir a WEBP sin depender de servicios externos. |
| 70 ⚠️ | **Se guardan 3 tamaños en WEBP** (lado largo 200 / 600 / 1200, calidad 70 / 78 / 82) y **la foto original NO se guarda**. Nunca se agranda. | Ahorra espacio (≈ 93 KB los tres para una foto de 12 MP). Si algún día se quiere el original, hay que cambiarlo ahora. |
| 71 | **Límites: 15 MB y 64 megapíxeles por foto, mínimo 50 px.** Formato por los primeros bytes (JPG/PNG/WEBP; GIF no), nunca por la extensión. | Anti "bomba de descompresión" y archivos disfrazados. |
| 72 | **Se corrige la orientación EXIF y se descartan todos los metadatos (GPS incluido).** Decodificación completa o nada (una foto cortada se rechaza). | Privacidad y que no se guarde una foto rota. |
| 73 | **Las fotos tienen una clave con dos Guid imposibles de adivinar**, caché inmutable de un año, y no son privadas por tienda. | Las fotos de producto son públicas; la protección es la clave. |
| 74 | Detrás de `IAlmacenArchivos` para cambiar a Cloudflare R2 después sin tocar servicios (pedido tuyo: "en la base por ahora"). | — |
| 75 | **Fotos huérfanas**: una subida que no se usa queda guardada (limpieza pendiente para M14). | Se prefirió no perder fotos a borrar de más. |

### Mi tienda (M3)

| # | Decisión | Por qué |
|---|---|---|
| 76 | **Color por defecto de una tienda nueva: mostaza `#F2A33A`** (el del prototipo); las anteriores siguen con su violeta. | Fidelidad al diseño. |
| 77 | **WhatsApp: celular de Bolivia (8 dígitos, empieza con 6 o 7), se guarda siempre como `+591XXXXXXXX`.** | El enlace `wa.me` usa esos dígitos. |
| 78 | **Al cambiar o quitar el logo se borra el anterior** (después de guardar el nuevo). | No acumular fotos de más. |
| 79 ⚠️ | **Cambiar la dirección (`/slug`) rompe los enlaces ya compartidos**: el front pide confirmación y no hay redirección todavía. | La redirección necesita la tienda pública (M10). |
| 80 | **Lo que quedó fuera de M3** porque depende de la tienda pública: enlace para compartir, QR, "Ver tienda", los interruptores de qué ven los clientes y "Personalizar diseño". | — |

### Catálogo (M4)

| # | Decisión | Por qué |
|---|---|---|
| 81 ⚠️ | **Variantes = colores × tallas, ambos opcionales** (opción A del plan). Sin ninguno, la variante es "Único". Máximo **60 variantes** y **8 fotos** por producto. | La opción por defecto cuando dijiste "continua". |
| 82 ⚠️ | **Margen: "Bajo" es ganar menos del 20 % del precio**; "Pierde" es ganar 0 o menos; "Sin costo" si el costo es 0. | Umbral razonable para emprendimientos; está en un solo lugar (`MargenProducto`). |
| 83 | **Un producto nunca se borra: se archiva** (y una variante se desactiva). Si se vuelve a poner un color, se reactiva la misma fila. | Puede tener ventas y compras. |
| 84 | **Precio > 0, costo ≥ 0, hasta 2 decimales, tope Bs 1.000.000** (se rechaza, no se redondea en silencio). | Que un cero de más no pase. |
| 85 | **Listado por cursor** (no `OFFSET`), 24 por página y máximo 60. | No repite ni saltea aunque se creen productos mientras se pagina. |
| 86 | **Búsqueda por nombre sin distinguir mayúsculas, pero sí tildes** (`unaccent` queda pendiente). | Más simple por ahora. |
| 87 | **Categorías: se desactivan, no se borran; nombre único por tienda.** Se pueden crear desde el formulario del producto. | Mismo criterio del proyecto. |
| 88 | Enums y números del JSON estrictos (enums como texto, un número como texto se rechaza). | Que el esquema OpenAPI y los tipos del front salgan bien. |

### Stock (M5)

| # | Decisión | Por qué |
|---|---|---|
| 89 ⚠️ | **El stock nunca se guarda: es la suma de movimientos**; los movimientos no se editan ni se borran (corregir es otro movimiento). | Un número cacheado se desincroniza. |
| 90 | **Estados: Agotado (≤ 0), Por agotarse (≤ mínimo) y Bien; mínimo por defecto 3** (como el prototipo). | — |
| 91 | **Candado por variante antes de leer el stock**, siempre ordenado por Id. Probado: sin candado pasaron 7 ventas de 5 unidades. | Evitar stock negativo con ventas simultáneas. |
| 92 | **"Corregir" pide cuánto HAY** (no la diferencia), con motivos sugeridos (Dañado, Perdido, Regalo, Conté y había otra cantidad). | Es lo que la gente sabe. |
| 93 | **No se puede sacar una variante que todavía tiene stock**: hay que corregirla a 0 antes. | Las unidades desaparecerían sin rastro. |
| 94 | **"Llegaron más" con costo actualiza el costo del producto.** | "El costo se actualiza cuando llega mercadería". |
| 95 | **Tope por movimiento 1.000.000 y por saldo 10.000.000.** | Contra errores de tipeo. |
| 96 | **La lista de Stock va de lo más urgente a lo menos** (agotados, por agotarse, bien, por encargo). | — |

## No incluido (a propósito, para no inflar cada módulo)

- **M6:** descuentos, precio editable, fecha manual en ventas.
- **M7:** proveedor dentro de "Llegaron más", proveedor por producto, fecha manual, pagos parciales, devolver el costo al anular.
- **M8:** costeo por variante, margen sobre costo (markup), calculadora en producto nuevo, sugerido dentro del aviso de margen de Compras.
- **M9:** editar gastos, categorías de gasto, gastos recurrentes, gráficos, otros meses en el Inicio.
- **Idempotencia** en los POST de stock ("Llegaron más" y "Corregir").
- Búsqueda sin tildes (`unaccent` de Postgres).

## Para confirmar con vos (las más importantes)

1. **Fórmula del precio sugerido** (#1): ¿margen sobre precio, 40 % por defecto, redondeo hacia arriba al entero?
2. **Costo = total del costeo, bloqueado** (#2): ¿está bien que el costo directo no se pueda editar mientras hay costeo?
3. **Anular compra no devuelve el costo** (#3).
4. **Cálculo del "ya cubriste X %"** (#4): ¿ganancia bruta contra gastos del mes está bien, o querés otra definición?
5. **"Cliente" escondido en la venta** (#22) y **calculadora solo en Editar producto** (#31).
6. **Anular siempre, nunca borrar** (#8), incluso gastos cargados por error.
7. **Precios de los planes** (#129) y **qué pasa al vencer** (#135). Y los datos reales de cobro (QR/cuenta) para el texto de pago (#131).
8. **Carrito**: que pedir no reserve stock (#144) y que la venta se cobre al precio de hoy (#146).
