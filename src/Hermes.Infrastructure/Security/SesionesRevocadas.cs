using Hermes.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace Hermes.Infrastructure.Security;

// "Las sesiones de esta persona valen desde ahora": un token de acceso emitido ANTES de ese instante deja de servir y el
// front renueva (recibe los permisos de hoy, o se cierra si ya no tiene acceso). Se usa al cambiar un rol, desactivar a alguien
// o restablecer su contraseña, para que no pasen hasta 15 minutos con los permisos de antes.
//
// El instante de emisión viaja en el claim "emi" (milisegundos): el estándar (nbf) solo llega al segundo, y un token emitido
// en el mismo segundo de la revocación quedaría vivo sin poder distinguirlo. Vive en memoria (con varias instancias de la API
// habría que pasarlo a una caché compartida, igual que BloqueoDeLogin).
public class SesionesRevocadas : ISesionesRevocadas
{
    // Más que la vida de un token de acceso (15 min + 1 de tolerancia): pasado ese tiempo ningún token viejo sigue vivo.
    private static readonly TimeSpan Retencion = TimeSpan.FromMinutes(30);

    private readonly IMemoryCache _cache;
    private readonly TimeProvider _reloj;

    public SesionesRevocadas(IMemoryCache cache, TimeProvider reloj)
    {
        _cache = cache;
        _reloj = reloj;
    }

    private static string Clave(Guid usuarioId) => $"sesiones-revocadas:{usuarioId}";

    public void Revocar(Guid usuarioId) => _cache.Set(Clave(usuarioId), _reloj.GetUtcNow(), Retencion);

    public bool Invalida(Guid usuarioId, DateTimeOffset emitidoEn)
        => _cache.TryGetValue(Clave(usuarioId), out DateTimeOffset corte) && emitidoEn < corte;
}
