using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Hermes.Public.Pages;

[ResponseCache(Duration = 30, Location = ResponseCacheLocation.Any)]
public class ProductoModel : TiendaBase
{
    public ProductoModel(IPublicoService publico, Urls urls) : base(publico, urls) { }

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    public ProductoPublicoDto Producto { get; private set; } = null!;

    // Hay botón de pedido si la tienda lo tiene encendido y cargó su WhatsApp (el DTO ya trae null si no).
    public bool PuedePedir => Tienda.Whatsapp is not null && Producto.Disponible != false;
    public string DireccionDeProducto => $"{DireccionDeTienda}/p/{Producto.Id}";

    // La variante que viene elegida: la primera disponible (o la primera, si todas están agotadas o no se muestra).
    public VariantePublicaDto? VarianteInicial => Producto.Variantes.FirstOrDefault(v => v.Disponible != false) ?? Producto.Variantes.FirstOrDefault();

    // ¿Hay algo que elegir? Un producto "Único" (sin color ni talla) no pregunta nada.
    public bool ConVariantes => Producto.Variantes.Count > 1 || Producto.Variantes.Any(v => v.Color is not null || v.Talla is not null);

    // El pedido que se arma sin JavaScript (variante inicial, cantidad 1); publico.js lo rearma al elegir.
    public string EnlacePedido => Formato.EnlaceWhatsapp(Tienda.Whatsapp!, Formato.MensajeDePedido(
        Tienda.Nombre, Producto.Nombre, VarianteInicial is { } v && ConVariantes ? Formato.Variante(v.Color, v.Talla) : string.Empty,
        1, Producto.Precio, DireccionDeProducto));

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await CargarTiendaAsync(ct)) return NotFound();
        try
        {
            Producto = await Publico.ProductoAsync(Slug, Id, ct);
        }
        catch (TiendaPublicaNoEncontradaException)
        {
            return NotFound();
        }

        var descripcion = Resumir(Producto.Descripcion);
        if (Producto.Precio is { } precio) descripcion = descripcion.Length > 0 ? $"{Formato.Bs(precio)} · {descripcion}" : Formato.Bs(precio);
        PonerVistaPrevia($"{Producto.Nombre} · {Tienda.Nombre}", descripcion, DireccionDeProducto, Producto.Id);
        return Page();
    }
}
