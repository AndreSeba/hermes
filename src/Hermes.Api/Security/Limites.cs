using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Hermes.Api.Security;

// Límite de pedidos por IP. Complementa el bloqueo por email de BloqueoDeLogin: este frena a
// quien prueba muchos emails distintos desde la misma IP (y el spam de registros).
// Ojo: en Bolivia muchos celulares salen a internet por la MISMA IP de la operadora (CGNAT), así
// que los límites no pueden ser tan bajos como para que varios clientes reales se bloqueen entre sí.
public static class Limites
{
    // Login y registro: lo que se ataca por fuerza bruta.
    public const string Auth = "auth";
    // Renovación de sesión: la hace sola cada pestaña abierta (al cargar y cada ~14 min), no un
    // atacante adivinando; con el límite de Auth se agotaba con uso normal.
    public const string Renovacion = "renovacion";
    // La tienda pública: la ven clientes de verdad, muchos tras la misma IP de la operadora (CGNAT): límite alto.
    public const string Publico = "publico";

    public static IServiceCollection AddLimites(this IServiceCollection services)
        => services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(Auth, contexto => PorIp(contexto, 20));
            options.AddPolicy(Renovacion, contexto => PorIp(contexto, 120));
            options.AddPolicy(Publico, contexto => PorIp(contexto, 240));
        });

    private static RateLimitPartition<string> PorIp(HttpContext contexto, int porMinuto)
        => RateLimitPartition.GetFixedWindowLimiter(
            contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = porMinuto, Window = TimeSpan.FromMinutes(1) });
}
