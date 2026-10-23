using Hermes.Application.Dtos;
using Hermes.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Hermes.Domain.Seguridad;

namespace Hermes.Api.Controllers;

// El lector de publicaciones de Instagram por reglas y su bandeja de revisión (M11). Sin conexión con Meta todavía (M12):
// la dueña pega el texto de la publicación.
[ApiController]
[Route("api/v1/instagram")]
[Authorize]
public class InstagramController : ControllerBase
{
    private readonly IInstagramService _instagram;

    public InstagramController(IInstagramService instagram) => _instagram = instagram;

    // "Probá tu formato": lo que se entiende de un texto, sin guardar nada.
    [HttpPost("leer")]
    [Authorize(Policy = Permisos.InstagramGestionar)]
    [ProducesResponseType(typeof(LecturaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<LecturaDto> Leer([FromBody] LeerTextoDto dto) => Ok(_instagram.Leer(dto));

    // Deja lo entendido en la bandeja, pendiente de revisión. Nada llega al catálogo solo.
    [HttpPost("importar")]
    [Authorize(Policy = Permisos.InstagramGestionar)]
    [ProducesResponseType(typeof(IReadOnlyList<BorradorDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<BorradorDto>>> Importar([FromBody] LeerTextoDto dto, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await _instagram.ImportarAsync(dto, ct));

    [HttpGet("borradores")]
    [Authorize(Policy = Permisos.InstagramGestionar)]
    public async Task<ActionResult<PaginaDeBorradoresDto>> Listar([FromQuery] FiltroBorradoresDto filtro, CancellationToken ct)
        => Ok(await _instagram.ListarAsync(filtro, ct));

    [HttpGet("borradores/{id:guid}")]
    [Authorize(Policy = Permisos.InstagramGestionar)]
    [ProducesResponseType(typeof(BorradorDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BorradorDto>> Obtener(Guid id, CancellationToken ct)
        => Ok(await _instagram.ObtenerAsync(id, ct));

    // Una sola vez: crea el producto con lo que la dueña revisó.
    [HttpPost("borradores/{id:guid}/publicar")]
    [Authorize(Policy = Permisos.InstagramGestionar)]
    [ProducesResponseType(typeof(PublicacionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PublicacionDto>> Publicar(Guid id, [FromBody] PublicarBorradorDto dto, CancellationToken ct)
        => Ok(await _instagram.PublicarAsync(id, dto, ct));

    [HttpPost("borradores/{id:guid}/descartar")]
    [Authorize(Policy = Permisos.InstagramGestionar)]
    [ProducesResponseType(typeof(BorradorDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BorradorDto>> Descartar(Guid id, CancellationToken ct)
        => Ok(await _instagram.DescartarAsync(id, ct));
}
