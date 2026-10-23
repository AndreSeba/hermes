using Hermes.Application.Dtos;
using Hermes.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Hermes.Domain.Seguridad;

namespace Hermes.Api.Controllers;

// El plan de la tienda de la sesión: ver el suyo, ver los que hay y pedir uno (el pago es fuera de la app: QR o transferencia).
[ApiController]
[Authorize]
public class PlanesController : ControllerBase
{
    private readonly IPlanService _planes;

    public PlanesController(IPlanService planes) => _planes = planes;

    [HttpGet("api/v1/planes")]
    public async Task<ActionResult<PlanesDto>> Listar(CancellationToken ct) => Ok(await _planes.ListarPlanesAsync(ct));

    // Su plan, cuánto usa de cada límite y el pago que espera revisión (si hay).
    [HttpGet("api/v1/plan")]
    public async Task<ActionResult<MiPlanDto>> MiPlan(CancellationToken ct) => Ok(await _planes.MiPlanAsync(ct));

    [HttpPost("api/v1/plan/solicitar")]
    [Authorize(Policy = Permisos.PlanGestionar)]
    [ProducesResponseType(typeof(PagoPlanDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PagoPlanDto>> Solicitar([FromBody] SolicitarPlanDto dto, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await _planes.SolicitarAsync(dto, ct));

    [HttpGet("api/v1/plan/pagos")]
    [Authorize(Policy = Permisos.PlanGestionar)]
    public async Task<ActionResult<IReadOnlyList<PagoPlanDto>>> MisPagos(CancellationToken ct) => Ok(await _planes.MisPagosAsync(ct));
}
