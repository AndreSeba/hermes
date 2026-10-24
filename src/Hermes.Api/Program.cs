using System.Text;
using Hermes.Api.Middleware;
using Hermes.Api.Security;
using Hermes.Compartido;
using Hermes.Application.Interfaces;
using Hermes.Infrastructure;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.ConfigurarLogs();
builder.ConfigurarProxy();

// JSON de la API, igual para los controllers (MVC) y para el generador de OpenAPI (que lee las opciones
// "http", otras): si solo se configura uno, el esquema que ve el front no coincide con lo que viaja.
//  - Los enums van como texto ("Pierde", no 1) → el front los recibe como unión de strings.
//  - Números estrictos: sin esto el esquema dice `number | string` en cada monto y cantidad, porque
//    por defecto la API aceptaría "180" (texto) donde se espera 180.
static void ConfigurarJson(System.Text.Json.JsonSerializerOptions o)
{
    o.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    o.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict;
}
builder.Services.ConfigureHttpJsonOptions(o => ConfigurarJson(o.SerializerOptions));
builder.Services.AddControllers().AddJsonOptions(o => ConfigurarJson(o.JsonSerializerOptions));
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITiendaActual, TiendaActualDesdeToken>();
builder.Services.AddScoped<IUsuarioActual, UsuarioActualDesdeToken>();
builder.Services.AddInfrastructure(builder.Configuration);
// Refresh tokens vencidos y fotos que nadie usa, cada hora (Limpieza:*). Solo acá: el sitio público no la corre.
builder.Services.AddHostedService<Hermes.Infrastructure.Mantenimiento.LimpiezaPeriodica>();
// Publicaciones nuevas de Instagram a la bandeja, cada Instagram:IntervaloMinutos (solo si hay una app de Meta configurada).
builder.Services.AddHostedService<Hermes.Infrastructure.Instagram.SincronizacionInstagramPeriodica>();

builder.Services.AddExceptionHandler<ManejadorGlobalDeExcepciones>();
builder.Services.AddProblemDetails();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Falta la sección 'Jwt' en la configuración.");
if (jwt.SecretKey.Length < 32)
    throw new InvalidOperationException("Falta 'Jwt:SecretKey' (mínimo 32 caracteres). En desarrollo: dotnet user-secrets set \"Jwt:SecretKey\" \"...\" --project src/Hermes.Api");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SecretKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
        // Un token todavía vigente deja de servir si a esa persona le cambiaron el rol, la desactivaron o le restablecieron la
        // contraseña DESPUÉS de emitirlo: el front recibe 401, renueva y obtiene los permisos de ahora.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = contexto =>
            {
                var revocadas = contexto.HttpContext.RequestServices.GetRequiredService<ISesionesRevocadas>();
                var principal = contexto.Principal;
                if (Guid.TryParse(principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var usuarioId))
                {
                    // El instante exacto de emisión (ms) va en un claim propio; sin él (tokens viejos) vale el nbf, al segundo.
                    var emitido = long.TryParse(principal!.FindFirst(JwtTokenService.ClaimEmitido)?.Value, out var ms)
                        ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
                        : new DateTimeOffset(contexto.SecurityToken.ValidFrom, TimeSpan.Zero);
                    if (revocadas.Invalida(usuarioId, emitido))
                        contexto.Fail("La sesión se renovó: los permisos cambiaron.");
                }
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization(o => o.AgregarPoliticas());
// Los permisos (policy = código de permiso) los resuelve este proveedor: no hace falta registrar uno por uno.
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider, PermisoPolicyProvider>();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, PermisoAuthorizationHandler>();
builder.Services.AddLimites();

// CORS con lista explícita de orígenes (nunca AllowAnyOrigin) — el frontend React vive en
// otro repo (../Hermes.Web) y otro puerto.
var origenesPermitidos = builder.Configuration.GetSection("CorsOrigenesPermitidos").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Web", policy =>
    {
        policy.WithOrigins(origenesPermitidos)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Comandos de administración (se corren en el servidor, sin levantar la API):
//   dotnet run --project src/Hermes.Api -- promover-admin <email>   /   quitar-admin <email>
// El rol de administrador NUNCA se da desde la web: sin verificación de email, "admin por email" sería una puerta abierta.
if (args is ["promover-admin" or "quitar-admin", var emailAdmin])
{
    await using var ambito = app.Services.CreateAsyncScope();
    var contexto = ambito.ServiceProvider.GetRequiredService<HermesDbContext>();
    var correo = emailAdmin.Trim().ToLowerInvariant();
    var usuario = await contexto.Usuarios.FirstOrDefaultAsync(u => u.Email == correo);
    if (usuario is null)
    {
        Console.Error.WriteLine($"No existe ninguna cuenta con el email {correo}. Primero hay que registrarla.");
        return 1;
    }
    usuario.EsAdministrador = args[0] == "promover-admin";
    await contexto.SaveChangesAsync();
    Console.WriteLine(usuario.EsAdministrador
        ? $"{correo} ahora es administrador. Tiene que volver a iniciar sesión para que el token lo incluya."
        : $"{correo} ya no es administrador.");
    return 0;
}

// Pasar las fotos de la base a Cloudflare R2, una vez (con Almacenamiento:R2 configurado):
//   dotnet Hermes.Api.dll copiar-fotos-a-r2      (después cambiar Almacenamiento:Tipo a R2 y reiniciar)
if (args is ["copiar-fotos-a-r2"])
{
    await using var ambito = app.Services.CreateAsyncScope();
    var opciones = Microsoft.Extensions.Options.Options.Create(app.Configuration.GetSection("Almacenamiento").Get<Hermes.Infrastructure.Imagenes.AlmacenamientoOptions>() ?? new());
    using var cliente = new Hermes.Infrastructure.Imagenes.ClienteR2(opciones);
    var total = await Hermes.Infrastructure.Imagenes.AlmacenEnObjetos.CopiarDesdeLaBaseAsync(
        ambito.ServiceProvider.GetRequiredService<HermesDbContext>(), cliente, n => Console.WriteLine($"{n} archivos copiados…"), CancellationToken.None);
    Console.WriteLine($"Listo: {total} archivos en R2. Ahora cambiá Almacenamiento:Tipo a R2 y reiniciá la API y el sitio público.");
    return 0;
}

app.UsarProxyYLogs();
app.UseExceptionHandler(_ => { });

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title = "Mi Casero API";
    });
}

