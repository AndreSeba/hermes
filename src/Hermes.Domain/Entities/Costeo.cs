using Hermes.Domain.Common;

namespace Hermes.Domain.Entities;

// El costeo opcional de un producto: de qué se compone lo que le cuesta a la dueña. Uno por producto.
// Mientras exista, `Producto.Costo` es SIEMPRE el total de estos componentes (ReglaCosteo.Total): se guarda
// ahí porque ventas, stock y catálogo ya leen ese número. El precio sugerido NO se guarda: se calcula.
public class Costeo : IPerteneceATienda
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TiendaId { get; set; }
    public Guid ProductoId { get; set; }

    // Por unidad, en Bs. Todos ≥ 0; al menos uno > 0.
    public decimal Material { get; set; }
    public decimal Empaque { get; set; }
    public decimal ManoDeObra { get; set; }
    // La parte que le toca de los gastos fijos (luz, alquiler…).
    public decimal CostoFijo { get; set; }

    // Cuánto quiere ganar, como % del PRECIO (igual que el margen del catálogo): 1 a 90.
    public int MargenObjetivo { get; set; } = ReglaCosteo.MargenPorDefecto;
}

// LA regla del precio sugerido, en un solo lugar (el front solo muestra lo que calcula el servidor).
public static class ReglaCosteo
{
    public const int MargenPorDefecto = 40;
    public const int MargenMinimo = 1;
    public const int MargenMaximo = 90;

    public static decimal Total(decimal material, decimal empaque, decimal manoDeObra, decimal costoFijo)
        => material + empaque + manoDeObra + costoFijo;

    // precio = costo ÷ (1 − margen): con ese precio, la ganancia es exactamente `margen` % del precio. Se redondea
    // HACIA ARRIBA al boliviano entero (cobrar Bs 63,17 no es práctico y redondear hacia abajo comería el margen).
    public static decimal PrecioSugerido(decimal costoTotal, int margenObjetivo)
        => costoTotal <= 0 ? 0 : Math.Ceiling(costoTotal / (1 - margenObjetivo / 100m));
}
