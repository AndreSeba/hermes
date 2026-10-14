using Hermes.Domain.Common;

namespace Hermes.Domain.Entities;

// Un gasto del negocio (alquiler, luz, publicidad, transporte…). NUNCA se borra: se ANULA y deja de contar.
// Son los gastos que NO son mercadería (esos son las compras): con ellos se ve cuánto de lo ganado ya los cubrió.
public class Gasto : IPerteneceATienda
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TiendaId { get; set; }

    public string Concepto { get; set; } = string.Empty;
    // Bs, > 0.
    public decimal Monto { get; set; }
    // UTC (DateTime por lo mismo que Venta.Fecha). Cuándo se pagó: por defecto ahora, se puede poner uno anterior.
    public DateTime Fecha { get; set; }

    // Igual que en Venta: la genera el celular; la misma clave devuelve el mismo gasto (no duplica).
    public Guid ClaveIdempotencia { get; set; }

    public bool Anulado { get; set; }
    public DateTime? AnuladoEn { get; set; }
}
