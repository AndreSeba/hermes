using Hermes.Application.Dtos;

namespace Hermes.Application.Interfaces;

// Los pedidos que llegan del carrito de la tienda pública. Nuevo → Confirmado → Entregado (registra la venta y ahí se
// descuenta el stock), o Cancelado. Cada paso pasa una sola vez (409 si ya estaba resuelto).
public interface IPedidoService
{
    Task<PaginaDePedidosDto> ListarAsync(FiltroPedidosDto filtro, CancellationToken ct);
    Task<ResumenPedidosDto> ResumenAsync(CancellationToken ct);
    // Unidades de los pedidos ENTREGADOS en el período (por la fecha en que se entregaron): "N desde la tienda web" del Inicio.
    Task<int> UnidadesEntregadasAsync(FiltroPeriodoDto filtro, CancellationToken ct);
    Task<PedidoDto> ObtenerAsync(Guid id, CancellationToken ct);
    Task<PedidoDto> ConfirmarAsync(Guid id, CancellationToken ct);
    Task<PedidoDto> CancelarAsync(Guid id, CancelarPedidoDto dto, CancellationToken ct);
    // Registra la venta (IVentaService, con el Id del pedido como clave: reintentar no duplica) y lo marca Entregado.
    Task<PedidoDto> EntregarAsync(Guid id, EntregarPedidoDto dto, CancellationToken ct);
}
