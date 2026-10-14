using System.Text;
using Hermes.Api.Middleware;
using Hermes.Api.Security;
using Hermes.Application.Interfaces;
using Hermes.Infrastructure;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITiendaActual, TiendaActualDesdeToken>();
builder.Services.AddInfrastructure(builder.Configuration);

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
    });
builder.Services.AddAuthorization();
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

app.UseExceptionHandler(_ => { });

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title = "Hermes API";
    });
}

// En desarrollo el front (Vite) llama por HTTP a través de su proxy: así no hace falta
// confiar en el certificado de desarrollo. En producción HTTPS es obligatorio.
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseCors("Web");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();

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
