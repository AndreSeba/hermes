using Hermes.Domain.Enums;

namespace Hermes.Public;

// Los colores de una tienda: el de su marca (lo elige la dueña) y el fondo (Claro / Crema / Oscuro). El texto sobre
// el color de marca se elige solo para que se lea (claro u oscuro según qué tan claro es el color).
public static class Tema
{
    public record Paleta(string Fondo, string Superficie, string Texto, string Suave, string Borde);

    public static Paleta DeFondo(FondoTienda fondo) => fondo switch
    {
        FondoTienda.Claro => new("#FFFFFF", "#F6F5F1", "#17181C", "#6B6E76", "#E6E4DE"),
        FondoTienda.Oscuro => new("#17181C", "#23252B", "#F6F5F1", "#A3A6AE", "#34363D"),
        _ => new("#F6F5F1", "#FFFFFF", "#17181C", "#6B6E76", "#E6E4DE"),
    };

    // Luminancia relativa (WCAG) del color "#RRGGBB" de la marca. Ya viene validado por el servidor.
    public static string TextoSobre(string hex)
    {
        static double Canal(string h, int i)
        {
            var c = Convert.ToInt32(h.Substring(1 + i * 2, 2), 16) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        var l = 0.2126 * Canal(hex, 0) + 0.7152 * Canal(hex, 1) + 0.0722 * Canal(hex, 2);
        return l > 0.4 ? "#17181C" : "#FFFFFF";
    }
}
