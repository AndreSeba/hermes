using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Hermes.Api.Security;

// Límite de pedidos por IP. Complementa el bloqueo por email de BloqueoDeLogin: este frena a
// quien prueba muchos emails distintos desde la misma IP (y el spam de registros).
public static class Limites
{
    public const string Auth = "auth";

    public static IServiceCollection AddLimites(this IServiceCollection services)
        => services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(Auth, contexto => RateLimitPartition.GetFixedWindowLimiter(
                contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
        });
}
