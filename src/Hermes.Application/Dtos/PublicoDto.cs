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
