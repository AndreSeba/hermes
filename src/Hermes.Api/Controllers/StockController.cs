using Hermes.Api.Security;
using Hermes.Application.Dtos;
using Hermes.Application.Interfaces;
using Hermes.Domain.Seguridad;
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
    [Authorize(Policy = Permisos.StockVer)]
    [ProducesResponseType(typeof(PaginaDeStockDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PaginaDeStockDto>> Listar([FromQuery] FiltroStockDto filtro, CancellationToken ct)
        => Ok((await _stockService.ListarAsync(filtro, ct)).Costos(VeCostos));

    [HttpGet("resumen")]
    [Authorize(Policy = Permisos.StockVer)]
    public async Task<ActionResult<ResumenStockDto>> Resumen(CancellationToken ct)
        => Ok((await _stockService.ResumenAsync(ct)).Costos(VeCostos));

    [HttpGet("{productoId:guid}")]
    [Authorize(Policy = Permisos.StockVer)]
    [ProducesResponseType(typeof(ProductoStockDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductoStockDto>> Obtener(Guid productoId, CancellationToken ct)
        => Ok((await _stockService.ObtenerAsync(productoId, ct)).Costos(VeCostos));

    // "Lo último que pasó" de un producto, más nuevo primero.
    [HttpGet("{productoId:guid}/movimientos")]
    [Authorize(Policy = Permisos.StockVer)]
    [ProducesResponseType(typeof(PaginaDeMovimientosDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaginaDeMovimientosDto>> Movimientos(Guid productoId, [FromQuery] FiltroMovimientosDto filtro, CancellationToken ct)
        => Ok((await _stockService.MovimientosAsync(productoId, filtro, ct)).Costos(VeCostos));

    // "Llegaron más".
    [HttpPost("ingresos")]
    [Authorize(Policy = Permisos.StockAjustar)]
    [ProducesResponseType(typeof(ProductoStockDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductoStockDto>> Ingresar([FromBody] IngresoDto dto, CancellationToken ct)
        => Ok((await _stockService.IngresarAsync(NoTocarCostos(dto), ct)).Costos(VeCostos));

    // "Conté y hay otra cantidad".
    [HttpPost("ajustes")]
    [Authorize(Policy = Permisos.StockAjustar)]
    [ProducesResponseType(typeof(ProductoStockDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductoStockDto>> Ajustar([FromBody] AjusteDto dto, CancellationToken ct)
        => Ok((await _stockService.AjustarAsync(dto, ct)).Costos(VeCostos));

    private bool VeCostos => User.Tiene(Permisos.CostosVer);

    // "Llegaron más" puede traer un costo nuevo que pasa a ser el del producto: solo quien edita costos puede cambiarlo.
    private IngresoDto NoTocarCostos(IngresoDto dto) => User.Tiene(Permisos.CostosEditar) ? dto : dto with { CostoUnitario = null };
}
