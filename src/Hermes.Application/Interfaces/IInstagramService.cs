using Hermes.Application.Dtos;

namespace Hermes.Application.Interfaces;

// El lector de publicaciones (M11) y su bandeja de revisión. Nada llega al catálogo sin pasar por la dueña.
public interface IInstagramService
{
    // Lo que se entiende de un texto, sin guardar nada ("Probá tu formato").
    LecturaDto Leer(LeerTextoDto dto);

    // Lee el texto y deja cada producto entendido en la bandeja (pendiente de revisión). 400 si no se entendió ninguno.
    Task<IReadOnlyList<BorradorDto>> ImportarAsync(LeerTextoDto dto, CancellationToken ct);

    Task<PaginaDeBorradoresDto> ListarAsync(FiltroBorradoresDto filtro, CancellationToken ct);
    Task<BorradorDto> ObtenerAsync(Guid id, CancellationToken ct);
    // Cuántos esperan en la bandeja, separados en "listos" (con precio) y "sin precio" (para el aviso del Inicio).
    Task<ResumenBandejaDto> ResumenBandejaAsync(CancellationToken ct);

    // Una sola vez (409 si ya se publicó o descartó): crea el producto con lo que la dueña revisó.
    Task<PublicacionDto> PublicarAsync(Guid id, PublicarBorradorDto dto, CancellationToken ct);
    Task<BorradorDto> DescartarAsync(Guid id, CancellationToken ct);
}
