using Hermes.Application.Dtos;

namespace Hermes.Application.Interfaces;

// La conexión de la tienda con su cuenta de Instagram (M12): conectar (OAuth de Instagram), ver el estado, sincronizar las
// publicaciones nuevas a la bandeja de revisión (M11) y desconectar. Todo sobre la tienda de la sesión.
public interface IInstagramConexionService
{
    Task<InstagramConexionDto> EstadoAsync(CancellationToken ct);
    // La dirección de Instagram a la que se manda a la dueña para autorizar (lleva un `state` firmado con su tienda).
    UrlConexionDto Url();
    Task<InstagramConexionDto> ConectarAsync(ConectarInstagramDto dto, CancellationToken ct);
    Task DesconectarAsync(CancellationToken ct);
    // "Sincronizar ahora" (y la tarea periódica, tienda por tienda).
    Task<SincronizacionDto> SincronizarAsync(CancellationToken ct);
}
