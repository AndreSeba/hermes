using Hermes.Application.Dtos;

namespace Hermes.Application.Interfaces;

// Quién es la persona que hace el pedido y qué puede hacer. En la API sale del token; en las pruebas se fija a mano.
// Los servicios que no la reciben (null) se comportan como "sin restricciones".
public interface IUsuarioActual
{
    Guid? UsuarioId { get; }
    bool Tiene(string permiso);
}

// Las sesiones abiertas de una persona dejan de valer de inmediato cuando se le cambia el rol, se la desactiva o se edita su
// rol (el token de acceso dura 15 minutos y mientras tanto llevaría los permisos de antes). El front renueva y recibe los nuevos.
// Vive en memoria (con varias instancias de la API habría que pasarlo a una caché compartida).
public interface ISesionesRevocadas
{
    void Revocar(Guid usuarioId);
    // ¿Un token válido desde esta fecha quedó invalidado?
    bool Invalida(Guid usuarioId, DateTimeOffset validoDesde);
}

public interface IRolService
{
    // Con el catálogo de permisos para armar la pantalla. La primera vez que se abre crea los roles por defecto.
    Task<RolesDto> ListarAsync(bool incluirInactivos, CancellationToken ct);
    Task<RolDto> CrearAsync(GuardarRolDto dto, CancellationToken ct);
    Task<RolDto> ActualizarAsync(Guid id, GuardarRolDto dto, CancellationToken ct);
}

public interface IEquipoService
{
    Task<EquipoDto> ListarAsync(CancellationToken ct);
    Task<MiembroDto> CrearAsync(CrearMiembroDto dto, CancellationToken ct);
    Task<MiembroDto> ActualizarAsync(Guid id, ActualizarMiembroDto dto, CancellationToken ct);
    Task<MiembroDto> RestablecerPasswordAsync(Guid id, RestablecerPasswordDto dto, CancellationToken ct);
}
