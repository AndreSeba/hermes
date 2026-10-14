using System.Security.Claims;

namespace Hermes.Api.Security;

public static class ClaimsPrincipalExtensions
{
    // "sub" llega como ClaimTypes.NameIdentifier por el mapeo por defecto de AddJwtBearer.
    // No "arreglarlo" con MapInboundClaims = false: rompe User.Identity.Name (trampa documentada en CMP).
    // Si falta el claim se lanza excepción, nunca se cae a un usuario inventado.
    public static Guid ObtenerUsuarioId(this ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)
            ? id
            : throw new InvalidOperationException("El token no identifica a un usuario.");

    public static Guid ObtenerTiendaId(this ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirst(TiendaActualDesdeToken.Claim)?.Value, out var id)
            ? id
            : throw new InvalidOperationException("El token no tiene tienda.");
}
