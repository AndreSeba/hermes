using Hermes.Api.Security;
using Hermes.Application.Dtos;
using Hermes.Application.Interfaces;
using Hermes.Domain.Seguridad;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hermes.Api.Controllers;

[ApiController]
[Route("api/v1/inicio")]
[Authorize]
public class InicioController : ControllerBase
{
    private readonly IInicioService _inicio;

    public InicioController(IInicioService inicio) => _inicio = inicio;

    // El resumen del período (el front manda el mes de quien mira: Desde incluido, Hasta excluido).
    [HttpGet]
    [Authorize(Policy = Permisos.InicioVer)]
    public async Task<ActionResult<InicioDto>> Resumen([FromQuery] FiltroPeriodoDto periodo, CancellationToken ct)
        => Ok((await _inicio.ResumenAsync(periodo, ct)).SegunPermisos(User.Tiene));
}
