using Hermes.Application.Exceptions;
using Hermes.Domain.Enums;
using SkiaSharp;

namespace Hermes.Infrastructure.Imagenes;

public record ImagenProcesada(int Ancho, int Alto, IReadOnlyDictionary<TamanoImagen, byte[]> Tamanos);

// Toma los bytes que subió el usuario y devuelve 3 versiones WEBP, sin metadatos (no queda el GPS
// de la foto) y con la orientación corregida. Nunca confía en la extensión ni en el Content-Type:
// el formato se decide por los primeros bytes y después se decodifica de verdad.
public static class ProcesadorImagenes
{
    public const int MaximoBytes = 15 * 1024 * 1024;
    // Una foto de celular actual ronda 12–50 MP; por encima de esto es otra cosa (o un ataque de
    // "bomba de descompresión": un archivo chico que al abrirse ocupa gigas de memoria).
    public const long MaximoPixeles = 64_000_000;
    public const int LadoMinimo = 50;

    public static readonly IReadOnlyDictionary<TamanoImagen, (int Lado, int Calidad)> Medidas =
        new Dictionary<TamanoImagen, (int, int)>
        {
            [TamanoImagen.Miniatura] = (200, 70),
            [TamanoImagen.Catalogo] = (600, 78),
            [TamanoImagen.Detalle] = (1200, 82),
        };

    public static ImagenProcesada Procesar(byte[] contenido)
    {
        if (contenido.Length == 0)
            throw new ImagenInvalidaException("El archivo está vacío.");
        if (contenido.Length > MaximoBytes)
            throw new ImagenInvalidaException($"La foto pesa demasiado (máximo {MaximoBytes / 1024 / 1024} MB).");
        if (!EsFormatoPermitido(contenido))
            throw new ImagenInvalidaException("Solo se aceptan fotos JPG, PNG o WEBP.");

        using var datos = SKData.CreateCopy(contenido);
        using var codec = SKCodec.Create(datos)
            ?? throw new ImagenInvalidaException("No pudimos leer la foto. Probá con otra.");

        var info = codec.Info;
        if ((long)info.Width * info.Height > MaximoPixeles)
            throw new ImagenInvalidaException("La foto es demasiado grande. Probá con una de menor resolución.");

        // Decodificación COMPLETA o nada. SKBitmap.Decode(codec) acepta un archivo cortado a la mitad
        // (conexión que se cae a mitad de subida) y rellena el resto con gris: se guardaría una foto
        // rota sin avisar. Por eso se pide el resultado y solo Success vale.
        using var decodificada = new SKBitmap(info);
        var estado = codec.GetPixels(decodificada.Info, decodificada.GetPixels());
        if (estado != SKCodecResult.Success)
            throw new ImagenInvalidaException("La foto llegó incompleta o está dañada. Probá subirla de nuevo.");
        using var derecha = Orientar(decodificada, codec.EncodedOrigin);

        if (Math.Min(derecha.Width, derecha.Height) < LadoMinimo)
            throw new ImagenInvalidaException($"La foto es muy chica (mínimo {LadoMinimo} px de lado).");

        var resultado = new Dictionary<TamanoImagen, byte[]>();
        foreach (var (tamano, (lado, calidad)) in Medidas)
            resultado[tamano] = Codificar(derecha, lado, calidad);

        return new ImagenProcesada(derecha.Width, derecha.Height, resultado);
    }

    // JPG: FF D8 FF · PNG: 89 50 4E 47 0D 0A 1A 0A · WEBP: "RIFF"....."WEBP"
    public static bool EsFormatoPermitido(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return true;
        if (b.Length >= 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return true;
        return b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b.Slice(8, 4).SequenceEqual("WEBP"u8);
    }

    // Las fotos del celular guardan "girala 90°" en los metadatos en vez de girar los píxeles.
    internal static SKBitmap Orientar(SKBitmap origen, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft || origin == SKEncodedOrigin.Default)
            return origen.Copy();

        var intercambia = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var ancho = intercambia ? origen.Height : origen.Width;
        var alto = intercambia ? origen.Width : origen.Height;

        var destino = new SKBitmap(ancho, alto, origen.ColorType, origen.AlphaType);
        using var lienzo = new SKCanvas(destino);
        lienzo.Clear(SKColors.Transparent);

        var w = origen.Width;
        var h = origen.Height;
        // Matriz que lleva cada píxel de la imagen guardada a su lugar correcto (tabla EXIF 1–8).
        var matriz = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => SKMatrix.Identity,
        };
        lienzo.SetMatrix(matriz);
        // Giros y espejos de 90° son exactos: sin interpolar (Nearest) no se pierde nitidez.
        lienzo.DrawBitmap(origen, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        return destino;
    }

    private static byte[] Codificar(SKBitmap origen, int ladoMaximo, int calidad)
    {
        var mayor = Math.Max(origen.Width, origen.Height);
        // Nunca se agranda una foto: una de 500 px queda en 500 px en todos los tamaños.
        var escala = mayor > ladoMaximo ? (double)ladoMaximo / mayor : 1d;
        var ancho = Math.Max(1, (int)Math.Round(origen.Width * escala));
        var alto = Math.Max(1, (int)Math.Round(origen.Height * escala));

        using var achicada = escala < 1d
            ? origen.Resize(new SKImageInfo(ancho, alto, origen.ColorType, origen.AlphaType),
                new SKSamplingOptions(SKCubicResampler.Mitchell))
            : origen.Copy();
        if (achicada is null) throw new ImagenInvalidaException("No pudimos procesar la foto.");

        using var imagen = SKImage.FromBitmap(achicada);
        // Codificar desde cero deja afuera los metadatos (EXIF/GPS) de la original.
        using var webp = imagen.Encode(SKEncodedImageFormat.Webp, calidad)
            ?? throw new ImagenInvalidaException("No pudimos procesar la foto.");
        return webp.ToArray();
    }
}
