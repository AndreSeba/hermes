using Hermes.Application.Dtos;
using Hermes.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hermes.Api.Controllers;

// Los pedidos que llegan del carrito de la tienda pública. Los crea el sitio público (Hermes.Public), no esta API.
[ApiController]
[Route("api/v1/pedidos")]
[Authorize]
public class PedidosController : ControllerBase
{
    private readonly IPedidoService _pedidos;

    public PedidosController(IPedidoService pedidos) => _pedidos = pedidos;

    // Más nuevos primero, por cursor. Vista: Pendientes (por defecto), Entregados, Cancelados o Todos.
    [HttpGet]
    public async Task<ActionResult<PaginaDePedidosDto>> Listar([FromQuery] FiltroPedidosDto filtro, CancellationToken ct)
        => Ok(await _pedidos.ListarAsync(filtro, ct));

    // Cuántos esperan (para el aviso del menú y del Inicio).
    [HttpGet("resumen")]
    public async Task<ActionResult<ResumenPedidosDto>> Resumen(CancellationToken ct)
        => Ok(await _pedidos.ResumenAsync(ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PedidoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PedidoDto>> Obtener(Guid id, CancellationToken ct)
        => Ok(await _pedidos.ObtenerAsync(id, ct));

    [HttpPost("{id:guid}/confirmar")]
    [ProducesResponseType(typeof(PedidoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PedidoDto>> Confirmar(Guid id, CancellationToken ct)
        => Ok(await _pedidos.ConfirmarAsync(id, ct));

    [HttpPost("{id:guid}/cancelar")]
    [ProducesResponseType(typeof(PedidoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PedidoDto>> Cancelar(Guid id, [FromBody] CancelarPedidoDto dto, CancellationToken ct)
        => Ok(await _pedidos.CancelarAsync(id, dto, ct));

    // Registra la venta (descuenta el stock) y lo marca entregado. Sin stock suficiente: 409 y el pedido no cambia.
    [HttpPost("{id:guid}/entregar")]
    [ProducesResponseType(typeof(PedidoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PedidoDto>> Entregar(Guid id, [FromBody] EntregarPedidoDto dto, CancellationToken ct)
        => Ok(await _pedidos.EntregarAsync(id, dto, ct));
}
