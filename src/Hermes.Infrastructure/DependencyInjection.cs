using Hermes.Application.Interfaces;
using Hermes.Infrastructure.Imagenes;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Security;
using Hermes.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.Infrastructure;

public static class DependencyInjection
{
    // ITiendaActual NO se registra acá: lo aporta quien hospeda (la API lo saca del token).
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("HermesDb")
            ?? throw new InvalidOperationException("Falta la cadena de conexión 'HermesDb' en la configuración.");

        services.AddDbContext<HermesDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migraciones", HermesDbContext.Esquema))
            .UseSnakeCaseNamingConvention());

        services.AddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<JwtTokenService>();
        services.AddSingleton<BloqueoDeLogin>();

        // Para pasar las fotos a Cloudflare R2 (docs/PLAN.md, M2/M14): otra implementación de
        // IAlmacenArchivos acá. Ningún servicio cambia.
        services.AddScoped<IAlmacenArchivos, AlmacenEnBaseDeDatos>();
        services.AddScoped<IImagenService, ImagenService>();

        services.AddScoped<IProductoService, ProductoService>();
        services.AddScoped<ITiendaService, TiendaService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICategoriaService, CategoriaService>();

        return services;
    }
}
