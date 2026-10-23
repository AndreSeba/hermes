using Hermes.Application.Dtos;
using Hermes.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Hermes.Domain.Seguridad;

namespace Hermes.Api.Controllers;

[ApiController]
[Route("api/v1/gastos")]
[Authorize]
public class GastosController : ControllerBase
{
    private readonly IGastoService _gastos;

    public GastosController(IGastoService gastos) => _gastos = gastos;

    // Más nuevos primero, por cursor. `Total` es de todo el período, no de la página.
    [HttpGet]
    [Authorize(Policy = Permisos.GastosVer)]
    public async Task<ActionResult<PaginaDeGastosDto>> Listar([FromQuery] FiltroGastosDto filtro, CancellationToken ct)
        => Ok(await _gastos.ListarAsync(filtro, ct));

    // 201 si se registró; 200 si esa ClaveIdempotencia ya había registrado el gasto (se devuelve el mismo).
    [HttpPost]
    [Authorize(Policy = Permisos.GastosGestionar)]
    [ProducesResponseType(typeof(GastoDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(GastoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GastoDto>> Registrar([FromBody] RegistrarGastoDto dto, CancellationToken ct)
    {
        var resultado = await _gastos.RegistrarAsync(dto, ct);
        return resultado.Creado ? StatusCode(StatusCodes.Status201Created, resultado.Gasto) : Ok(resultado.Gasto);
    }

    [HttpPost("{id:guid}/anular")]
    [Authorize(Policy = Permisos.GastosGestionar)]
    [ProducesResponseType(typeof(GastoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GastoDto>> Anular(Guid id, CancellationToken ct)
        => Ok(await _gastos.AnularAsync(id, ct));
}
