using Hermes.Domain.Common;
using Hermes.Domain.Enums;

namespace Hermes.Domain.Entities;

// Una foto subida por la tienda (de un producto, el logo…). Los bytes NO están acá: cada tamaño es
// un archivo aparte en IAlmacenArchivos, con la clave que da ClaveDe. La original no se guarda.
public class Imagen : IPerteneceATienda
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TiendaId { get; set; }
    // Medidas después de corregir la orientación, antes de achicar.
    public int AnchoOriginal { get; set; }
    public int AltoOriginal { get; set; }
    // Suma de los tres tamaños guardados (para límites por plan, M13).
    public int BytesGuardados { get; set; }
    public DateTimeOffset CreadaEn { get; set; }

    // Misma forma que tendrá la ruta en un bucket (R2): migrar es copiar con la misma clave.
    public static string ClaveDe(Guid tiendaId, Guid imagenId, TamanoImagen tamano)
        => $"{tiendaId}/imagenes/{imagenId}/{tamano.ToString().ToLowerInvariant()}.webp";
}
