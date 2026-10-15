using Hermes.Application.Dtos;

namespace Hermes.Application.Interfaces;

public interface IImagenService
{
    // Valida, procesa (3 tamaños WEBP) y guarda. La foto original no se guarda.
    // `esComprobante`: la foto de un comprobante de pago; no cuenta para el límite de espacio del plan.
    Task<ImagenDto> SubirAsync(byte[] contenido, CancellationToken ct, bool esComprobante = false);
    Task BorrarAsync(Guid id, CancellationToken ct);
}
