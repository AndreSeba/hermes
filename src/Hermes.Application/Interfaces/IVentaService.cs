using Hermes.Application.Dtos;

namespace Hermes.Application.Interfaces;

public record ResultadoVenta(VentaDto Venta, bool Creada);

public interface IVentaService
{
    // Creada = false: esa clave ya había registrado la venta y se devuelve la misma (reintento).
    Task<ResultadoVenta> RegistrarAsync(RegistrarVentaDto dto, CancellationToken ct);
    Task<VentaDto> ObtenerAsync(Guid id, CancellationToken ct);
    Task<PaginaDeVentasDto> ListarAsync(FiltroVentasDto filtro, CancellationToken ct);
    Task<ResumenVentasDto> ResumenAsync(FiltroPeriodoDto filtro, CancellationToken ct);
    // Devuelve el stock de lo que se había descontado. Una venta anulada no se puede anular otra vez (409).
    Task<VentaDto> AnularAsync(Guid id, CancellationToken ct);
    Task<PaginaDeVendiblesDto> ProductosParaVenderAsync(FiltroVendiblesDto filtro, CancellationToken ct);
}
