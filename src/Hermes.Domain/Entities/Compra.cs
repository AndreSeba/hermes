using Hermes.Domain.Common;

namespace Hermes.Domain.Entities;

// Un pedido a un proveedor. Vive en dos ejes independientes:
//   · Recibida: llegó la mercadería (suma stock y actualiza el costo; una sola vez).
//   · Pagada: ya se le pagó al proveedor (se puede pagar antes o después de recibir).
// NUNCA se borra: se ANULA (si ya estaba recibida, revierte el stock). El total sale de las líneas.
public class Compra : IPerteneceATienda
{
    public const int MaximoLineas = 50;

    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TiendaId { get; set; }
    public Guid ProveedorId { get; set; }
    public Proveedor? Proveedor { get; set; }

    // UTC (DateTime por lo mismo que Venta.Fecha: SQLite no compara DateTimeOffset en SQL).
    public DateTime Fecha { get; set; }
    public string? Notas { get; set; }

    // Igual que en Venta: la genera el celular; la misma clave devuelve la misma compra.
    public Guid ClaveIdempotencia { get; set; }

    public bool Recibida { get; set; }
    public DateTime? RecibidaEn { get; set; }
    public bool Pagada { get; set; }
    public DateTime? PagadaEn { get; set; }
    public bool Anulada { get; set; }
    public DateTime? AnuladaEn { get; set; }

    public List<CompraLinea> Lineas { get; set; } = [];
}

// Una variante, cuántas se piden y a cuánto cada una. Guarda el nombre del momento.
public class CompraLinea : IPerteneceATienda
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TiendaId { get; set; }
    public Guid CompraId { get; set; }
    public Compra? Compra { get; set; }

    // El orden en que se muestran (no ordenar por Id: los Guid v7 del mismo milisegundo salen en cualquier orden).
    public int Orden { get; set; }

    public Guid ProductoId { get; set; }
    public Guid VarianteId { get; set; }
    public string Descripcion { get; set; } = string.Empty;

    public int Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }

    // ¿Al recibir, esta línea sumó stock? (el producto llevaba control). Anular una compra recibida solo
    // resta lo que se sumó.
    public bool SumoStock { get; set; }
}
