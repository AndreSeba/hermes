using System.Threading.RateLimiting;
using Hermes.Application.Dtos;
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
    // Mandar pedidos: mucho más bajo (anti-spam). Por IP Y POR TIENDA: muchos celulares comparten la IP de la operadora
    // (CGNAT), y un límite solo por IP haría que los clientes de una tienda le gasten el cupo a los de otra.
    o.AddPolicy("pedidos", c => RateLimitPartition.GetFixedWindowLimiter(
        $"{c.Connection.RemoteIpAddress}|{(c.Request.RouteValues["slug"] as string)?.ToLowerInvariant()}",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(10) }));
});
// El carrito habla JSON con estas rutas: los enums viajan como texto ("Retiro", "Envio").
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

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

// ───────────── Carrito (vive en el navegador; acá se revisa y se manda) ─────────────
// Solo JSON: un formulario de otro sitio no puede mandar application/json sin CORS (y acá no hay CORS), así que no hace falta
// token antiforgery. Las respuestas no se guardan en caché (precio y disponibilidad de ahora).

app.MapPost("/{slug}/carrito/revisar", async (string slug, RevisarCarritoDto dto, IPublicoService publico, Urls urls, HttpContext http, CancellationToken ct) =>
{
    if (!Slugs.Valido(slug)) return Results.NotFound();
    http.Response.Headers.CacheControl = "no-store";
    // Las fotos se sirven desde donde dice la configuración (como en las páginas: Urls.Foto), no desde este sitio.
    return await Responder(() => publico.RevisarCarritoAsync(slug, dto, ct), r => Results.Ok(r with
    {
        Articulos = r.Articulos.Select(a => a.Foto is null ? a : a with
        {
            Foto = a.Foto with { Miniatura = urls.Foto(a.Foto.Miniatura), Catalogo = urls.Foto(a.Foto.Catalogo), Detalle = urls.Foto(a.Foto.Detalle) },
        }).ToList(),
    }));
});

app.MapPost("/{slug}/pedido", async (string slug, SolicitudDePedido s, IPublicoService publico, HttpContext http, CancellationToken ct) =>
{
    if (!Slugs.Valido(slug)) return Results.NotFound();
    http.Response.Headers.CacheControl = "no-store";
    // Campo trampa: invisible para una persona, los robots que llenan todo lo completan.
    if (!string.IsNullOrEmpty(s.Sitio)) return Results.Problem(detail: "No pudimos enviar tu pedido.", statusCode: StatusCodes.Status400BadRequest);
    var dto = new NuevoPedidoDto(s.ClaveIdempotencia, s.Articulos ?? [], s.Nombre ?? "", s.Celular ?? "", s.Entrega, s.Direccion, s.Referencia, s.Nota);
    return await Responder(() => publico.PedirAsync(slug, dto, ct), p => Results.Json(
        new { p.Numero, p.Total, p.Nuevo, Whatsapp = Formato.EnlaceWhatsapp(p.Whatsapp, Formato.MensajeDeCarrito(p)) },
        statusCode: p.Nuevo ? StatusCodes.Status201Created : StatusCodes.Status200OK));
}).RequireRateLimiting("pedidos");

app.MapRazorPages();

app.Run();

// Los errores esperables salen como ProblemDetails con el mensaje para mostrar (`detail`); el resto, 500 sin detalles.
static async Task<IResult> Responder<T>(Func<Task<T>> accion, Func<T, IResult> ok)
{
    try
    {
        return ok(await accion());
    }
    catch (DominioException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: ex.Status);
    }
}

record SolicitudDePedido(
    Guid ClaveIdempotencia, List<ArticuloPedidoDto>? Articulos, string? Nombre, string? Celular,
    Hermes.Domain.Entities.EntregaPedido Entrega, string? Direccion, string? Referencia, string? Nota, string? Sitio);

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
