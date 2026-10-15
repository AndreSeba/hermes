using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hermes.Application.Exceptions;
using Microsoft.Extensions.Options;

namespace Hermes.Infrastructure.Instagram;

// Sección "Instagram". Sin AppId / AppSecret / RedirectUri / ClaveCifrado la conexión no está disponible (la dueña puede
// seguir pegando el texto a mano, M11). Los dos secretos van por user-secrets o variables de entorno, nunca en appsettings.
public class InstagramOptions
{
    public const string SectionName = "Instagram";

    public string AppId { get; set; } = string.Empty;      // "Instagram App ID" del panel de Meta
    public string AppSecret { get; set; } = string.Empty;  // "Instagram App Secret"
    // Adonde vuelve Instagram después de autorizar: la pantalla del panel /instagram/conectado (tiene que estar cargada
    // tal cual en "Valid OAuth Redirect URIs" de la app de Meta). Ej.: https://panel.midominio.com/instagram/conectado
    public string RedirectUri { get; set; } = string.Empty;
    // 32 bytes al azar en base64 (openssl rand -base64 32): cifra los tokens en la base y firma el `state`.
    public string ClaveCifrado { get; set; } = string.Empty;
    public string VersionGraph { get; set; } = "v23.0";

    // Las direcciones de Meta. SOLO para las pruebas de punta a punta se apuntan a un Meta falso (scripts/meta_falso.py);
    // en producción no se tocan. SoloHttps = false permite bajar fotos por http (únicamente con ese Meta falso local).
    public string UrlAutorizacion { get; set; } = "https://www.instagram.com/oauth/authorize";
    public string UrlApi { get; set; } = "https://api.instagram.com";
    public string UrlGraph { get; set; } = "https://graph.instagram.com";
    public bool SoloHttps { get; set; } = true;

    // La tarea que trae publicaciones nuevas cada tanto (solo en la API).
    public bool SincronizacionAutomatica { get; set; } = true;
    public int IntervaloMinutos { get; set; } = 30;
    public int MaximoPorSincronizacion { get; set; } = 25;
    // La PRIMERA vez se traen solo las publicaciones de estos últimos días (no todo el historial a la bandeja).
    public int DiasPrimeraSincronizacion { get; set; } = 30;

    public bool Configurada => AppId.Length > 0 && AppSecret.Length > 0 && RedirectUri.Length > 0 && ClaveCifrado.Length > 0;
}

public record TokenIg(string Token, int? ExpiraEnSegundos);
public record PerfilIg(string Id, string Usuario);
public record HijoIg(string Tipo, string? UrlMedia, string? Miniatura);
public record MediaIg(string Id, string? Texto, string Tipo, string? UrlMedia, string? Miniatura, string? Enlace, DateTimeOffset Fecha, IReadOnlyList<HijoIg> Hijos);

// Lo que se le pide a Meta. Interfaz para poder probar todo sin una app de Meta real (las pruebas usan uno falso).
public interface IClienteInstagram
{
    Task<TokenIg> CanjearCodigoAsync(string codigo, CancellationToken ct);
    Task<TokenIg> TokenLargoAsync(string tokenCorto, CancellationToken ct);
    Task<TokenIg> RenovarAsync(string tokenLargo, CancellationToken ct);
    Task<PerfilIg> PerfilAsync(string token, CancellationToken ct);
    // Las más nuevas primero (como las devuelve Instagram).
    Task<IReadOnlyList<MediaIg>> PublicacionesAsync(string token, int limite, CancellationToken ct);
    Task<byte[]> DescargarAsync(string url, CancellationToken ct);
}

// El cliente real: "Instagram API with Instagram Login" (sin página de Facebook; cuentas profesionales), según la
// documentación de Meta vigente en octubre de 2026. NO probado contra Meta de verdad (no hay app todavía): las pruebas
// verifican las direcciones, los parámetros y cómo se leen las respuestas y los errores.
public class ClienteInstagramGraph : IClienteInstagram
{
    private const int MaximoFoto = 15 * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly InstagramOptions _o;

    public ClienteInstagramGraph(HttpClient http, IOptions<InstagramOptions> opciones)
    {
        _http = http;
        _o = opciones.Value;
    }

    private string Graph(string ruta) => $"{_o.UrlGraph.TrimEnd('/')}/{_o.VersionGraph}/{ruta}";

