using Hermes.Application.Dtos;

namespace Hermes.Application.Interfaces;

// La tienda pública: lo que ve cualquiera con el enlace, SIN sesión. Cada método recibe el slug y falla con
// TiendaPublicaNoEncontradaException (404) si no existe, está apagada o el producto no se muestra.
public interface IPublicoService
{
    Task<TiendaPublicaDto> ObtenerTiendaAsync(string slug, CancellationToken ct);
    Task<PaginaPublicaDto> ProductosAsync(string slug, FiltroPublicoDto filtro, CancellationToken ct);
    Task<ProductoPublicoDto> ProductoAsync(string slug, Guid productoId, CancellationToken ct);

    // El carrito vive en el navegador: esto devuelve cómo está hoy cada cosa (precio, disponible).
    Task<CarritoPublicoDto> RevisarCarritoAsync(string slug, RevisarCarritoDto dto, CancellationToken ct);
    // Guarda el pedido (queda "Nuevo" en el panel). No toca el stock. La misma clave devuelve el mismo pedido.
    Task<PedidoEnviadoDto> PedirAsync(string slug, NuevoPedidoDto dto, CancellationToken ct);
}
