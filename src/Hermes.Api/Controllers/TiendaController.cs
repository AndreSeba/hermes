using Hermes.Application.Dtos;
using Hermes.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hermes.Api.Controllers;

// "La tienda" es siempre la de la sesión (claim `tienda` del token): no hay rutas con id de tienda.
[ApiController]
[Route("api/v1/tienda")]
[Authorize]
public class TiendaController : ControllerBase
{
    private readonly ITiendaService _tiendaService;

    public TiendaController(ITiendaService tiendaService) => _tiendaService = tiendaService;

    [HttpGet]
    public async Task<ActionResult<TiendaDto>> Obtener(CancellationToken ct)
        => Ok(await _tiendaService.ObtenerAsync(ct));

    [HttpPut]
    [ProducesResponseType(typeof(TiendaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TiendaDto>> Actualizar([FromBody] ActualizarTiendaDto dto, CancellationToken ct)
        => Ok(await _tiendaService.ActualizarAsync(dto, ct));

    [HttpPut("direccion")]
    [ProducesResponseType(typeof(TiendaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TiendaDto>> CambiarDireccion([FromBody] CambiarDireccionDto dto, CancellationToken ct)
        => Ok(await _tiendaService.CambiarDireccionAsync(dto, ct));
}