    public async Task<TokenIg> CanjearCodigoAsync(string codigo, CancellationToken ct)
    {
        using var r = await _http.PostAsync($"{_o.UrlApi.TrimEnd('/')}/oauth/access_token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _o.AppId,
            ["client_secret"] = _o.AppSecret,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = _o.RedirectUri,
            ["code"] = codigo,
        }), ct);
        var json = await LeerAsync(r, ct);
        // A veces viene envuelto en { "data": [ { … } ] }.
        if (json.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0) json = data[0];
        return new TokenIg(json.GetProperty("access_token").GetString()!, null);
    }

    public async Task<TokenIg> TokenLargoAsync(string tokenCorto, CancellationToken ct)
        => Token(await LeerAsync(await _http.GetAsync(
            $"{_o.UrlGraph.TrimEnd('/')}/access_token?grant_type=ig_exchange_token&client_secret={Uri.EscapeDataString(_o.AppSecret)}&access_token={Uri.EscapeDataString(tokenCorto)}", ct), ct));

    public async Task<TokenIg> RenovarAsync(string tokenLargo, CancellationToken ct)
        => Token(await LeerAsync(await _http.GetAsync(
            $"{_o.UrlGraph.TrimEnd('/')}/refresh_access_token?grant_type=ig_refresh_token&access_token={Uri.EscapeDataString(tokenLargo)}", ct), ct));

    public async Task<PerfilIg> PerfilAsync(string token, CancellationToken ct)
    {
        var json = await LeerAsync(await _http.GetAsync(Graph($"me?fields=user_id,username&access_token={Uri.EscapeDataString(token)}"), ct), ct);
        var id = json.TryGetProperty("user_id", out var uid) ? Texto(uid) : json.TryGetProperty("id", out var i) ? Texto(i) : null;
        return new PerfilIg(id ?? throw new InstagramRechazoException("Instagram no devolvió la cuenta."), json.GetProperty("username").GetString() ?? string.Empty);
    }

    public async Task<IReadOnlyList<MediaIg>> PublicacionesAsync(string token, int limite, CancellationToken ct)
    {
        const string campos = "id,caption,media_type,media_url,thumbnail_url,permalink,timestamp,children{media_type,media_url,thumbnail_url}";
        var json = await LeerAsync(await _http.GetAsync(Graph($"me/media?fields={Uri.EscapeDataString(campos)}&limit={limite}&access_token={Uri.EscapeDataString(token)}"), ct), ct);
        var lista = new List<MediaIg>();
        foreach (var m in json.GetProperty("data").EnumerateArray())
        {
            var hijos = new List<HijoIg>();
            if (m.TryGetProperty("children", out var c) && c.TryGetProperty("data", out var cd))
                foreach (var h in cd.EnumerateArray())
                    hijos.Add(new HijoIg(Cadena(h, "media_type") ?? "IMAGE", Cadena(h, "media_url"), Cadena(h, "thumbnail_url")));
            lista.Add(new MediaIg(
                Texto(m.GetProperty("id"))!, Cadena(m, "caption"), Cadena(m, "media_type") ?? "IMAGE", Cadena(m, "media_url"), Cadena(m, "thumbnail_url"),
                Cadena(m, "permalink"), Fecha(Cadena(m, "timestamp")), hijos));
        }
        return lista;
    }

    public async Task<byte[]> DescargarAsync(string url, CancellationToken ct)
    {
        // Solo direcciones https (las de la CDN de Instagram): nunca seguir lo que venga en otro esquema.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || !(u.Scheme == Uri.UriSchemeHttps || !_o.SoloHttps && u.Scheme == Uri.UriSchemeHttp))
            throw new InstagramRechazoException("La foto de Instagram tiene una dirección inválida.");
        using var r = await _http.GetAsync(u, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!r.IsSuccessStatusCode) throw new InstagramRechazoException("No se pudo bajar una foto de Instagram.");
        if (r.Content.Headers.ContentLength is > MaximoFoto) throw new InstagramRechazoException("Una foto de Instagram pesa demasiado.");
        await using var flujo = await r.Content.ReadAsStreamAsync(ct);
        using var copia = new MemoryStream();
        var buffer = new byte[81920];
        int leidos;
        while ((leidos = await flujo.ReadAsync(buffer, ct)) > 0)
        {
            copia.Write(buffer, 0, leidos);
            if (copia.Length > MaximoFoto) throw new InstagramRechazoException("Una foto de Instagram pesa demasiado.");
        }
        return copia.ToArray();
    }

    // ───────────── Respuestas y errores de Meta ─────────────

    private static async Task<JsonElement> LeerAsync(HttpResponseMessage r, CancellationToken ct)
    {
        using (r)
        {
            var texto = await r.Content.ReadAsStringAsync(ct);
            JsonElement json;
            try { json = JsonDocument.Parse(texto).RootElement.Clone(); }
            catch (JsonException) { throw new InstagramRechazoException($"Instagram respondió algo que no se entiende ({(int)r.StatusCode})."); }
            if (r.IsSuccessStatusCode && !json.TryGetProperty("error", out _) && !json.TryGetProperty("error_type", out _)) return json;
            throw Error(json, (int)r.StatusCode);
        }
    }

    // Meta manda { "error": { "message", "type", "code" } } (Graph) o { "error_type", "code", "error_message" } (OAuth).
    // Código 190 = token inválido/vencido/revocado: hay que volver a conectar.
    internal static InstagramRechazoException Error(JsonElement json, int status)
    {
        var e = json.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object ? err : json;
        var codigo = e.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
        var mensaje = Cadena(e, "message") ?? Cadena(e, "error_message") ?? $"error {status}";
        var tipo = Cadena(e, "type") ?? Cadena(e, "error_type") ?? string.Empty;
        var tokenInvalido = codigo == 190 || tipo == "OAuthException" && status == 401;
        return new InstagramRechazoException(tokenInvalido
            ? "Instagram dice que la conexión ya no vale (se venció o se revocó el permiso). Volvé a conectar tu cuenta."
            : $"Instagram no aceptó el pedido: {mensaje}", tokenInvalido);
    }

    private static TokenIg Token(JsonElement json)
        => new(json.GetProperty("access_token").GetString()!, json.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : null);

    private static string? Cadena(JsonElement e, string nombre) => e.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static string? Texto(JsonElement v) => v.ValueKind == JsonValueKind.Number ? v.GetRawText() : v.GetString();

    // "2026-10-14T18:30:00+0000"
    private static DateTimeOffset Fecha(string? t)
        => DateTimeOffset.TryParseExact(t, ["yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:sszz", "yyyy-MM-dd'T'HH:mm:ssK"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var f)
            || DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out f)
            ? f.ToUniversalTime() : DateTimeOffset.MinValue;
}

