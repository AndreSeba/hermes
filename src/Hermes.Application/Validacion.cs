using Hermes.Application.Exceptions;

namespace Hermes.Application;

// Validación de texto de entrada: un vacío o un texto más largo que la columna es un 400
// con mensaje claro, nunca un 500 de la base.
public static class Validacion
{
    // Obligatorio: sin espacios sobrantes, no vacío, con tope de largo.
    public static string Texto(string? valor, int maximo, string campo)
    {
        var limpio = (valor ?? string.Empty).Trim();
        if (limpio.Length == 0)
            throw new ValidacionException($"{campo} es obligatorio.");
        if (limpio.Length > maximo)
            throw new ValidacionException($"{campo} no puede superar los {maximo} caracteres.");
        return limpio;
    }

    // Siempre en minúsculas: el índice único de usuarios.email compara texto exacto.
    public static string Email(string? valor)
    {
        var email = Texto(valor, 254, "El email").ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            throw new ValidacionException("El email no tiene un formato válido.");
        return email;
    }

    // 8 a 72 caracteres (72 es el límite real de BCrypt) con al menos una letra y un número.
    public static string Password(string? valor)
    {
        var password = valor ?? string.Empty;
        if (password.Length is < 8 or > 72)
            throw new ValidacionException("La contraseña debe tener entre 8 y 72 caracteres.");
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            throw new ValidacionException("La contraseña debe tener al menos una letra y un número.");
        return password;
    }

    // Dinero en Bs: hasta 2 decimales (se rechaza, no se redondea en silencio: 10,555 no es 10,56) y un
    // tope para que un cero de más no pase. `permitirCero`: el costo puede ser 0 ("todavía no lo cargué").
    public static decimal Monto(decimal valor, string campo, bool permitirCero)
    {
        if (valor != decimal.Round(valor, 2))
            throw new ValidacionException($"{campo} puede tener hasta 2 decimales.");
        if (valor < 0 || (!permitirCero && valor == 0))
            throw new ValidacionException(permitirCero ? $"{campo} no puede ser negativo." : $"{campo} tiene que ser mayor a 0.");
        if (valor > 1_000_000m)
            throw new ValidacionException($"{campo} es demasiado alto.");
        return valor;
    }

    // Lista de textos cortos ("café", "negro"): sin espacios sobrantes, sin vacíos y sin repetidos
    // (sin distinguir mayúsculas: "Rojo" y "rojo" son lo mismo; queda la primera forma escrita).
    public static List<string> ListaDeTextos(IReadOnlyList<string>? valores, int maximoLargo, string campo)
    {
        var resultado = new List<string>();
        foreach (var crudo in valores ?? [])
        {
            var texto = (crudo ?? string.Empty).Trim();
            if (texto.Length == 0) continue;
            if (texto.Length > maximoLargo)
                throw new ValidacionException($"Cada {campo} puede tener hasta {maximoLargo} caracteres.");
            if (!resultado.Contains(texto, StringComparer.OrdinalIgnoreCase)) resultado.Add(texto);
        }
        return resultado;
    }

    // Celular de Bolivia (8 dígitos, empieza con 6 o 7) con o sin +591, espacios, guiones o
    // paréntesis. Se guarda SIEMPRE como "+591" + 8 dígitos: el enlace wa.me usa esos mismos dígitos.
    // Opcional: vacío = sin WhatsApp.
    public static string? Whatsapp(string? valor)
    {
        var texto = valor?.Trim();
        if (string.IsNullOrEmpty(texto)) return null;
        if (texto.Any(c => !char.IsDigit(c) && !" +-().".Contains(c)))
            throw new ValidacionException("El WhatsApp solo puede tener números.");

        var digitos = new string(texto.Where(char.IsDigit).ToArray());
        if (digitos.StartsWith("591") && digitos.Length == 11) digitos = digitos[3..];
        if (!System.Text.RegularExpressions.Regex.IsMatch(digitos, "^[67][0-9]{7}$"))
            throw new ValidacionException("Ingresá tu celular de Bolivia: 8 dígitos que empiecen con 6 o 7.");
        return "+591" + digitos;
    }

    // "#RRGGBB", guardado en mayúsculas.
    public static string ColorHex(string? valor)
    {
        var color = (valor ?? string.Empty).Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$"))
            throw new ValidacionException("El color no es válido.");
        return color.ToUpperInvariant();
    }

    // Rutas propias de la app que no pueden ser la dirección de una tienda.
    private static readonly HashSet<string> SlugsReservados =
    [
        "api", "public", "admin", "app", "www", "hermes", "login", "registro", "ayuda",
        "soporte", "precios", "planes", "tienda", "tiendas", "static", "assets", "scalar", "openapi",
        // Rutas de la tienda pública (Hermes.Public).
        "error", "archivos", "css", "js", "og",
    ];

    // hermes.bo/{slug}: minúsculas, números y guiones; 2 a 40 caracteres; empieza con letra o número.
    public static string Slug(string? valor)
    {
        var slug = Texto(valor, 40, "La dirección de la tienda").ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9][a-z0-9-]{1,39}$"))
            throw new ValidacionException("La dirección solo puede tener letras minúsculas, números y guiones (mínimo 2 caracteres).");
        if (SlugsReservados.Contains(slug))
            throw new ValidacionException($"La dirección '{slug}' está reservada. Probá con otra.");
        return slug;
    }

    // Opcional: vacío o solo espacios se guarda como null.
    public static string? TextoOpcional(string? valor, int maximo, string campo)
    {
        var limpio = valor?.Trim();
        if (string.IsNullOrEmpty(limpio)) return null;
        if (limpio.Length > maximo)
            throw new ValidacionException($"{campo} no puede superar los {maximo} caracteres.");
        return limpio;
    }
}
