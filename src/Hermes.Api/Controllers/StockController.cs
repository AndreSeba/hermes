using Hermes.Application.Dtos;
using Hermes.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hermes.Api.Controllers;

[ApiController]
[Route("api/v1/stock")]
[Authorize]
public class StockController : ControllerBase
{
    private readonly IStockService _stockService;

    public StockController(IStockService stockService) => _stockService = stockService;

    // Lo más urgente primero: agotados, por agotarse, bien y por encargo. Paginado por cursor.
    [HttpGet]
    [ProducesResponseType(typeof(PaginaDeStockDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PaginaDeStockDto>> Listar([FromQuery] FiltroStockDto filtro, CancellationToken ct)
        => Ok(await _stockService.ListarAsync(filtro, ct));

    [HttpGet("resumen")]
    public async Task<ActionResult<ResumenStockDto>> Resumen(CancellationToken ct)
        => Ok(await _stockService.ResumenAsync(ct));

    [HttpGet("{productoId:guid}")]
    [ProducesResponseType(typeof(ProductoStockDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductoStockDto>> Obtener(Guid productoId, CancellationToken ct)
        => Ok(await _stockService.ObtenerAsync(productoId, ct));

    // "Lo último que pasó" de un producto, más nuevo primero.
    [HttpGet("{productoId:guid}/movimientos")]
    [ProducesResponseType(typeof(PaginaDeMovimientosDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaginaDeMovimientosDto>> Movimientos(Guid productoId, [FromQuery] FiltroMovimientosDto filtro, CancellationToken ct)
        => Ok(await _stockService.MovimientosAsync(productoId, filtro, ct));

    // "Llegaron más".
    [HttpPost("ingresos")]
    [ProducesResponseType(typeof(ProductoStockDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductoStockDto>> Ingresar([FromBody] IngresoDto dto, CancellationToken ct)
        => Ok(await _stockService.IngresarAsync(dto, ct));

    // "Conté y hay otra cantidad".
    [HttpPost("ajustes")]
    [ProducesResponseType(typeof(ProductoStockDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductoStockDto>> Ajustar([FromBody] AjusteDto dto, CancellationToken ct)
        => Ok(await _stockService.AjustarAsync(dto, ct));
}
