using Hermes.Application.Dtos;
using Hermes.Application.Interfaces;
using Hermes.Domain.Seguridad;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hermes.Api.Controllers;

// Los roles del negocio y sus permisos. La primera vez que se pide la lista, el negocio recibe los tres roles por defecto.
[ApiController]
[Route("api/v1/roles")]
[Authorize]
public class RolesController : ControllerBase
{
    private readonly IRolService _roles;

    public RolesController(IRolService roles) => _roles = roles;

    // Trae también el catálogo de permisos (para armar la pantalla). Quien arma el equipo necesita ver los roles para elegir uno.
    [HttpGet]
    [Authorize(Policy = $"{Permisos.RolesGestionar}|{Permisos.EquipoGestionar}")]
    public async Task<ActionResult<RolesDto>> Listar([FromQuery] bool incluirInactivos, CancellationToken ct)
        => Ok(await _roles.ListarAsync(incluirInactivos, ct));

    [HttpPost]
    [Authorize(Policy = Permisos.RolesGestionar)]
    [ProducesResponseType(typeof(RolDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RolDto>> Crear([FromBody] GuardarRolDto dto, CancellationToken ct)
    {
        var creado = await _roles.CrearAsync(dto, ct);
        return CreatedAtAction(nameof(Listar), creado);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permisos.RolesGestionar)]
    [ProducesResponseType(typeof(RolDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RolDto>> Actualizar(Guid id, [FromBody] GuardarRolDto dto, CancellationToken ct)
        => Ok(await _roles.ActualizarAsync(id, dto, ct));
}
