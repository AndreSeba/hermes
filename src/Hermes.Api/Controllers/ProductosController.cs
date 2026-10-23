using Hermes.Api.Security;
using Hermes.Application.Dtos;
using Hermes.Application.Interfaces;
using Hermes.Domain.Seguridad;
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

    private bool VeCostos => User.Tiene(Permisos.CostosVer);

    // Más nuevos primero, paginado por cursor: pasar `siguiente` de la respuesta como `cursor`.
    [HttpGet]
    [Authorize(Policy = Permisos.ProductosVer)]
    public async Task<ActionResult<PaginaDeProductosDto>> Listar([FromQuery] FiltroProductosDto filtro, CancellationToken ct)
    {
        // "Revisar precio" filtra por margen: sin ver costos no se puede usar (si no, el filtro mostraría el margen).
        if (!VeCostos) filtro = filtro with { RevisarPrecio = false };
        return Ok((await _productoService.ListarAsync(filtro, ct)).Costos(VeCostos));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permisos.ProductosVer)]
    [ProducesResponseType(typeof(ProductoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductoDto>> Obtener(Guid id, CancellationToken ct)
        => Ok((await _productoService.ObtenerAsync(id, ct)).Costos(VeCostos));

    [HttpPost]
    [Authorize(Policy = Permisos.ProductosCrear)]
    [ProducesResponseType(typeof(ProductoDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductoDto>> Crear([FromBody] GuardarProductoDto dto, CancellationToken ct)
    {
        var creado = await _productoService.CrearAsync(dto, ct);
        return CreatedAtAction(nameof(Obtener), new { id = creado.Id }, creado.Costos(VeCostos));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permisos.ProductosEditar)]
    [ProducesResponseType(typeof(ProductoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductoDto>> Actualizar(Guid id, [FromBody] GuardarProductoDto dto, CancellationToken ct)
        => Ok((await _productoService.ActualizarAsync(id, dto, ct)).Costos(VeCostos));

    [HttpPost("{id:guid}/archivar")]
    [Authorize(Policy = Permisos.ProductosArchivar)]
    [ProducesResponseType(typeof(ProductoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductoDto>> Archivar(Guid id, CancellationToken ct)
        => Ok((await _productoService.ArchivarAsync(id, true, ct)).Costos(VeCostos));

    [HttpPost("{id:guid}/desarchivar")]
    [Authorize(Policy = Permisos.ProductosArchivar)]
    [ProducesResponseType(typeof(ProductoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductoDto>> Desarchivar(Guid id, CancellationToken ct)
        => Ok((await _productoService.ArchivarAsync(id, false, ct)).Costos(VeCostos));
}
