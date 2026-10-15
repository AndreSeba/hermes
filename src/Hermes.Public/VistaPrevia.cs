using Hermes.Application.Interfaces;
using Hermes.Domain.Enums;
using SkiaSharp;

namespace Hermes.Public;

// La imagen que muestran WhatsApp & co. cuando se pega el enlace: 1200×630 (la proporción que esperan), la foto del
// producto (o la portada de la tienda, o su logo) entera sobre el fondo de la tienda, en JPEG.
public static class VistaPrevia
{
    private const int Ancho = 1200;
    private const int Alto = 630;

    public static async Task<byte[]> GenerarAsync(string slug, Guid? productoId, IPublicoService publico, Urls urls, IAlmacenArchivos almacen, CancellationToken ct)
    {
        var tienda = await publico.ObtenerTiendaAsync(slug, ct); // 404 si no existe o está apagada
        string? urlFoto = null;
        if (productoId is { } id)
            urlFoto = (await publico.ProductoAsync(slug, id, ct)).Fotos.FirstOrDefault()?.Detalle;
        urlFoto ??= tienda.Logo?.Detalle;
        urlFoto ??= (await publico.ProductosAsync(slug, new(Limite: 1), ct)).Items.FirstOrDefault()?.Portada?.Detalle;

        var paleta = Tema.DeFondo(tienda.Fondo);
        using var lienzo = new SKBitmap(Ancho, Alto);
        using var canvas = new SKCanvas(lienzo);
        canvas.Clear(SKColor.Parse(paleta.Superficie));

        SKBitmap? foto = null;
        if (urlFoto is not null && almacen.ClaveDeUrl(urlFoto) is { } clave && await almacen.ObtenerAsync(clave, ct) is { } archivo)
            foto = SKBitmap.Decode(archivo.Datos);
        using (foto)
        {
            if (foto is not null)
            {
                // Entera (sin recortar): se escala para entrar en 1200×630 y queda centrada.
                var escala = Math.Min((float)Ancho / foto.Width, (float)Alto / foto.Height);
                var w = foto.Width * escala;
                var h = foto.Height * escala;
                using var pintura = new SKPaint { IsAntialias = true };
                canvas.DrawBitmap(foto, new SKRect((Ancho - w) / 2, (Alto - h) / 2, (Ancho + w) / 2, (Alto + h) / 2), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), pintura);
            }
            else
            {
                // Sin ninguna foto: el color de la marca con el nombre de la tienda.
                canvas.Clear(SKColor.Parse(tienda.Color));
                using var fuente = new SKFont(SKTypeface.Default, 96);
                using var pintura = new SKPaint { Color = SKColor.Parse(Tema.TextoSobre(tienda.Color)), IsAntialias = true };
                canvas.DrawText(tienda.Nombre, Ancho / 2f, Alto / 2f + 32, SKTextAlign.Center, fuente, pintura);
            }
        }

        using var imagen = SKImage.FromBitmap(lienzo);
        using var datos = imagen.Encode(SKEncodedImageFormat.Jpeg, 82);
        return datos.ToArray();
    }
}
