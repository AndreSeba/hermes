using Hermes.Application.Interfaces;
using Hermes.Infrastructure.Imagenes;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Security;
using Hermes.Infrastructure.Stock;
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

        // Segunda barrera multi-tienda en Postgres (Row Level Security): ver Persistence/SeguridadPorFila.cs. Se enciende en
        // producción, donde la app entra con un rol sin privilegios; en desarrollo (superusuario) no cambiaría nada.
        var seguridadPorFila = configuration.GetValue<bool>("Base:SeguridadPorFila");
        services.AddDbContext<HermesDbContext>(options =>
        {
            HermesDbContext.Configurar(options, connectionString);
            if (seguridadPorFila) options.ConSeguridadPorFila();
        });

        services.AddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<JwtTokenService>();
        services.AddSingleton<BloqueoDeLogin>();

        // Dónde viven las fotos (Almacenamiento:Tipo): "Base" (tabla `archivos`, hoy) o "R2" (Cloudflare R2). Ningún servicio
        // cambia: todos usan IAlmacenArchivos. Para pasar a R2: comando `copiar-fotos-a-r2` y recién después cambiar el tipo.
        services.Configure<AlmacenamientoOptions>(configuration.GetSection(AlmacenamientoOptions.SectionName));
        var almacenamiento = configuration[$"{AlmacenamientoOptions.SectionName}:Tipo"] ?? "Base";
        if (almacenamiento.Equals("R2", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IClienteDeObjetos, ClienteR2>();
            services.AddScoped<IAlmacenArchivos, AlmacenEnObjetos>();
        }
        else if (almacenamiento.Equals("Base", StringComparison.OrdinalIgnoreCase))
            services.AddScoped<IAlmacenArchivos, AlmacenEnBaseDeDatos>();
        else
            throw new InvalidOperationException($"Almacenamiento:Tipo '{almacenamiento}' no existe: tiene que ser Base o R2.");
        services.AddScoped<IImagenService, ImagenService>();

        services.AddScoped<IOperacionesStock, OperacionesStock>();
        services.AddScoped<IStockService, StockService>();
        services.AddScoped<IVentaService, VentaService>();
        services.AddScoped<IPedidoService, PedidoService>();
        services.Configure<Planes.PlanesOptions>(configuration.GetSection(Planes.PlanesOptions.SectionName));
        services.Configure<Mantenimiento.LimpiezaOptions>(configuration.GetSection(Mantenimiento.LimpiezaOptions.SectionName));
        // La limpieza en sí; la tarea que la corre cada hora (LimpiezaPeriodica) la registra SOLO la API, no el sitio público.
        services.AddScoped<Mantenimiento.Limpieza>();
        services.AddScoped<Planes.PlanesDeTienda>();
        services.AddScoped<ILimitesDePlan, Planes.LimitesDePlan>();
        services.AddScoped<IPlanService, Planes.PlanService>();
        services.AddScoped<IAdminService, Planes.AdminService>();
        services.AddScoped<IInstagramService, InstagramService>();
        // M12: la conexión con la cuenta de Instagram (Meta). Sin la sección "Instagram" configurada queda apagada.
        services.Configure<Instagram.InstagramOptions>(configuration.GetSection(Instagram.InstagramOptions.SectionName));
        services.AddSingleton<Instagram.SecretosInstagram>();
        services.AddHttpClient<Instagram.IClienteInstagram, Instagram.ClienteInstagramGraph>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<IInstagramConexionService, Instagram.InstagramConexionService>();
        services.AddScoped<TiendaPublicaContexto>();
        services.AddScoped<IPublicoService, PublicoService>();
        services.AddScoped<IGastoService, GastoService>();
        services.AddScoped<IInicioService, InicioService>();
        services.AddScoped<ICosteoService, CosteoService>();
        services.AddScoped<IProveedorService, ProveedorService>();
        services.AddScoped<ICompraService, CompraService>();
        services.AddScoped<IProductoService, ProductoService>();
        services.AddScoped<ITiendaService, TiendaService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICategoriaService, CategoriaService>();

        return services;
    }
}
