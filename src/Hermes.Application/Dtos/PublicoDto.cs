using Hermes.Domain.Entities;
using Hermes.Domain.Enums;

namespace Hermes.Application.Dtos;

// ───────────── Configuración desde el panel ("Qué ven tus clientes" y "Personalizar diseño") ─────────────

// Publicada = la tienda pública se ve (Tienda.Activa). Apagada, /{slug} da 404.
public record ConfiguracionPublicaDto(bool Publicada, bool MostrarPrecios, bool MostrarDisponibilidad, bool PedidosPorWhatsapp, FondoTienda Fondo);

// ───────────── Lo que ve el público (SIN login) ─────────────
//
// REGLA: estos DTOs NUNCA llevan costo, margen, ganancia ni stock exacto. Solo "disponible / agotado". Hay una prueba
// que serializa cada uno y busca esas palabras. Si se agrega un campo acá, pensar primero si un cliente debe verlo.

public record CategoriaPublicaDto(Guid Id, string Nombre);

// Whatsapp: solo si la tienda tiene "Pedidos por WhatsApp" encendido y cargó su número ("+591XXXXXXXX").
public record TiendaPublicaDto(
    string Nombre, string Slug, string? Descripcion, string Color, FondoTienda Fondo, ImagenDto? Logo,
    bool MostrarPrecios, bool MostrarDisponibilidad, string? Whatsapp, IReadOnlyList<CategoriaPublicaDto> Categorias);

// Precio es null si la tienda no muestra precios; Disponible es null si no muestra disponibilidad.
public record ProductoPublicoResumenDto(Guid Id, string Nombre, decimal? Precio, ImagenDto? Portada, string? Categoria, bool? Disponible);

public record VariantePublicaDto(Guid Id, string? Color, string? Talla, bool? Disponible);

public record ProductoPublicoDto(
    Guid Id, string Nombre, string? Descripcion, decimal? Precio, string? Categoria, bool? Disponible,
    IReadOnlyList<ImagenDto> Fotos, IReadOnlyList<VariantePublicaDto> Variantes);

public record FiltroPublicoDto(string? Buscar = null, Guid? CategoriaId = null, string? Cursor = null, int? Limite = null);

// Lo disponible primero y los agotados al final. Siguiente: cursor opaco.
public record PaginaPublicaDto(IReadOnlyList<ProductoPublicoResumenDto> Items, string? Siguiente);

// ───────────── Carrito y pedido (el carrito vive en el navegador; acá se revisa y se manda) ─────────────
// OJO: "Unidades" y no "Cantidad" a propósito: la prueba de "lo que nunca sale" prohíbe esa palabra en todo DTO público
// (para que nunca se cuele el stock). Las unidades acá son las que pide el cliente, no las que hay.

// Las variantes que el cliente tiene en su carrito (como mucho Pedido.MaximoLineas).
public record RevisarCarritoDto(IReadOnlyList<Guid> Variantes);

// Cómo está HOY cada cosa del carrito: nombre, precio (null si la tienda no los muestra) y si se puede pedir (null si no
// muestra disponibilidad). Lo que ya no existe, se archivó o se ocultó no vuelve: el navegador lo saca del carrito.
public record ArticuloCarritoDto(Guid VarianteId, Guid ProductoId, string Nombre, string? Variante, decimal? Precio, bool? Disponible, ImagenDto? Foto);

public record CarritoPublicoDto(IReadOnlyList<ArticuloCarritoDto> Articulos, bool RecibePedidos);

public record ArticuloPedidoDto(Guid VarianteId, int Unidades);

// Entrega = Retiro o Envio (con Direccion). Celular: de Bolivia, se guarda "+591XXXXXXXX".
public record NuevoPedidoDto(
    Guid ClaveIdempotencia, IReadOnlyList<ArticuloPedidoDto> Articulos, string Nombre, string Celular,
    EntregaPedido Entrega, string? Direccion, string? Referencia, string? Nota);

public record LineaPedidoPublicaDto(string Descripcion, int Unidades, decimal? Precio);

// Lo que ve el cliente después de pedir (y con qué arma el mensaje de WhatsApp). Nuevo = false: era un reintento.
public record PedidoEnviadoDto(
    string Tienda, int Numero, IReadOnlyList<LineaPedidoPublicaDto> Lineas, decimal? Total,
    string Nombre, EntregaPedido Entrega, string? Direccion, string Whatsapp, bool Nuevo);
