using System.Security.Claims;
using Hermes.Application.Interfaces;
using Hermes.Infrastructure.Security;

namespace Hermes.Api.Security;

// Quién hace el pedido y qué puede hacer, según el token. Sin request (tareas en segundo plano) no hay nadie: UsuarioId es null
// y no tiene ningún permiso.
public class UsuarioActualDesdeToken : IUsuarioActual
{
    private readonly IHttpContextAccessor _http;

    public UsuarioActualDesdeToken(IHttpContextAccessor http) => _http = http;

    private ClaimsPrincipal? Usuario => _http.HttpContext?.User;

    public Guid? UsuarioId => Guid.TryParse(Usuario?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    public bool Tiene(string permiso) => Usuario?.HasClaim(JwtTokenService.ClaimPermiso, permiso) ?? false;
}

public static class PermisosDelToken
{
    // Para los controllers: `User.Tiene(Permisos.CostosVer)`.
    public static bool Tiene(this ClaimsPrincipal user, string permiso) => user.HasClaim(JwtTokenService.ClaimPermiso, permiso);
}
