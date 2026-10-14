using Hermes.Application.Dtos;
using Hermes.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hermes.Api.Controllers;

[ApiController]
[Route("api/v1/productos")]
[Authorize]
public class ProductosController : ControllerBase
{
    private readonly IProductoService _productoService;

    public ProductosController(IProductoService productoService) => _productoService = productoService;

    // Más nuevos primero, paginado por cursor: pasar `siguiente` de la respuesta como `cursor`.
    [HttpGet]
    public async Task<ActionResult<PaginaDeProductosDto>> Listar([FromQuery] FiltroProductosDto filtro, CancellationToken ct)
        => Ok(await _productoService.ListarAsync(filtro, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProductoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductoDto>> Obtener(Guid id, CancellationToken ct)
        => Ok(await _productoService.ObtenerAsync(id, ct));

    [HttpPost]
    [ProducesResponseType(typeof(ProductoDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductoDto>> Crear([FromBody] GuardarProductoDto dto, CancellationToken ct)
    {
        var creado = await _productoService.CrearAsync(dto, ct);
        return CreatedAtAction(nameof(Obtener), new { id = creado.Id }, creado);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ProductoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductoDto>> Actualizar(Guid id, [FromBody] GuardarProductoDto dto, CancellationToken ct)
        => Ok(await _productoService.ActualizarAsync(id, dto, ct));

    [HttpPost("{id:guid}/archivar")]
    [ProducesResponseType(typeof(ProductoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductoDto>> Archivar(Guid id, CancellationToken ct)
        => Ok(await _productoService.ArchivarAsync(id, true, ct));

    [HttpPost("{id:guid}/desarchivar")]
    [ProducesResponseType(typeof(ProductoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductoDto>> Desarchivar(Guid id, CancellationToken ct)
        => Ok(await _productoService.ArchivarAsync(id, false, ct));
}
