using Hermes.Application.Dtos;

namespace Hermes.Application.Interfaces;

public interface IImagenService
{
    // Valida, procesa (3 tamaños WEBP) y guarda. La foto original no se guarda.
    Task<ImagenDto> SubirAsync(byte[] contenido, CancellationToken ct);
    Task BorrarAsync(Guid id, CancellationToken ct);
}
