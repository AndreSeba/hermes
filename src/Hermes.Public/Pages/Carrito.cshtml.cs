using Hermes.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Hermes.Public.Pages;

// El carrito. Lo que tiene adentro vive en el navegador (localStorage, por tienda): la página es solo el armazón y
// publico.js la llena después de preguntar al servidor cómo está hoy cada cosa (/{slug}/carrito/revisar).
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class CarritoModel : TiendaBase
{
    public CarritoModel(IPublicoService publico, Urls urls) : base(publico, urls) { }

    // La tienda recibe pedidos si tiene "Pedidos por WhatsApp" encendido y cargó su número (el DTO trae null si no).
    public bool RecibePedidos => Tienda.Whatsapp is not null;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await CargarTiendaAsync(ct)) return NotFound();
        ViewData["Titulo"] = $"Tu carrito · {Tienda.Nombre}";
        return Page();
    }
}
