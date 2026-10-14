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

    // Dirección pública del archivo. En la base: /archivos/{clave} (lo sirve la API); en R2 será
    // la del subdominio de fotos.
    string Url(string clave);
}
