# Decisiones tomadas sin consultar

Registro de lo que se decidió por criterio propio mientras se construía (sin que el usuario lo pidiera de forma
explícita). Cada una dice **por qué** y **cómo se cambia**. Las marcadas con ⚠️ son las que más conviene revisar
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
