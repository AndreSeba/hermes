namespace Hermes.Infrastructure.Planes;

// Sección "Planes" de la configuración.
public class PlanesOptions
{
    public const string SectionName = "Planes";

    // El plan de una tienda que no tiene NINGUNA suscripción vigente. En producción "gratis". En desarrollo, "negocio": así las
    // cuentas de prueba (que crean decenas de productos) no chocan con el límite de 15 del Gratis; las pruebas de límites asignan
    // el Gratis explícitamente. Si el código no existe, se usa "gratis".
    public string PlanSinSuscripcion { get; set; } = "gratis";

    // Por cuántos meses se puede pedir un plan.
    // Sin valor inicial a propósito: el binder de configuración AGREGA a un arreglo ya lleno (salían los meses duplicados).
    public int[] MesesPermitidos { get; set; } = [];

    public IReadOnlyList<int> Meses => MesesPermitidos.Length > 0 ? MesesPermitidos : [1, 3, 6, 12];

    // Cómo pagar (se muestra al pedir un plan). Lo escribe la plataforma: datos de la cuenta, número del QR, etc.
    public string InstruccionesDePago { get; set; } = "Pagá por QR o transferencia a la cuenta de la plataforma y subí la foto del comprobante. Lo revisamos y activamos tu plan.";
}
