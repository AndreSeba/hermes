using Microsoft.Extensions.Caching.Memory;

namespace Hermes.Infrastructure.Security;

// 5 fallos por email en 10 minutos → 429 hasta que venza la ventana (que corre desde el primer
// fallo). Se cuenta también para emails inexistentes: si no, el bloqueo revelaría qué cuentas existen.
// Vive en memoria: con varias instancias de la API habría que pasarlo a una caché compartida.
public class BloqueoDeLogin
{
    public const int MaximoFallos = 5;
    public static readonly TimeSpan Ventana = TimeSpan.FromMinutes(10);

    private sealed record Fallos(int Cantidad, DateTimeOffset Vence);

    private readonly IMemoryCache _cache;
    private readonly TimeProvider _reloj;
    private readonly object _candado = new();

    public BloqueoDeLogin(IMemoryCache cache, TimeProvider reloj)
    {
        _cache = cache;
        _reloj = reloj;
    }

    private static string Clave(string email) => $"login-fallos:{email}";

    private Fallos? Vigentes(string email)
        => _cache.TryGetValue(Clave(email), out Fallos? f) && f!.Vence > _reloj.GetUtcNow() ? f : null;

    public bool EstaBloqueado(string email) => Vigentes(email) is { Cantidad: >= MaximoFallos };

    public void RegistrarFallo(string email)
    {
        lock (_candado)
        {
            var previos = Vigentes(email);
            var nuevos = previos is null
                ? new Fallos(1, _reloj.GetUtcNow().Add(Ventana))
                : previos with { Cantidad = previos.Cantidad + 1 };
            // La caché vence con SU reloj (el del sistema), así que solo se le da un plazo relativo
            // para limpiar; quien decide si la ventana sigue abierta es Vence, medido con _reloj.
            _cache.Set(Clave(email), nuevos, Ventana);
        }
    }

    public void Limpiar(string email) => _cache.Remove(Clave(email));
}
