using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hermes.Public.Pages;

// Lo común de las páginas de una tienda: pedir la tienda por su slug (404 si no existe o está apagada) y armar las
// direcciones absolutas para la vista previa al compartir.
public abstract class TiendaBase : PageModel
{
    protected readonly IPublicoService Publico;
    public Urls Urls { get; }

    protected TiendaBase(IPublicoService publico, Urls urls)
    {
        Publico = publico;
        Urls = urls;
    }

    [BindProperty(SupportsGet = true)]
    public string Slug { get; set; } = string.Empty;

    public TiendaPublicaDto Tienda { get; private set; } = null!;

    // Dirección pública de ESTA tienda (con el esquema y host con que llegó el pedido: detrás de un proxy, hay que
    // configurar ForwardedHeaders en la salida a producción).
    public string Base => $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
    public string DireccionDeTienda => $"{Base}/{Tienda.Slug}";

    // true = la tienda existe y está cargada; false = hay que devolver el 404 (el llamador hace `return NotFound()`).
    protected async Task<bool> CargarTiendaAsync(CancellationToken ct)
    {
        if (!Slugs.Valido(Slug)) return false; // no es ni la forma de una dirección: ni se consulta la base
        try
        {
            Tienda = await Publico.ObtenerTiendaAsync(Slug, ct);
        }
        catch (TiendaPublicaNoEncontradaException)
        {
            return false;
        }
        ViewData["Tienda"] = Tienda;
        return true;
    }

    protected void PonerVistaPrevia(string titulo, string? descripcion, string direccion, Guid? productoId = null)
    {
        ViewData["Titulo"] = titulo;
        ViewData["Descripcion"] = descripcion;
        ViewData["Direccion"] = direccion;
        ViewData["Imagen"] = $"{DireccionDeTienda}/og.jpg" + (productoId is { } id ? $"?p={id}" : string.Empty);
    }

    // El texto que se ve al compartir: corto y sin saltos de línea.
    protected static string Resumir(string? texto, int maximo = 160)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
        var limpio = string.Join(' ', texto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return limpio.Length <= maximo ? limpio : limpio[..(maximo - 1)].TrimEnd() + "…";
    }
}
