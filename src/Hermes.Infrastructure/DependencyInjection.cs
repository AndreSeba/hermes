using Hermes.Application.Interfaces;
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

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICategoriaService, CategoriaService>();

        return services;
    }
}
