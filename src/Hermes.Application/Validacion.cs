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

    // Rutas propias de la app que no pueden ser la dirección de una tienda.
    private static readonly HashSet<string> SlugsReservados =
    [
        "api", "public", "admin", "app", "www", "hermes", "login", "registro", "ayuda",
        "soporte", "precios", "planes", "tienda", "tiendas", "static", "assets", "scalar", "openapi",
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
