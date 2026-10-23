using Hermes.Application.Dtos;
using Hermes.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Hermes.Domain.Seguridad;

namespace Hermes.Api.Controllers;

// El costeo de un producto y su precio sugerido. El sugerido se calcula, nunca se guarda ni cambia el precio.
[ApiController]
[Route("api/v1/productos/{productoId:guid}/costeo")]
[Authorize]
public class CosteoController : ControllerBase
{
    private readonly ICosteoService _costeo;

    public CosteoController(ICosteoService costeo) => _costeo = costeo;

    [HttpGet]
    [Authorize(Policy = Permisos.CostosVer)]
    [ProducesResponseType(typeof(CosteoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CosteoDto>> Obtener(Guid productoId, CancellationToken ct)
        => Ok(await _costeo.ObtenerAsync(productoId, ct));

    // La calculadora: cuenta sin guardar.
    [HttpPost("calcular")]
    [Authorize(Policy = Permisos.CostosVer)]
    public async Task<ActionResult<CosteoDto>> Calcular(Guid productoId, [FromBody] GuardarCosteoDto dto, CancellationToken ct)
        => Ok(await _costeo.CalcularAsync(productoId, dto, ct));

    [HttpPut]
    [Authorize(Policy = Permisos.CostosEditar)]
    [ProducesResponseType(typeof(CosteoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CosteoDto>> Guardar(Guid productoId, [FromBody] GuardarCosteoDto dto, CancellationToken ct)
        => Ok(await _costeo.GuardarAsync(productoId, dto, ct));

    [HttpDelete]
    [Authorize(Policy = Permisos.CostosEditar)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Quitar(Guid productoId, CancellationToken ct)
    {
        await _costeo.QuitarAsync(productoId, ct);
        return NoContent();
    }
}
