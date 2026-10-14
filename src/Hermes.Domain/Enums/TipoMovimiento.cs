namespace Hermes.Domain.Enums;

// Qué pasó con el stock de una variante. El stock NO se guarda: es la suma de los movimientos.
public enum TipoMovimiento
{
    Ingreso,    // "Llegaron más": suma
    Ajuste,     // la dueña contó y hay otra cantidad: suma o resta la diferencia (con motivo)
    Venta,      // resta (M6)
    Devolucion, // suma: se anuló una venta (M6)
    Reversion,  // resta: se anuló una compra que ya se había recibido (M7)
}
