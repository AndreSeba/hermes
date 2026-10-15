namespace Hermes.Domain.Entities;

public enum EstadoPago
{
    Pendiente, // la dueña subió el comprobante y espera
    Aprobado, // el administrador lo confirmó: el plan se activó
    Rechazado, // el administrador no lo aceptó (con un motivo)
}

// Qué plan tiene una tienda y hasta cuándo. Una tienda sin ninguna suscripción vigente tiene el plan por defecto de la
// configuración (Planes:PlanSinSuscripcion, "gratis" en producción). Ni esta tabla ni PagoPlan llevan filtro global por
// tienda: las crea y las lee el administrador de la plataforma sobre OTRAS tiendas. Los servicios de la dueña filtran por
// la tienda de la sesión a mano (como TiendaService).
public class Suscripcion
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TiendaId { get; set; }
    public Guid PlanId { get; set; }
    public Plan? Plan { get; set; }

    // UTC. Vigente si Desde <= ahora y (Hasta es null o Hasta > ahora). null = sin vencimiento.
    public DateTime Desde { get; set; }
    public DateTime? Hasta { get; set; }

    // "Pago" (se aprobó un comprobante) o "Manual" (lo asignó el administrador: cortesía, prueba, corrección).
    public string Origen { get; set; } = "Pago";
    public Guid? PagoId { get; set; }
    // El administrador que la asignó o aprobó (queda el rastro de quién fue).
    public Guid? AsignadaPorId { get; set; }

    public DateTime CreadaEn { get; set; }
}

// La solicitud de un plan pagado: la dueña paga por QR o transferencia fuera de la app y sube el comprobante.
public class PagoPlan
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TiendaId { get; set; }
    public Guid PlanId { get; set; }
    public Plan? Plan { get; set; }

    public int Meses { get; set; }
    // PrecioMensual × Meses al momento de pedirlo (lo calcula el servidor; un cambio de precio después no lo toca).
    public decimal Monto { get; set; }
    // "Qr" o "Transferencia".
    public string Metodo { get; set; } = string.Empty;
    // La foto del comprobante (tabla `imagenes`, de ESA tienda).
    public Guid? ComprobanteImagenId { get; set; }

    public EstadoPago Estado { get; set; } = EstadoPago.Pendiente;
    // Por qué se rechazó (lo ve la dueña).
    public string? Motivo { get; set; }

    public DateTime CreadoEn { get; set; }
    public DateTime? ResueltoEn { get; set; }
    public Guid? ResueltoPorId { get; set; }
}
