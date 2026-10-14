using Hermes.Application.Dtos;

namespace Hermes.Application.Interfaces;

// Lo que usa la pantalla de Stock.
public interface IStockService
{
    Task<PaginaDeStockDto> ListarAsync(FiltroStockDto filtro, CancellationToken ct);
    Task<ResumenStockDto> ResumenAsync(CancellationToken ct);
    // Un producto (aunque esté archivado o sin control). 404 si no existe o es de otra tienda.
    Task<ProductoStockDto> ObtenerAsync(Guid productoId, CancellationToken ct);
    Task<PaginaDeMovimientosDto> MovimientosAsync(Guid productoId, FiltroMovimientosDto filtro, CancellationToken ct);
    Task<ProductoStockDto> IngresarAsync(IngresoDto dto, CancellationToken ct);
    Task<ProductoStockDto> AjustarAsync(AjusteDto dto, CancellationToken ct);
}
