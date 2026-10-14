using Hermes.Application.Dtos;

namespace Hermes.Application.Interfaces;

public interface IProductoService
{
    Task<PaginaDeProductosDto> ListarAsync(FiltroProductosDto filtro, CancellationToken ct);
    Task<ProductoDto> ObtenerAsync(Guid id, CancellationToken ct);
    Task<ProductoDto> CrearAsync(GuardarProductoDto dto, CancellationToken ct);
    Task<ProductoDto> ActualizarAsync(Guid id, GuardarProductoDto dto, CancellationToken ct);
    // Un producto nunca se borra: se archiva (deja de verse en el catálogo y la tienda) y se puede desarchivar.
    Task<ProductoDto> ArchivarAsync(Guid id, bool archivar, CancellationToken ct);
}
