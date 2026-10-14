using System.Text.RegularExpressions;
using Hermes.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hermes.Api.Controllers;

// Sirve las fotos SIN login: son las de la tienda pública. La protección es que la clave lleva dos
// GUID v7 (tienda e imagen) imposibles de adivinar, y que solo se entrega lo que tenga la forma de
// una foto procesada. Cuando las fotos pasen a Cloudflare R2 esta ruta deja de usarse (las urls
// apuntan al subdominio de fotos) y se puede quitar.
[ApiController]
[AllowAnonymous]
public partial class ArchivosController : ControllerBase
{
    private readonly IAlmacenArchivos _almacen;

    public ArchivosController(IAlmacenArchivos almacen) => _almacen = almacen;

    [GeneratedRegex(@"^[0-9a-f-]{36}/imagenes/[0-9a-f-]{36}/(miniatura|catalogo|detalle)\.webp$")]
    private static partial Regex FormaDeClave();

    [HttpGet("/archivos/{**clave}")]
    [ResponseCache(Duration = 31536000, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Obtener(string clave, CancellationToken ct)
    {
        if (!FormaDeClave().IsMatch(clave)) return NotFound();

        var archivo = await _almacen.ObtenerAsync(clave, ct);
        if (archivo is null) return NotFound();

        // La clave nunca cambia de contenido (una foto nueva es otra clave): se puede cachear un
        // año y el navegador ni vuelve a preguntar.
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(archivo.Datos, archivo.ContentType);
    }
}
