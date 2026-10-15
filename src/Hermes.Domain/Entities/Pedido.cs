using Hermes.Domain.Common;

namespace Hermes.Domain.Entities;

public enum EstadoPedido
{
    Nuevo,       // recién llegó de la tienda pública
    Confirmado,  // la dueña lo vio y le avisó al cliente
    Entregado,   // se registró la venta (descontó el stock)
    Cancelado,
}

public enum EntregaPedido
{
    Retiro, // pasa a buscarlo
    Envio,  // a domicilio: lleva dirección
}

// Un pedido que hizo un cliente desde la tienda pública (carrito). NO toca el stock: recién al registrar la venta se descuenta.
// Nunca se borra: termina Entregado (con su venta) o Cancelado.
public class Pedido : IPerteneceATienda
{
    public const int MaximoLineas = 30;
    public const int MaximoCantidad = 99;

    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TiendaId { get; set; }

    // "Pedido #12": correlativo por tienda (índice único (TiendaId, Numero)).
    public int Numero { get; set; }
    // La genera el navegador al abrir el carrito: un doble toque o un reintento no duplica el pedido.
    public Guid ClaveIdempotencia { get; set; }
    // UTC. DateTime (no DateTimeOffset) por lo mismo que Venta.Fecha: SQLite no lo compara en SQL.
    public DateTime Fecha { get; set; }

    public string ClienteNombre { get; set; } = string.Empty;
    // "+591XXXXXXXX" (Validacion.Whatsapp): la dueña le escribe desde el panel.
    public string ClienteCelular { get; set; } = string.Empty;
    public EntregaPedido Entrega { get; set; }
    public string? Direccion { get; set; }
    public string? Referencia { get; set; }
    public string? Nota { get; set; }

    public EstadoPedido Estado { get; set; } = EstadoPedido.Nuevo;
    public DateTime? ResueltoEn { get; set; }
    public string? MotivoCancelacion { get; set; }
    // La venta que se registró al entregarlo.
    public Guid? VentaId { get; set; }

    public List<PedidoLinea> Lineas { get; set; } = [];
}

// Una línea del pedido con una FOTO del momento (nombre y precio): lo que el cliente vio cuando pidió.
public class PedidoLinea : IPerteneceATienda
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TiendaId { get; set; }
    public Guid PedidoId { get; set; }
    public Pedido? Pedido { get; set; }

    public Guid ProductoId { get; set; }
    public Guid VarianteId { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public int Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    // Orden en que el cliente los puso (los Guid v7 del mismo milisegundo no tienen orden).
    public int Orden { get; set; }
}
