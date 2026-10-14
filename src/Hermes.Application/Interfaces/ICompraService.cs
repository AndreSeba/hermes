using Hermes.Application.Dtos;

namespace Hermes.Application.Interfaces;

public interface IProveedorService
{
    // `incluirInactivos`: por defecto solo los que se usan.
    Task<IReadOnlyList<ProveedorDto>> ListarAsync(bool incluirInactivos, CancellationToken ct);
    Task<ProveedorDetalleDto> ObtenerAsync(Guid id, CancellationToken ct);
    Task<ProveedorDto> CrearAsync(GuardarProveedorDto dto, CancellationToken ct);
    Task<ProveedorDto> ActualizarAsync(Guid id, GuardarProveedorDto dto, CancellationToken ct);
    Task<ProveedorDto> CambiarActivoAsync(Guid id, bool activo, CancellationToken ct);
}

// `Creada`: false si esa ClaveIdempotencia ya había registrado la compra (se devuelve la misma).
public record ResultadoCompra(CompraDto Compra, bool Creada);

public interface ICompraService
{
    Task<ResultadoCompra> RegistrarAsync(RegistrarCompraDto dto, CancellationToken ct);
    Task<CompraDto> ObtenerAsync(Guid id, CancellationToken ct);
    Task<PaginaDeComprasDto> ListarAsync(FiltroComprasDto filtro, CancellationToken ct);
    Task<ResumenComprasDto> ResumenAsync(CancellationToken ct);

    // Llegó la mercadería: suma stock, el costo de cada producto pasa a ser el de la compra y avisa los márgenes flojos.
    // Una sola vez (409 si ya estaba recibida o anulada).
    Task<ResultadoRecibirDto> RecibirAsync(Guid id, CancellationToken ct);
    Task<CompraDto> PagarAsync(Guid id, CancellationToken ct);
    // Si ya estaba recibida, resta el stock que sumó (409 si esas unidades ya se vendieron).
    Task<CompraDto> AnularAsync(Guid id, CancellationToken ct);

    // El último costo de cada proveedor para un producto, el más barato primero.
    Task<IReadOnlyList<PrecioProveedorDto>> CompararAsync(Guid productoId, CancellationToken ct);
}
