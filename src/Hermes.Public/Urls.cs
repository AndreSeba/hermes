namespace Hermes.Public;

// Las fotos las sirve la API (/archivos/...): acá se arma la dirección completa. Mientras las fotos estén en la base,
// `Publico:ArchivosBaseUrl` apunta a la API; cuando pasen a Cloudflare R2, las direcciones del DTO ya serán absolutas y
// esto no agrega nada.
public class Urls
{
    private readonly string _archivos;

    public Urls(IConfiguration configuracion) => _archivos = (configuracion["Publico:ArchivosBaseUrl"] ?? string.Empty).TrimEnd('/');

    public string Foto(string url) => url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : _archivos + url;

    // La clave que entiende IAlmacenArchivos ("{tienda}/imagenes/{id}/detalle.webp") a partir de la dirección de la foto.
    public static string? ClaveDe(string url)
    {
        const string marca = "/archivos/";
        var i = url.IndexOf(marca, StringComparison.Ordinal);
        return i < 0 ? null : url[(i + marca.Length)..];
    }
}
