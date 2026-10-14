using Hermes.Application.Dtos;

namespace Hermes.Application.Interfaces;

public interface IAuthService
{
    Task<SesionEmitida> RegistrarAsync(RegistroDto dto, CancellationToken ct);
    Task<SesionEmitida> LoginAsync(LoginDto dto, CancellationToken ct);
    // Rota el refresh token: el recibido queda revocado y se emite uno nuevo.
    Task<SesionEmitida> RenovarAsync(string refreshToken, CancellationToken ct);
    // Idempotente: un token desconocido o ya revocado no es error.
    Task CerrarSesionAsync(string refreshToken, CancellationToken ct);
    Task<PerfilDto> ObtenerPerfilAsync(Guid usuarioId, Guid tiendaId, CancellationToken ct);
}
