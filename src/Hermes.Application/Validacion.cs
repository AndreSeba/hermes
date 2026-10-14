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
