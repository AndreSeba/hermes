using Hermes.Api.Security;
using Hermes.Application.Dtos;
using Hermes.Application.Interfaces;
using Hermes.Domain.Seguridad;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hermes.Api.Controllers;

// Quien no tiene "ventas.ver" (ni "inicio.ver") ve SOLO las ventas que registró él: lo decide VentaService.
[ApiController]
[Route("api/v1/ventas")]
[Authorize]
public class VentasController : ControllerBase
{
    // Quien registra ventas ve las suyas; quien mira el negocio ve las de todos.
    private const string VerVentas = $"{Permisos.VentasVer}|{Permisos.VentasRegistrar}|{Permisos.InicioVer}";
    // Los números del período (resumen, gráfico, más vendidos): el Inicio o el listado de todas.
    private const string VerNumeros = $"{Permisos.VentasVer}|{Permisos.InicioVer}";

    private readonly IVentaService _ventaService;

    public VentasController(IVentaService ventaService) => _ventaService = ventaService;

    private bool VeCostos => User.Tiene(Permisos.CostosVer);

    // Más nuevas primero, por cursor. Las anuladas no salen salvo que se pidan.
    [HttpGet]
    [Authorize(Policy = VerVentas)]
    public async Task<ActionResult<PaginaDeVentasDto>> Listar([FromQuery] FiltroVentasDto filtro, CancellationToken ct)
        => Ok((await _ventaService.ListarAsync(filtro, ct)).Costos(VeCostos));

    [HttpGet("resumen")]
    [Authorize(Policy = VerNumeros)]
    public async Task<ActionResult<ResumenVentasDto>> Resumen([FromQuery] FiltroPeriodoDto filtro, CancellationToken ct)
        => Ok((await _ventaService.ResumenAsync(filtro, ct)).Costos(VeCostos));

    // Lo vendido día por día (el gráfico del Inicio): siempre vienen todos los días pedidos, con 0 si no se vendió.
    [HttpGet("por-dia")]
    [Authorize(Policy = VerNumeros)]
    [ProducesResponseType(typeof(IReadOnlyList<VentaDiaDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<VentaDiaDto>>> PorDia([FromQuery] FiltroPorDiaDto filtro, CancellationToken ct)
        => Ok(await _ventaService.PorDiaAsync(filtro, ct));

    // Lo que más se vendió en el período (por unidades), con la ganancia de cada producto.
    [HttpGet("mas-vendidos")]
    [Authorize(Policy = VerNumeros)]
    public async Task<ActionResult<IReadOnlyList<MasVendidoDto>>> MasVendidos([FromQuery] FiltroMasVendidosDto filtro, CancellationToken ct)
        => Ok((await _ventaService.MasVendidosAsync(filtro, ct)).Costos(VeCostos));

    // Lo que vendió cada persona en el período (quien no ve todo, solo aparece él).
    [HttpGet("por-vendedor")]
    [Authorize(Policy = VerNumeros)]
    public async Task<ActionResult<IReadOnlyList<VentaVendedorDto>>> PorVendedor([FromQuery] FiltroPeriodoDto filtro, CancellationToken ct)
        => Ok(await _ventaService.PorVendedorAsync(filtro, ct));

    // Los productos para elegir en "Nueva venta": con sus variantes y cuánto queda; los agotados al final.
    [HttpGet("productos")]
    [Authorize(Policy = Permisos.VentasRegistrar)]
    public async Task<ActionResult<PaginaDeVendiblesDto>> Productos([FromQuery] FiltroVendiblesDto filtro, CancellationToken ct)
        => Ok((await _ventaService.ProductosParaVenderAsync(filtro, ct)).Costos(VeCostos));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = VerVentas)]
    [ProducesResponseType(typeof(VentaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VentaDto>> Obtener(Guid id, CancellationToken ct)
        => Ok((await _ventaService.ObtenerAsync(id, ct)).Costos(VeCostos));

    // 201 si se registró; 200 si esa ClaveIdempotencia ya había registrado la venta (se devuelve la misma).
    [HttpPost]
    [Authorize(Policy = Permisos.VentasRegistrar)]
    [ProducesResponseType(typeof(VentaDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(VentaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VentaDto>> Registrar([FromBody] RegistrarVentaDto dto, CancellationToken ct)
    {
        var resultado = await _ventaService.RegistrarAsync(dto, ct);
        var venta = resultado.Venta.Costos(VeCostos);
        return resultado.Creada
            ? CreatedAtAction(nameof(Obtener), new { id = venta.Id }, venta)
            : Ok(venta);
    }

    [HttpPost("{id:guid}/anular")]
    [Authorize(Policy = Permisos.VentasAnular)]
    [ProducesResponseType(typeof(VentaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VentaDto>> Anular(Guid id, CancellationToken ct)
        => Ok((await _ventaService.AnularAsync(id, ct)).Costos(VeCostos));
}
