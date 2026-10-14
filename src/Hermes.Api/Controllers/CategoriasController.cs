using Hermes.Application.Dtos;
using Hermes.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hermes.Api.Controllers;

[ApiController]
[Route("api/v1/categorias")]
[Authorize]
public class CategoriasController : ControllerBase
{
    private readonly ICategoriaService _categoriaService;

    public CategoriasController(ICategoriaService categoriaService) => _categoriaService = categoriaService;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CategoriaDto>>> Listar([FromQuery] bool incluirInactivas, CancellationToken ct)
        => Ok(await _categoriaService.ListarAsync(incluirInactivas, ct));

    [HttpPost]
    [ProducesResponseType(typeof(CategoriaDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoriaDto>> Crear([FromBody] CrearCategoriaDto dto, CancellationToken ct)
    {
        var creada = await _categoriaService.CrearAsync(dto, ct);
        return CreatedAtAction(nameof(Listar), creada);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CategoriaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoriaDto>> Actualizar(Guid id, [FromBody] ActualizarCategoriaDto dto, CancellationToken ct)
        => Ok(await _categoriaService.ActualizarAsync(id, dto, ct));
}
