using System.Text.RegularExpressions;

namespace Hermes.Public;

// El formato de la dirección de una tienda (Validacion.Slug del servidor): se revisa ANTES de ir a la base, así los
// pedidos de cualquier otra cosa (/wp-login.php, /.env, bots) no cuestan una consulta.
public static partial class Slugs
{
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,39}$")]
    private static partial Regex Formato();

    public static bool Valido(string? slug) => slug is not null && Formato().IsMatch(slug.Trim().ToLowerInvariant());
}
