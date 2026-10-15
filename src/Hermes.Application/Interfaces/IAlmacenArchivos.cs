namespace Hermes.Application.Interfaces;

public record ArchivoLeido(byte[] Datos, string ContentType);

// Dónde viven los archivos (fotos). Los servicios solo conocen esta interfaz: hoy la implementa
// AlmacenEnBaseDeDatos; con más tiendas, una implementación para Cloudflare R2 (docs/PLAN.md, M2).
public interface IAlmacenArchivos
{
    // No guarda por sí sola: queda pendiente hasta el SaveChanges/commit del servicio que la llama,
    // así la foto y su registro se guardan juntos o no se guarda nada.
    Task GuardarAsync(string clave, byte[] datos, string contentType, CancellationToken ct);

    Task<ArchivoLeido?> ObtenerAsync(string clave, CancellationToken ct);

    Task BorrarAsync(IEnumerable<string> claves, CancellationToken ct);

    // Dirección pública del archivo. En la base: /archivos/{clave} (lo sirve la API); en R2, la del
    // subdominio de fotos.
    string Url(string clave);

    // Lo inverso de Url: la clave a partir de una dirección que armó este almacén (null si no es suya).
    // La usa la vista previa de la tienda pública para leer la foto del producto.
    string? ClaveDeUrl(string url);
}
