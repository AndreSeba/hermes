using Hermes.Application.Dtos;

namespace Hermes.Application.Interfaces;

// `Creado`: false si esa ClaveIdempotencia ya había registrado el gasto (se devuelve el mismo).
public record ResultadoGasto(GastoDto Gasto, bool Creado);

public interface IGastoService
{
    Task<ResultadoGasto> RegistrarAsync(RegistrarGastoDto dto, CancellationToken ct);
    Task<PaginaDeGastosDto> ListarAsync(FiltroGastosDto filtro, CancellationToken ct);
    Task<GastoDto> AnularAsync(Guid id, CancellationToken ct);
    // Suma de los gastos vigentes del período (Desde incluido, Hasta excluido).
    Task<decimal> TotalAsync(DateTimeOffset? desde, DateTimeOffset? hasta, CancellationToken ct);
}

public interface IInicioService
{
    Task<InicioDto> ResumenAsync(FiltroPeriodoDto periodo, CancellationToken ct);
}
