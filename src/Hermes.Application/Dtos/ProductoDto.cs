using Hermes.Domain;

namespace Hermes.Application.Dtos;

// Color y Talla son null cuando no aplican (la variante "Único").
public record VarianteDto(Guid Id, string? Color, string? Talla);

// Lo que muestra cada tarjeta del catálogo. Ganancia y EstadoPrecio los calcula MargenProducto.
public record ProductoResumenDto(
    Guid Id, string Nombre, decimal Precio, decimal Costo, decimal Ganancia, EstadoPrecio EstadoPrecio,
    Guid? CategoriaId, string? Categoria, ImagenDto? Portada, bool VisibleEnTienda, bool Archivado, int Variantes,
    // Stock es null si el producto no lleva control ("por encargo").
    int? Stock, EstadoStock EstadoStock);

// El producto completo (editar). Variantes: solo las activas. Fotos: en orden, la primera es la portada.
public record ProductoDto(
    Guid Id, string Nombre, string? Descripcion, decimal Precio, decimal Costo, decimal Ganancia, EstadoPrecio EstadoPrecio,
    Guid? CategoriaId, bool VisibleEnTienda, bool Archivado,
    IReadOnlyList<VarianteDto> Variantes, IReadOnlyList<ImagenDto> Fotos,
    bool ControlaStock, int StockMinimo);

// Crear y editar usan lo mismo. Las variantes salen de Colores × Tallas ("Colores: café, negro" y
// "Tallas: S, M" son 4 variantes); ambas vacías = una sola, "Único". FotoIds: los Id que devolvió
// POST /imagenes, en el orden en que se muestran (la primera es la portada).
public record GuardarProductoDto(
    string Nombre, string? Descripcion, decimal Precio, decimal Costo, Guid? CategoriaId, bool VisibleEnTienda,
    IReadOnlyList<string>? Colores, IReadOnlyList<string>? Tallas, IReadOnlyList<Guid>? FotoIds,
    // Llevar control de stock (apagado = "por encargo") y cuántas quedan para avisar "por agotarse".
    bool ControlaStock = true, int StockMinimo = ReglaStock.MinimoPorDefecto);

// Parámetros de GET /productos. Cursor = el `siguiente` de la página anterior. `Pagina` (desde 1) pide una página
// numerada (anterior / 1 2 3 / siguiente) y tiene prioridad sobre el cursor.
public record FiltroProductosDto(
    string? Buscar = null, Guid? CategoriaId = null, bool RevisarPrecio = false, bool Archivados = false,
    Guid? Cursor = null, int? Limite = null, int? Pagina = null);

// Siguiente es null en la última página. Total / Pagina / TotalPaginas solo vienen con una página numerada.
public record PaginaDeProductosDto(IReadOnlyList<ProductoResumenDto> Items, Guid? Siguiente, int Total = 0, int Pagina = 1, int TotalPaginas = 1);
