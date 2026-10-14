using Hermes.Domain.Entities;

namespace Hermes.Application.Dtos;

public record LeerTextoDto(string Texto);

// Un producto leído. Faltan: qué le falta para poder publicarse ("nombre", "precio").
public record ProductoLeidoDto(
    string? Nombre, decimal? Precio, decimal? PrecioAnterior, IReadOnlyList<string> Colores, IReadOnlyList<string> Tallas,
    int? Stock, string? Categoria, string? Descripcion, IReadOnlyList<string> Avisos, IReadOnlyList<string> Faltan);

// "Probá tu formato": lo que se entendió, sin guardar nada.
public record LecturaDto(IReadOnlyList<ProductoLeidoDto> Productos, IReadOnlyList<string> Hashtags);

public record BorradorDto(
    Guid Id, EstadoBorrador Estado, string Origen, string Texto, string Nombre, decimal? Precio, decimal? PrecioAnterior,
    IReadOnlyList<string> Colores, IReadOnlyList<string> Tallas, int? Stock, string? Categoria, string? Descripcion,
    IReadOnlyList<string> Avisos, IReadOnlyList<string> Faltan, Guid? ProductoId, DateTimeOffset CreadoEn);

public record FiltroBorradoresDto(EstadoBorrador Estado = EstadoBorrador.Pendiente, Guid? Cursor = null, int? Limite = null);

public record PaginaDeBorradoresDto(IReadOnlyList<BorradorDto> Items, Guid? Siguiente, int Pendientes);

// Publicar un borrador: el producto como la dueña lo dejó al revisarlo. `Stock` (de "Quedan 5") solo se carga si el
// producto lleva control de stock y tiene UNA sola variante; con varias no se sabe cuántas son de cada una.
public record PublicarBorradorDto(GuardarProductoDto Producto, int? Stock = null);

// StockCargado = false con un AvisoStock explicando por qué (o qué hacer).
public record PublicacionDto(ProductoDto Producto, bool StockCargado, string? AvisoStock);