// Cifrado de los tokens en la base (AES-GCM, clave de Instagram:ClaveCifrado) y firma del `state` del OAuth (HMAC):
// si alguien lee la base, no puede usar los tokens; si alguien arma un `state`, no puede atar su cuenta a otra tienda.
public class SecretosInstagram
{
    private readonly byte[] _clave;

    public SecretosInstagram(IOptions<InstagramOptions> opciones)
    {
        var texto = opciones.Value.ClaveCifrado;
        _clave = texto.Length == 0 ? [] : Convert.FromBase64String(texto);
        if (_clave.Length is not (0 or 32)) throw new InvalidOperationException("Instagram:ClaveCifrado tiene que ser de 32 bytes en base64 (openssl rand -base64 32).");
    }

    private byte[] Clave => _clave.Length == 32 ? _clave : throw new InvalidOperationException("Falta Instagram:ClaveCifrado.");

    public string Cifrar(string token)
    {
        var plano = Encoding.UTF8.GetBytes(token);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cifrado = new byte[plano.Length];
        var etiqueta = new byte[16];
        using var aes = new AesGcm(Clave, 16);
        aes.Encrypt(nonce, plano, cifrado, etiqueta);
        return "v1:" + Convert.ToBase64String([.. nonce, .. cifrado, .. etiqueta]);
    }

    public string Descifrar(string guardado)
    {
        if (!guardado.StartsWith("v1:", StringComparison.Ordinal)) throw new InvalidOperationException("Token con un formato desconocido.");
        var datos = Convert.FromBase64String(guardado[3..]);
        var cifrado = datos.AsSpan(12, datos.Length - 28);
        var plano = new byte[cifrado.Length];
        using var aes = new AesGcm(Clave, 16);
        aes.Decrypt(datos.AsSpan(0, 12), cifrado, datos.AsSpan(datos.Length - 16), plano);
        return Encoding.UTF8.GetString(plano);
    }

    // state = "{tienda}.{vence unix}.{firma}": vale 15 minutos y solo para esa tienda.
    public string FirmarEstado(Guid tienda, DateTimeOffset vence)
    {
        var cuerpo = $"{tienda:N}.{vence.ToUnixTimeSeconds()}";
        return $"{cuerpo}.{Firma(cuerpo)}";
    }

    public bool EstadoValido(string? estado, Guid tienda, DateTimeOffset ahora)
    {
        var partes = (estado ?? string.Empty).Split('.');
        if (partes.Length != 3 || !Guid.TryParseExact(partes[0], "N", out var t) || t != tienda || !long.TryParse(partes[1], out var vence)) return false;
        if (ahora.ToUnixTimeSeconds() > vence) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Firma($"{partes[0]}.{partes[1]}")), Encoding.ASCII.GetBytes(partes[2]));
    }

    private string Firma(string cuerpo)
        => Convert.ToBase64String(HMACSHA256.HashData(Clave, Encoding.UTF8.GetBytes("estado:" + cuerpo))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
