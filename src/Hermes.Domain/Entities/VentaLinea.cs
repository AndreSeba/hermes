using Hermes.Domain.Common;

namespace Hermes.Domain.Entities;

// Una línea de la venta: una variante y cuántas se vendieron. Guarda una FOTO de lo que valía en ese
// momento (nombre, precio, costo): cambiar el producto después no reescribe lo que ya se vendió.
public class VentaLinea : IPerteneceATienda
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TiendaId { get; set; }
    public Guid VentaId { get; set; }
    public Venta? Venta { get; set; }

    public Guid ProductoId { get; set; }
    public Guid VarianteId { get; set; }
    // "Cartera de cuero Mía (café · M)" tal como se vendió.
    public string Descripcion { get; set; } = string.Empty;

    public int Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    // El costo del producto al vender. 0 = la dueña todavía no lo había cargado: esa línea no cuenta
    // para la ganancia (no se puede saber), en vez de contar como si todo fuera ganancia.
    public decimal CostoUnitario { get; set; }

    // ¿Esta línea descontó stock? (el producto llevaba control en ese momento). Anular la venta solo
    // devuelve lo que se descontó: si el producto pasó a llevar control DESPUÉS, no se inventan unidades.
    public bool DescontoStock { get; set; }
}
