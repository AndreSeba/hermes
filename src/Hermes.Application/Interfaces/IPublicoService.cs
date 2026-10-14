using Hermes.Application.Dtos;

namespace Hermes.Application.Interfaces;

// La tienda pública: lo que ve cualquiera con el enlace, SIN sesión. Cada método recibe el slug y falla con
// TiendaPublicaNoEncontradaException (404) si no existe, está apagada o el producto no se muestra.
public interface IPublicoService
{
    Task<TiendaPublicaDto> ObtenerTiendaAsync(string slug, CancellationToken ct);
    Task<PaginaPublicaDto> ProductosAsync(string slug, FiltroPublicoDto filtro, CancellationToken ct);
    Task<ProductoPublicoDto> ProductoAsync(string slug, Guid productoId, CancellationToken ct);
}
