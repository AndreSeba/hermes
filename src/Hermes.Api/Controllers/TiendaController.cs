using Hermes.Application.Dtos;
using Hermes.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Hermes.Domain.Seguridad;

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
    [Authorize(Policy = Permisos.TiendaGestionar)]
    [ProducesResponseType(typeof(TiendaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TiendaDto>> Actualizar([FromBody] ActualizarTiendaDto dto, CancellationToken ct)
        => Ok(await _tiendaService.ActualizarAsync(dto, ct));

    // "Qué ven tus clientes" y el fondo de la tienda pública.
    [HttpGet("publica")]
    [Authorize(Policy = Permisos.TiendaGestionar)]
    public async Task<ActionResult<ConfiguracionPublicaDto>> ObtenerPublica(CancellationToken ct)
        => Ok(await _tiendaService.ObtenerConfiguracionPublicaAsync(ct));

    [HttpPut("publica")]
    [Authorize(Policy = Permisos.TiendaGestionar)]
    [ProducesResponseType(typeof(ConfiguracionPublicaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ConfiguracionPublicaDto>> GuardarPublica([FromBody] ConfiguracionPublicaDto dto, CancellationToken ct)
        => Ok(await _tiendaService.GuardarConfiguracionPublicaAsync(dto, ct));

    [HttpPut("direccion")]
    [Authorize(Policy = Permisos.TiendaGestionar)]
    [ProducesResponseType(typeof(TiendaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TiendaDto>> CambiarDireccion([FromBody] CambiarDireccionDto dto, CancellationToken ct)
        => Ok(await _tiendaService.CambiarDireccionAsync(dto, ct));
}
