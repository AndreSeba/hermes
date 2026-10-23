using Hermes.Application.Dtos;
using Hermes.Application.Interfaces;
using Hermes.Domain.Seguridad;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hermes.Api.Controllers;

// Las personas que trabajan en el negocio. El dueño crea la cuenta (correo y contraseña provisoria) y le asigna un rol.
[ApiController]
[Route("api/v1/equipo")]
[Authorize(Policy = Permisos.EquipoGestionar)]
public class EquipoController : ControllerBase
{
    private readonly IEquipoService _equipo;

    public EquipoController(IEquipoService equipo) => _equipo = equipo;

    // El dueño primero, después el equipo (los activos primero), con cuántas personas admite el plan.
    [HttpGet]
    public async Task<ActionResult<EquipoDto>> Listar(CancellationToken ct)
        => Ok(await _equipo.ListarAsync(ct));

    [HttpPost]
    [ProducesResponseType(typeof(MiembroDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status402PaymentRequired)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MiembroDto>> Crear([FromBody] CrearMiembroDto dto, CancellationToken ct)
    {
        var creado = await _equipo.CrearAsync(dto, ct);
        return CreatedAtAction(nameof(Listar), creado);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(MiembroDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status402PaymentRequired)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MiembroDto>> Actualizar(Guid id, [FromBody] ActualizarMiembroDto dto, CancellationToken ct)
        => Ok(await _equipo.ActualizarAsync(id, dto, ct));

    // Una contraseña provisoria nueva: la persona elige la suya al entrar y se le cierran las sesiones abiertas.
    [HttpPost("{id:guid}/password")]
    [ProducesResponseType(typeof(MiembroDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MiembroDto>> RestablecerPassword(Guid id, [FromBody] RestablecerPasswordDto dto, CancellationToken ct)
        => Ok(await _equipo.RestablecerPasswordAsync(id, dto, ct));
}
