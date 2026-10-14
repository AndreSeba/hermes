using Hermes.Application.Dtos;
using Hermes.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hermes.Api.Controllers;

[ApiController]
[Route("api/v1/ventas")]
[Authorize]
public class VentasController : ControllerBase
{
    private readonly IVentaService _ventaService;

    public VentasController(IVentaService ventaService) => _ventaService = ventaService;

    // Más nuevas primero, por cursor. Las anuladas no salen salvo que se pidan.
    [HttpGet]
    public async Task<ActionResult<PaginaDeVentasDto>> Listar([FromQuery] FiltroVentasDto filtro, CancellationToken ct)
        => Ok(await _ventaService.ListarAsync(filtro, ct));

    [HttpGet("resumen")]
    public async Task<ActionResult<ResumenVentasDto>> Resumen([FromQuery] FiltroPeriodoDto filtro, CancellationToken ct)
        => Ok(await _ventaService.ResumenAsync(filtro, ct));

    // Los productos para elegir en "Nueva venta": con sus variantes y cuánto queda; los agotados al final.
    [HttpGet("productos")]
    public async Task<ActionResult<PaginaDeVendiblesDto>> Productos([FromQuery] FiltroVendiblesDto filtro, CancellationToken ct)
        => Ok(await _ventaService.ProductosParaVenderAsync(filtro, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(VentaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VentaDto>> Obtener(Guid id, CancellationToken ct)
        => Ok(await _ventaService.ObtenerAsync(id, ct));

    // 201 si se registró; 200 si esa ClaveIdempotencia ya había registrado la venta (se devuelve la misma).
    [HttpPost]
    [ProducesResponseType(typeof(VentaDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(VentaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VentaDto>> Registrar([FromBody] RegistrarVentaDto dto, CancellationToken ct)
    {
        var resultado = await _ventaService.RegistrarAsync(dto, ct);
        return resultado.Creada
            ? CreatedAtAction(nameof(Obtener), new { id = resultado.Venta.Id }, resultado.Venta)
            : Ok(resultado.Venta);
    }

    [HttpPost("{id:guid}/anular")]
    [ProducesResponseType(typeof(VentaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VentaDto>> Anular(Guid id, CancellationToken ct)
        => Ok(await _ventaService.AnularAsync(id, ct));
}
