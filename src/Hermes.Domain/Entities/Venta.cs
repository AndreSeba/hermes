using Hermes.Domain.Common;
using Hermes.Domain.Enums;

namespace Hermes.Domain.Entities;

// Una venta registrada. NUNCA se borra: se ANULA (devuelve el stock y deja de contar en los números).
// El total y la ganancia no se guardan: salen de las líneas, que guardan el precio y el costo del momento.
public class Venta : IPerteneceATienda
{
    public const int MaximoLineas = 50;

    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TiendaId { get; set; }

    // UTC. `DateTime` y no `DateTimeOffset` a propósito: EF no puede comparar DateTimeOffset en SQL con SQLite
    // (las pruebas), y las ventas se filtran por rango de fechas. En Postgres es el mismo timestamptz.
    public DateTime Fecha { get; set; }
    public MetodoPago MetodoPago { get; set; }
    // Opcional: "Nombre o celular". Registrar una venta tiene que ser rápido.
    public string? Cliente { get; set; }

    // La genera el celular al abrir la pantalla de venta. Si la misma llega dos veces (doble toque,
    // reintento por mala conexión), la segunda devuelve la venta ya registrada en vez de duplicarla.
    public Guid ClaveIdempotencia { get; set; }

    // Quién la registró (null en las ventas anteriores a los roles). Una persona sin el permiso de ver todas las ventas ve solo las suyas.
    public Guid? UsuarioId { get; set; }

    public bool Anulada { get; set; }
    public DateTime? AnuladaEn { get; set; }

    public List<VentaLinea> Lineas { get; set; } = [];
}
