using SkiaSharp;

namespace Hermes.UnitTests;

public static class FotoDePrueba
{
    // Un JPG liso de verdad (se decodifica, se achica y se guarda como cualquier foto).
    public static byte[] Jpeg(int ancho = 400, int alto = 300)
    {
        using var bitmap = new SKBitmap(ancho, alto);
        bitmap.Erase(SKColors.SteelBlue);
        using var imagen = SKImage.FromBitmap(bitmap);
        return imagen.Encode(SKEncodedImageFormat.Jpeg, 85).ToArray();
    }
}
