using Hermes.Domain.Common;

namespace Hermes.Domain.Entities;

// Un producto del catálogo. NUNCA se borra: se archiva (puede tener ventas y compras, M5–M7).
// El stock NO está acá: se calcula sumando movimientos por variante (M5).
public class Producto : IPerteneceATienda
{
    public const int MaximoFotos = 8;
    public const int MaximoVariantes = 60;

    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TiendaId { get; set; }

    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    // Lo que cobra la dueña. decimal(12,2), en Bs.
    public decimal Precio { get; set; }
    // Lo que le cuesta a ella (0 = todavía no lo cargó). Lo actualizan las compras (M7) y el costeo (M8).
    public decimal Costo { get; set; }

    public Guid? CategoriaId { get; set; }
    public Categoria? Categoria { get; set; }

    // ¿Se cuentan las unidades de este producto? Apagado = "por encargo": piezas únicas, hecho a pedido,
    // servicios. No tiene stock, nunca figura agotado y vender no descuenta nada. El stock sigue siendo
    // una suma de movimientos; esto solo decide si el producto participa. Apagarlo no borra su historial.
    public bool ControlaStock { get; set; } = true;
    // "Por agotarse" cuando quedan estas o menos.
    public int StockMinimo { get; set; } = ReglaStock.MinimoPorDefecto;

    // Oculto de la tienda pública pero sigue en el catálogo (M10 lo usa).
    public bool VisibleEnTienda { get; set; } = true;
    public bool Archivado { get; set; }

    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset ActualizadoEn { get; set; }

    public List<Variante> Variantes { get; set; } = [];
    public List<ProductoFoto> Fotos { get; set; } = [];
}
