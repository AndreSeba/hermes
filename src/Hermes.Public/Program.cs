using System.Threading.RateLimiting;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Infrastructure;
using Hermes.Public;

// La tienda pública: páginas renderizadas EN EL SERVIDOR (Razor Pages), sin login. Es un proyecto aparte de la API para
// que el enlace que se comparte por WhatsApp tenga vista previa con foto (una SPA de React no la puede generar: el
// rastreador de WhatsApp no ejecuta JavaScript). Usa los mismos servicios (IPublicoService) que la API, sin pasar por HTTP.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
// Texto legible en el HTML ("Mía", no "M&#xED;a"): se sigue escapando todo lo peligroso (< > & " '), solo se deja de
// convertir en entidades las letras con tilde y la ñ. Importa para la vista previa y para quien lee el código fuente.
builder.Services.AddWebEncoders(o => o.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.BasicLatin, System.Text.Unicode.UnicodeRanges.Latin1Supplement, System.Text.Unicode.UnicodeRanges.LatinExtendedA, System.Text.Unicode.UnicodeRanges.GeneralPunctuation, System.Text.Unicode.UnicodeRanges.Arrows));
// Sin login no hay "tienda de la sesión": la fija PublicoService con el slug de la dirección (y el filtro global sigue
// acotando cada consulta a esa tienda).
builder.Services.AddScoped<ITiendaActual>(sp => sp.GetRequiredService<TiendaPublicaContexto>());
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<Urls>();

// Límite por IP, alto: muchos clientes de verdad comparten la IP de la operadora (CGNAT).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(c =>
        RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 240, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHttpsRedirection();
}
app.UseStatusCodePagesWithReExecute("/Error", "?codigo={0}");

app.Use(async (contexto, siguiente) =>
{
    contexto.Response.Headers.XContentTypeOptions = "nosniff";
    contexto.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    contexto.Response.Headers.XFrameOptions = "DENY";
    await siguiente();
});

app.UseStaticFiles();
app.UseRateLimiter();

// La imagen de la vista previa de WhatsApp: siempre JPEG (WhatsApp y otros rastreadores a veces no leen WEBP).
app.MapGet("/{slug}/og.jpg", async (string slug, Guid? p, IPublicoService publico, Urls urls, IAlmacenArchivos almacen, CancellationToken ct) =>
{
    if (!Slugs.Valido(slug)) return Results.NotFound();
    try
    {
        var bytes = await VistaPrevia.GenerarAsync(slug, p, publico, urls, almacen, ct);
        return (IResult)new JpegConCache(bytes);
    }
    catch (TiendaPublicaNoEncontradaException)
    {
        return Results.NotFound();
    }
});

app.MapRazorPages();

app.Run();

// El resultado con la caché de una hora puesta (la foto de una tienda casi no cambia).
sealed class JpegConCache(byte[] bytes) : IResult
{
    public async Task ExecuteAsync(HttpContext contexto)
    {
        contexto.Response.ContentType = "image/jpeg";
        contexto.Response.Headers.CacheControl = "public, max-age=3600";
        await contexto.Response.Body.WriteAsync(bytes);
    }
}
