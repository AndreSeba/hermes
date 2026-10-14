using Hermes.Application.Dtos;

namespace Hermes.Application.Interfaces;

public interface ICategoriaService
{
    Task<IReadOnlyList<CategoriaDto>> ListarAsync(bool incluirInactivas, CancellationToken ct);
    Task<CategoriaDto> CrearAsync(CrearCategoriaDto dto, CancellationToken ct);
    Task<CategoriaDto> ActualizarAsync(Guid id, ActualizarCategoriaDto dto, CancellationToken ct);
}
