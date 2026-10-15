using Hermes.Domain.Entities;

namespace Hermes.Application.Dtos;

// null en un límite = sin límite.
public record PlanDto(Guid Id, string Codigo, string Nombre, decimal PrecioMensual, int? MaxProductos, int? MaxAlmacenamientoMb, bool Activo);

// Los planes que se pueden contratar, los meses permitidos y cómo pagar (texto que configura la plataforma).
public record PlanesDto(IReadOnlyList<PlanDto> Planes, IReadOnlyList<int> Meses, string InstruccionesDePago);

public record PagoPlanDto(
    Guid Id, Guid PlanId, string Plan, int Meses, decimal Monto, string Metodo, EstadoPago Estado, string? Motivo,
    ImagenDto? Comprobante, DateTimeOffset CreadoEn, DateTimeOffset? ResueltoEn);

// Hasta = null: sin vencimiento. AlmacenamientoMb con 1 decimal. Pendiente: el pago que espera revisión, si hay.
public record MiPlanDto(PlanDto Plan, DateTimeOffset? Hasta, int Productos, decimal AlmacenamientoMb, PagoPlanDto? Pendiente);

// Metodo: "Qr" o "Transferencia". ComprobanteImagenId: el Id que devolvió POST /imagenes?comprobante=true.
public record SolicitarPlanDto(Guid PlanId, int Meses, string Metodo, Guid ComprobanteImagenId);

// ───────────── Administración de la plataforma ─────────────

public record PagoParaRevisarDto(PagoPlanDto Pago, Guid TiendaId, string Tienda, string Slug, string Email);

public record FiltroPagosDto(EstadoPago Estado = EstadoPago.Pendiente, Guid? Cursor = null, int? Limite = null);

public record PaginaDePagosDto(IReadOnlyList<PagoParaRevisarDto> Items, Guid? Siguiente);

public record RechazarPagoDto(string Motivo);

public record AdminTiendaDto(
    Guid Id, string Nombre, string Slug, string Email, bool Activa, string Plan, DateTimeOffset? Hasta, int Productos, decimal AlmacenamientoMb);

public record FiltroTiendasDto(string? Buscar = null, Guid? Cursor = null, int? Limite = null);

public record PaginaDeTiendasDto(IReadOnlyList<AdminTiendaDto> Items, Guid? Siguiente);

// Meses = null: sin vencimiento (cortesía o el plan gratis explícito).
public record AsignarPlanDto(Guid PlanId, int? Meses = null);

public record ActualizarPlanDto(decimal PrecioMensual, int? MaxProductos, int? MaxAlmacenamientoMb, bool Activo);
