using Hermes.Domain.Entities;
using Hermes.Domain.Enums;

namespace Hermes.Application.Dtos;

// ───────────── Pedidos de la tienda pública, vistos desde el panel ─────────────

// PrecioUnitario = el del momento del pedido; PrecioActual = el de hoy (la venta se registra con este: si difieren, el panel avisa).
public record PedidoLineaDto(Guid VarianteId, Guid ProductoId, string Descripcion, int Cantidad, decimal PrecioUnitario, decimal Subtotal, decimal PrecioActual);

public record PedidoDto(
    Guid Id, int Numero, DateTimeOffset Fecha, EstadoPedido Estado,
    string ClienteNombre, string ClienteCelular, EntregaPedido Entrega, string? Direccion, string? Referencia, string? Nota,
    decimal Total, int Unidades, IReadOnlyList<PedidoLineaDto> Lineas,
    Guid? VentaId, string? MotivoCancelacion, DateTimeOffset? ResueltoEn);

// Detalle: "2 Cartera · 1 Aretes · y 1 más".
public record PedidoResumenDto(
    Guid Id, int Numero, DateTimeOffset Fecha, EstadoPedido Estado, string ClienteNombre, EntregaPedido Entrega,
    decimal Total, int Unidades, string Detalle);

// Pendientes = Nuevo + Confirmado (lo que hay que atender).
public enum VistaPedidos
{
    Pendientes,
    Entregados,
    Cancelados,
    Todos,
}

public record FiltroPedidosDto(VistaPedidos Vista = VistaPedidos.Pendientes, Guid? Cursor = null, int? Limite = null);

public record PaginaDePedidosDto(IReadOnlyList<PedidoResumenDto> Items, Guid? Siguiente);

public record ResumenPedidosDto(int Nuevos, int Confirmados);

public record CancelarPedidoDto(string? Motivo);

// Registrar la venta del pedido: con qué pagó el cliente.
public record EntregarPedidoDto(MetodoPago MetodoPago);
