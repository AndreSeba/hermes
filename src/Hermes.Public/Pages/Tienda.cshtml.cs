using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Hermes.Public.Pages;

// Lo que se ve puede tardar hasta 30 s en reflejar un cambio de la dueña (y no se va a la base en cada visita).
[ResponseCache(Duration = 30, Location = ResponseCacheLocation.Any)]
public class TiendaModel : TiendaBase
{
    public TiendaModel(IPublicoService publico, Urls urls) : base(publico, urls) { }

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? Categoria { get; set; }
    [BindProperty(SupportsGet = true)] public string? Cursor { get; set; }

    public PaginaPublicaDto Pagina { get; private set; } = new([], null);

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await CargarTiendaAsync(ct)) return NotFound();

        try
        {
            Pagina = await Publico.ProductosAsync(Slug, new FiltroPublicoDto(Q, Categoria, Cursor), ct);
        }
        catch (ValidacionException)
        {
            // Un cursor inventado en la dirección: se muestra desde el principio en vez de un error.
            Cursor = null;
            Pagina = await Publico.ProductosAsync(Slug, new FiltroPublicoDto(Q, Categoria), ct);
        }

        PonerVistaPrevia(Tienda.Nombre, Resumir(Tienda.Descripcion) is { Length: > 0 } d ? d : "Mirá lo que tenemos y pedí por WhatsApp.", DireccionDeTienda);
        return Page();
    }
}
