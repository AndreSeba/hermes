using Hermes.Application.Dtos;

namespace Hermes.Application.Interfaces;

public interface ICosteoService
{
    Task<CosteoDto> ObtenerAsync(Guid productoId, CancellationToken ct);

    // Cuenta el precio sugerido de unos números sin guardar nada (la calculadora mientras se escribe).
    Task<CosteoDto> CalcularAsync(Guid productoId, GuardarCosteoDto dto, CancellationToken ct);

    // Crea o reemplaza el costeo y deja `Producto.Costo` en el total. NO toca el precio de venta.
    Task<CosteoDto> GuardarAsync(Guid productoId, GuardarCosteoDto dto, CancellationToken ct);

    // Quita el costeo: el producto conserva el último costo total como costo directo.
    Task QuitarAsync(Guid productoId, CancellationToken ct);
}
