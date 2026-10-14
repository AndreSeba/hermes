using Hermes.Api.Security;
using Hermes.Application.Dtos;
using Hermes.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hermes.Api.Controllers;

// La tienda pública en JSON, SIN login. Los DTOs son propios y no tienen dónde poner costo, margen ni stock exacto.
// Límite por IP (alto: muchos clientes comparten la IP de la operadora) y caché corta: lo que se ve puede tardar hasta
// 30 s en reflejar un cambio, a cambio de no ir a la base en cada visita.
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(Limites.Publico)]
[Route("public/{slug}")]
[ResponseCache(Duration = 30, Location = ResponseCacheLocation.Any)]
public class PublicoController : ControllerBase
{
    private readonly IPublicoService _publico;

    public PublicoController(IPublicoService publico) => _publico = publico;

    [HttpGet]
    [ProducesResponseType(typeof(TiendaPublicaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TiendaPublicaDto>> Tienda(string slug, CancellationToken ct)
        => Ok(await _publico.ObtenerTiendaAsync(slug, ct));

    [HttpGet("productos")]
    public async Task<ActionResult<PaginaPublicaDto>> Productos(string slug, [FromQuery] FiltroPublicoDto filtro, CancellationToken ct)
        => Ok(await _publico.ProductosAsync(slug, filtro, ct));

    [HttpGet("productos/{id:guid}")]
    [ProducesResponseType(typeof(ProductoPublicoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductoPublicoDto>> Producto(string slug, Guid id, CancellationToken ct)
        => Ok(await _publico.ProductoAsync(slug, id, ct));
}
