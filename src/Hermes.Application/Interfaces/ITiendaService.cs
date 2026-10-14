using Hermes.Application.Dtos;

namespace Hermes.Application.Interfaces;

// La tienda de la SESIÓN (ITiendaActual). No hay "tienda por id": nadie edita la de otro.
public interface ITiendaService
{
    Task<TiendaDto> ObtenerAsync(CancellationToken ct);
    Task<TiendaDto> ActualizarAsync(ActualizarTiendaDto dto, CancellationToken ct);
    Task<TiendaDto> CambiarDireccionAsync(CambiarDireccionDto dto, CancellationToken ct);
    Task<ConfiguracionPublicaDto> ObtenerConfiguracionPublicaAsync(CancellationToken ct);
    Task<ConfiguracionPublicaDto> GuardarConfiguracionPublicaAsync(ConfiguracionPublicaDto dto, CancellationToken ct);
}
