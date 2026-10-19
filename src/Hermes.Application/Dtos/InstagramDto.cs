using Hermes.Domain.Entities;

namespace Hermes.Application.Dtos;

public record LeerTextoDto(string Texto);

// Un producto leído. Faltan: qué le falta para poder publicarse ("nombre", "precio").
public record ProductoLeidoDto(
    string? Nombre, decimal? Precio, decimal? PrecioAnterior, IReadOnlyList<string> Colores, IReadOnlyList<string> Tallas,
    int? Stock, string? Categoria, string? Descripcion, IReadOnlyList<string> Avisos, IReadOnlyList<string> Faltan);

// "Probá tu formato": lo que se entendió, sin guardar nada.
public record LecturaDto(IReadOnlyList<ProductoLeidoDto> Productos, IReadOnlyList<string> Hashtags);

// Fotos / Enlace: solo los que vinieron de Instagram (M12) los traen; los pegados a mano, vacíos.
public record BorradorDto(
    Guid Id, EstadoBorrador Estado, string Origen, string Texto, string Nombre, decimal? Precio, decimal? PrecioAnterior,
    IReadOnlyList<string> Colores, IReadOnlyList<string> Tallas, int? Stock, string? Categoria, string? Descripcion,
    IReadOnlyList<string> Avisos, IReadOnlyList<string> Faltan, Guid? ProductoId, DateTimeOffset CreadoEn,
    IReadOnlyList<ImagenDto>? Fotos = null, string? Enlace = null);

// ───────────── Conexión con Instagram (M12) ─────────────

// Configurada = la plataforma tiene una app de Meta cargada (sin eso no se puede conectar: solo pegar texto).
// El token NUNCA viaja al navegador.
public record InstagramConexionDto(
    bool Configurada, bool Conectada, string? Usuario, DateTimeOffset? ConectadaEn, DateTimeOffset? UltimaSincronizacion,
    string? UltimoError, bool NecesitaReconectar);

public record UrlConexionDto(string Url);

// Lo que Instagram manda de vuelta a la dirección de regreso (?code=…&state=…).
public record ConectarInstagramDto(string Code, string State);

// Una vuelta de sincronización: publicaciones nuevas procesadas, borradores creados y las que no tenían productos.
public record SincronizacionDto(int Publicaciones, int Productos, int SinProductos, IReadOnlyList<string> Avisos);

public record FiltroBorradoresDto(EstadoBorrador Estado = EstadoBorrador.Pendiente, Guid? Cursor = null, int? Limite = null);

public record PaginaDeBorradoresDto(IReadOnlyList<BorradorDto> Items, Guid? Siguiente, int Pendientes);

// Lo que espera en la bandeja: Listos = con precio (se pueden agregar al catálogo), SinPrecio = hay que cargarlo al revisar.
public record ResumenBandejaDto(int Listos, int SinPrecio);

// Publicar un borrador: el producto como la dueña lo dejó al revisarlo. `Stock` (de "Quedan 5") solo se carga si el
// producto lleva control de stock y tiene UNA sola variante; con varias no se sabe cuántas son de cada una.
public record PublicarBorradorDto(GuardarProductoDto Producto, int? Stock = null);

// StockCargado = false con un AvisoStock explicando por qué (o qué hacer).
public record PublicacionDto(ProductoDto Producto, bool StockCargado, string? AvisoStock);
