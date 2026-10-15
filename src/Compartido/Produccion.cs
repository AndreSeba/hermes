using System.Net;
using Hermes.Infrastructure.Persistence;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Hermes.Compartido;

// Lo que la API y el sitio público necesitan igual para correr en producción. Este archivo está ENLAZADO en los dos
// proyectos (`<Compile Include="..\Compartido\Produccion.cs" />`): un solo lugar, sin un proyecto más.
public static class Produccion
{
    // Logs con Serilog: en desarrollo legibles; en producción JSON de una línea (lo que entienden los recolectores de logs).
    // Si hay `Sentry:Dsn`, los errores (nivel Error o más) también van a Sentry. Sin DSN, Sentry no se activa.
    public static void ConfigurarLogs(this WebApplicationBuilder builder)
    {
        var dsn = builder.Configuration["Sentry:Dsn"];
        var desarrollo = builder.Environment.IsDevelopment();
        builder.Services.AddSerilog((servicios, log) =>
        {
            log.ReadFrom.Configuration(builder.Configuration)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Aplicacion", builder.Environment.ApplicationName);
            if (desarrollo) log.WriteTo.Console();
            else log.WriteTo.Console(new RenderedCompactJsonFormatter());
            if (!string.IsNullOrWhiteSpace(dsn))
                log.WriteTo.Sentry(o =>
                {
                    o.Dsn = dsn;
                    o.Environment = builder.Environment.EnvironmentName;
                    o.MinimumEventLevel = LogEventLevel.Error;
                    o.MinimumBreadcrumbLevel = LogEventLevel.Information;
                    o.SendDefaultPii = false; // nada de IPs ni datos de usuarios a un servicio de afuera
                });
        });
    }

    // Detrás de un proxy inverso (Caddy, Nginx, Cloudflare) la conexión llega desde el proxy: sin esto, el límite por IP
    // contaría a TODOS los clientes como una sola IP y la página se armaría con http:// en vez de https://.
    // Solo se confía en los proxies de `Proxy:Redes` (por ejemplo "172.16.0.0/12" para la red de Docker): si se confiara en
    // cualquiera, un cliente podría mandar su propio X-Forwarded-For y saltarse el límite por IP.
    public static void ConfigurarProxy(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            o.ForwardLimit = 1;
            foreach (var red in builder.Configuration.GetSection("Proxy:Redes").Get<string[]>() ?? [])
                o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(red));
            foreach (var ip in builder.Configuration.GetSection("Proxy:Ips").Get<string[]>() ?? [])
                o.KnownProxies.Add(IPAddress.Parse(ip));
            // El host que manda el proxy se acepta solo si está en AllowedHosts (el filtro de hosts de ASP.NET corre después).
        });
    }

    // Va PRIMERO en el pipeline (antes del límite por IP y de todo lo que mire la IP o el esquema).
    public static void UsarProxyYLogs(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseSerilogRequestLogging(o =>
        {
            // Sin la consulta (?…) en el log: puede traer datos de búsqueda de los clientes.
            o.MessageTemplate = "{RequestMethod} {RequestPath} respondió {StatusCode} en {Elapsed:0} ms";
            o.GetLevel = (contexto, _, ex) => ex is not null || contexto.Response.StatusCode >= 500 ? LogEventLevel.Error
                : contexto.Request.Path.StartsWithSegments("/salud") ? LogEventLevel.Verbose : LogEventLevel.Information;
        });
    }

    // Para el proxy y el monitoreo: 200 si la base responde, 503 si no. No dice nada más (ni versión ni datos).
    public static void MapearSalud(this WebApplication app)
        => app.MapGet("/salud", async (HermesDbContext db, CancellationToken ct) =>
        {
            try
            {
                return await db.Database.CanConnectAsync(ct) ? Results.Ok(new { estado = "ok" }) : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            catch (Exception)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        }).DisableRateLimiting();
}
