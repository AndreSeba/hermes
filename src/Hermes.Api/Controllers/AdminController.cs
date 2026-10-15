using Hermes.Api.Security;
using Hermes.Application.Dtos;
using Hermes.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hermes.Api.Controllers;

// El panel del administrador de la PLATAFORMA. Solo con el rol `admin` en el token (política "Admin"); ese rol solo se
// emite si el usuario es administrador en la base, y solo se le da desde el servidor (comando `promover-admin`).
[ApiController]
[Route("api/v1/admin")]
[Authorize(Policy = Politicas.Admin)]
public class AdminController : ControllerBase
{
    private readonly IAdminService _admin;

    public AdminController(IAdminService admin) => _admin = admin;

    // Los pagos a revisar (por defecto los pendientes, del más viejo al más nuevo).
    [HttpGet("pagos")]
    public async Task<ActionResult<PaginaDePagosDto>> Pagos([FromQuery] FiltroPagosDto filtro, CancellationToken ct) => Ok(await _admin.PagosAsync(filtro, ct));

    [HttpPost("pagos/{id:guid}/aprobar")]
    [ProducesResponseType(typeof(PagoParaRevisarDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PagoParaRevisarDto>> Aprobar(Guid id, CancellationToken ct) => Ok(await _admin.AprobarAsync(id, User.ObtenerUsuarioId(), ct));

    [HttpPost("pagos/{id:guid}/rechazar")]
    [ProducesResponseType(typeof(PagoParaRevisarDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PagoParaRevisarDto>> Rechazar(Guid id, [FromBody] RechazarPagoDto dto, CancellationToken ct)
        => Ok(await _admin.RechazarAsync(id, User.ObtenerUsuarioId(), dto, ct));

    [HttpGet("tiendas")]
    public async Task<ActionResult<PaginaDeTiendasDto>> Tiendas([FromQuery] FiltroTiendasDto filtro, CancellationToken ct) => Ok(await _admin.TiendasAsync(filtro, ct));

    // Cortesía, prueba o corrección: le pone un plan a una tienda sin pago.
    [HttpPut("tiendas/{id:guid}/plan")]
    [ProducesResponseType(typeof(AdminTiendaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminTiendaDto>> AsignarPlan(Guid id, [FromBody] AsignarPlanDto dto, CancellationToken ct)
        => Ok(await _admin.AsignarPlanAsync(id, User.ObtenerUsuarioId(), dto, ct));

    [HttpGet("planes")]
    public async Task<ActionResult<IReadOnlyList<PlanDto>>> Planes(CancellationToken ct) => Ok(await _admin.PlanesAsync(ct));

    [HttpPut("planes/{id:guid}")]
    [ProducesResponseType(typeof(PlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlanDto>> ActualizarPlan(Guid id, [FromBody] ActualizarPlanDto dto, CancellationToken ct) => Ok(await _admin.ActualizarPlanAsync(id, dto, ct));
}
