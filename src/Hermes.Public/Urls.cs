namespace Hermes.Public;

// Las fotos las sirve la API (/archivos/...): acá se arma la dirección completa. Mientras las fotos estén en la base,
// `Publico:ArchivosBaseUrl` apunta a la API; cuando pasen a Cloudflare R2, las direcciones del DTO ya serán absolutas y
// esto no agrega nada.
public class Urls
{
    private readonly string _archivos;

    public Urls(IConfiguration configuracion) => _archivos = (configuracion["Publico:ArchivosBaseUrl"] ?? string.Empty).TrimEnd('/');

    public string Foto(string url) => url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : _archivos + url;
    // (La clave de una foto a partir de su dirección la sabe el almacén: IAlmacenArchivos.ClaveDeUrl.)
}