// En desarrollo el front (Vite) llama por HTTP a través de su proxy: así no hace falta
// confiar en el certificado de desarrollo. En producción HTTPS es obligatorio.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors("Web");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapearSalud();

// En producción (una sola instancia de la API) las migraciones se aplican al arrancar si `Base:MigrarAlArrancar` = true:
// así desplegar es solo levantar la imagen nueva. Con varias instancias, apagarlo y migrar aparte antes de desplegar.
// Con Row Level Security la app entra con un rol sin privilegios que no puede cambiar el esquema: las migraciones van con el
// DUEÑO de las tablas (`ConnectionStrings:HermesDbMigraciones`). Sin esa cadena, con la misma de la app.
if (app.Configuration.GetValue<bool>("Base:MigrarAlArrancar"))
{
    var cadena = app.Configuration.GetConnectionString("HermesDbMigraciones") ?? app.Configuration.GetConnectionString("HermesDb")!;
    var opciones = (DbContextOptionsBuilder<HermesDbContext>)HermesDbContext.Configurar(new DbContextOptionsBuilder<HermesDbContext>(), cadena);
    await using var db = new HermesDbContext(opciones.Options, new SinTienda());
    await db.Database.MigrateAsync();
}

// Si el código es más nuevo que la base, avisarlo al arrancar ahorra buscar el problema en otro lado.
try
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<HermesDbContext>();
    var pendientes = (await db.Database.GetPendingMigrationsAsync()).ToList();
    if (pendientes.Count > 0)
        app.Logger.LogWarning(
            "La base tiene {Cantidad} migración(es) sin aplicar: {Migraciones}. Ejecutá: dotnet ef database update --project src/Hermes.Infrastructure --startup-project src/Hermes.Api",
            pendientes.Count, string.Join(", ", pendientes));
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "No se pudo verificar si hay migraciones pendientes (¿Postgres está apagado?).");
}

app.Run();
return 0;

// Las migraciones no son de ninguna tienda.
sealed class SinTienda : ITiendaActual
{
    public Guid? TiendaId => null;
}
