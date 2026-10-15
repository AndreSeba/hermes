using Hermes.Application.Dtos;
using Hermes.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hermes.Api.Controllers;

// M12: la cuenta de Instagram de la tienda. El token de Meta nunca sale de acá.
[ApiController]
[Route("api/v1/instagram/conexion")]
[Authorize]
public class InstagramConexionController : ControllerBase
{
    private readonly IInstagramConexionService _conexion;

    public InstagramConexionController(IInstagramConexionService conexion) => _conexion = conexion;

    // Configurada = la plataforma tiene una app de Meta; Conectada = esta tienda conectó su cuenta.
    [HttpGet]
    public async Task<ActionResult<InstagramConexionDto>> Estado(CancellationToken ct) => Ok(await _conexion.EstadoAsync(ct));

    // Adonde mandar a la dueña para que autorice (Instagram vuelve a Instagram:RedirectUri con ?code=…&state=…).
    [HttpGet("url")]
    [ProducesResponseType(typeof(UrlConexionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<UrlConexionDto> DireccionParaConectar() => Ok(_conexion.Url());

    [HttpPost]
    [ProducesResponseType(typeof(InstagramConexionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InstagramConexionDto>> Conectar([FromBody] ConectarInstagramDto dto, CancellationToken ct)
        => Ok(await _conexion.ConectarAsync(dto, ct));

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Desconectar(CancellationToken ct)
    {
        await _conexion.DesconectarAsync(ct);
        return NoContent();
    }

    // "Sincronizar ahora": trae las publicaciones nuevas a la bandeja.
    [HttpPost("sincronizar")]
    [ProducesResponseType(typeof(SincronizacionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SincronizacionDto>> Sincronizar(CancellationToken ct) => Ok(await _conexion.SincronizarAsync(ct));
}
