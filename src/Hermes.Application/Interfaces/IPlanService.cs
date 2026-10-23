using Hermes.Application.Dtos;

namespace Hermes.Application.Interfaces;

// Los límites de plan que aplican los demás servicios (productos, fotos). Una sola fuente de verdad: el plan VIGENTE de la
// tienda de la sesión. Sin límite aplicable no hace nada; si se pasa, 402 con un mensaje que manda a "Mi plan".
public interface ILimitesDePlan
{
    // ¿Puede tener un producto (no archivado) más?
    Task ExigirPoderCrearProductoAsync(CancellationToken ct);

    // ¿Entran `bytesNuevos` más de fotos en su espacio?
    Task ExigirAlmacenamientoAsync(long bytesNuevos, CancellationToken ct);

    // ¿Puede sumar una persona más al equipo? (cuenta al dueño y a las personas activas)
    Task ExigirPoderSumarPersonaAsync(CancellationToken ct);
}

// El plan de la tienda de la SESIÓN: ver el suyo, ver los que hay y pedir uno (pagando por QR o transferencia fuera de la app).
public interface IPlanService
{
    Task<PlanesDto> ListarPlanesAsync(CancellationToken ct);
    Task<MiPlanDto> MiPlanAsync(CancellationToken ct);
    // Una sola solicitud pendiente por tienda. El monto lo calcula el servidor (precio × meses).
    Task<PagoPlanDto> SolicitarAsync(SolicitarPlanDto dto, CancellationToken ct);
    Task<IReadOnlyList<PagoPlanDto>> MisPagosAsync(CancellationToken ct);
}

// El panel del administrador de la PLATAFORMA (rol distinto del de dueña de tienda). Mira y cambia OTRAS tiendas: por eso
// cada consulta acota por tienda a mano y no usa el filtro global.
public interface IAdminService
{
    Task<PaginaDePagosDto> PagosAsync(FiltroPagosDto filtro, CancellationToken ct);
    // Aprobar activa el plan (o lo extiende). Una sola vez: el segundo intento da 409.
    Task<PagoParaRevisarDto> AprobarAsync(Guid pagoId, Guid adminId, CancellationToken ct);
    Task<PagoParaRevisarDto> RechazarAsync(Guid pagoId, Guid adminId, RechazarPagoDto dto, CancellationToken ct);

    Task<PaginaDeTiendasDto> TiendasAsync(FiltroTiendasDto filtro, CancellationToken ct);
    // Cortesía, prueba o corrección: cambia el plan de una tienda sin pago.
    Task<AdminTiendaDto> AsignarPlanAsync(Guid tiendaId, Guid adminId, AsignarPlanDto dto, CancellationToken ct);

    Task<IReadOnlyList<PlanDto>> PlanesAsync(CancellationToken ct);
    Task<PlanDto> ActualizarPlanAsync(Guid planId, ActualizarPlanDto dto, CancellationToken ct);
}
