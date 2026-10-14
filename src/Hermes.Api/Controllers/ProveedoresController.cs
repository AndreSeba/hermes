using Hermes.Application.Dtos;
using Hermes.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hermes.Api.Controllers;

[ApiController]
[Route("api/v1/proveedores")]
[Authorize]
public class ProveedoresController : ControllerBase
{
    private readonly IProveedorService _proveedores;

    public ProveedoresController(IProveedorService proveedores) => _proveedores = proveedores;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProveedorDto>>> Listar([FromQuery] bool incluirInactivos, CancellationToken ct)
        => Ok(await _proveedores.ListarAsync(incluirInactivos, ct));

    // Con lo último que se le pagó por cada producto.
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProveedorDetalleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProveedorDetalleDto>> Obtener(Guid id, CancellationToken ct)
        => Ok(await _proveedores.ObtenerAsync(id, ct));

    [HttpPost]
    [ProducesResponseType(typeof(ProveedorDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProveedorDto>> Crear([FromBody] GuardarProveedorDto dto, CancellationToken ct)
    {
        var creado = await _proveedores.CrearAsync(dto, ct);
        return CreatedAtAction(nameof(Obtener), new { id = creado.Id }, creado);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ProveedorDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProveedorDto>> Actualizar(Guid id, [FromBody] GuardarProveedorDto dto, CancellationToken ct)
        => Ok(await _proveedores.ActualizarAsync(id, dto, ct));

    [HttpPost("{id:guid}/desactivar")]
    public async Task<ActionResult<ProveedorDto>> Desactivar(Guid id, CancellationToken ct)
        => Ok(await _proveedores.CambiarActivoAsync(id, false, ct));

    [HttpPost("{id:guid}/activar")]
    public async Task<ActionResult<ProveedorDto>> Activar(Guid id, CancellationToken ct)
        => Ok(await _proveedores.CambiarActivoAsync(id, true, ct));
}
