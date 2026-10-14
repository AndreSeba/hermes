using Hermes.Domain.Common;
using Hermes.Domain.Enums;

namespace Hermes.Domain.Entities;

// Un hecho que cambió el stock de una variante. Es el libro del stock: NUNCA se edita ni se borra
// (una corrección es otro movimiento). El stock de una variante es SUM(Cantidad) de sus movimientos.
public class MovimientoStock : IPerteneceATienda
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TiendaId { get; set; }
    public Guid VarianteId { get; set; }
    public Variante? Variante { get; set; }

    public TipoMovimiento Tipo { get; set; }
    // Con signo: + entra, − sale. Nunca 0 (CHECK en la base).
    public int Cantidad { get; set; }
    // Cuánto quedó DESPUÉS de este movimiento. Es una foto para mostrar el historial ("Corregiste a 5"):
    // el stock real siempre es la suma, nunca este campo. Se calcula dentro del candado de la variante,
    // así que es exacto. Nunca negativo (CHECK).
    public int Saldo { get; set; }

    // La venta que lo originó (Venta y Devolucion). null en llegadas y correcciones.
    public Guid? VentaId { get; set; }

    public string? Nota { get; set; }
    // Solo en ingresos donde la dueña dijo cuánto pagó por unidad.
    public decimal? CostoUnitario { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}
