namespace Hermes.Domain.Entities;

// Un plan que se puede contratar. Es de la PLATAFORMA (no de una tienda): lo edita el administrador, por eso no hay filtro
// por tienda. Los límites son datos, no código: null = sin límite.
public class Plan
{
    public const string CodigoGratis = "gratis";
    public const string CodigoEmprendedor = "emprendedor";
    public const string CodigoNegocio = "negocio";

    public Guid Id { get; set; } = Guid.CreateVersion7();
    // "gratis" / "emprendedor" / "negocio": no cambia (el código lo usa para decidir el plan por defecto).
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    // Bs por mes. 0 = gratis (no se paga ni se solicita).
    public decimal PrecioMensual { get; set; }
    // Productos NO archivados que puede tener a la vez.
    public int? MaxProductos { get; set; }
    // Espacio de fotos (la suma de BytesGuardados de sus imágenes), en MB.
    public int? MaxAlmacenamientoMb { get; set; }
    // Desactivado: ya no se ofrece para contratar (las tiendas que lo tienen lo conservan).
    public bool Activo { get; set; } = true;
    public int Orden { get; set; }
}
