using System.Text;
using Hermes.Application.Exceptions;
using Hermes.Domain.Entities;
using Hermes.Domain.Enums;
using Hermes.Infrastructure.Imagenes;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace Hermes.UnitTests;

public class ImagenTests : IDisposable
{
    private readonly BaseDePrueba _bd = new();

    public void Dispose() => _bd.Dispose();

    // Imagen con ruido para que no comprima a nada (se parece más a una foto real).
    private static byte[] Foto(int ancho, int alto, SKEncodedImageFormat formato = SKEncodedImageFormat.Jpeg)
    {
        using var bitmap = new SKBitmap(ancho, alto);
        var azar = new Random(7);
        for (var y = 0; y < alto; y++)
            for (var x = 0; x < ancho; x++)
                bitmap.SetPixel(x, y, new SKColor((byte)azar.Next(256), (byte)(x * 255 / ancho), (byte)(y * 255 / alto)));
        using var imagen = SKImage.FromBitmap(bitmap);
        return imagen.Encode(formato, 90).ToArray();
    }

    private static (int Ancho, int Alto) Medidas(byte[] webp)
    {
        using var codec = SKCodec.Create(SKData.CreateCopy(webp))!;
        return (codec.Info.Width, codec.Info.Height);
    }

    private static bool EsWebp(byte[] b) => ProcesadorImagenes.EsFormatoPermitido(b) && b[0] == 'R' && b[8] == 'W';

    private ImagenService Servicio(HermesDbContext db, Guid? tienda)
        => new(db, new AlmacenEnBaseDeDatos(db, TimeProvider.System), new BaseDePrueba.TiendaFija(tienda), TimeProvider.System);

    // ───────────── Procesador ─────────────

    [Fact]
    public void Una_foto_grande_sale_en_tres_tamanos_webp_proporcionales()
    {
        var resultado = ProcesadorImagenes.Procesar(Foto(3000, 2000));

        Assert.Equal((3000, 2000), (resultado.Ancho, resultado.Alto));
        Assert.Equal((200, 133), Medidas(resultado.Tamanos[TamanoImagen.Miniatura]));
        Assert.Equal((600, 400), Medidas(resultado.Tamanos[TamanoImagen.Catalogo]));
        Assert.Equal((1200, 800), Medidas(resultado.Tamanos[TamanoImagen.Detalle]));
        Assert.All(resultado.Tamanos.Values, b => Assert.True(EsWebp(b)));
    }

    [Fact]
    public void Una_foto_vertical_se_acota_por_su_lado_largo()
    {
        var resultado = ProcesadorImagenes.Procesar(Foto(1500, 3000));
        Assert.Equal((600, 1200), Medidas(resultado.Tamanos[TamanoImagen.Detalle]));
        Assert.Equal((100, 200), Medidas(resultado.Tamanos[TamanoImagen.Miniatura]));
    }

    [Fact]
    public void Nunca_se_agranda_una_foto_chica()
    {
        var resultado = ProcesadorImagenes.Procesar(Foto(300, 200));

        Assert.Equal((300, 200), Medidas(resultado.Tamanos[TamanoImagen.Detalle]));
        Assert.Equal((300, 200), Medidas(resultado.Tamanos[TamanoImagen.Catalogo]));
        Assert.Equal((200, 133), Medidas(resultado.Tamanos[TamanoImagen.Miniatura]));
    }

    [Fact]
    public void Acepta_png_y_webp_ademas_de_jpg()
    {
        Assert.NotNull(ProcesadorImagenes.Procesar(Foto(400, 300, SKEncodedImageFormat.Png)));
        Assert.NotNull(ProcesadorImagenes.Procesar(Foto(400, 300, SKEncodedImageFormat.Webp)));
    }

    [Fact]
    public void Un_archivo_falso_con_extension_de_foto_se_rechaza()
    {
        var texto = Encoding.UTF8.GetBytes("esto no es una foto, solo se llama foto.jpg");
        Assert.Throws<ImagenInvalidaException>(() => ProcesadorImagenes.Procesar(texto));

        // Un ejecutable también.
        var exe = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0, 0, 0, 4, 0, 0, 0 };
        Assert.Throws<ImagenInvalidaException>(() => ProcesadorImagenes.Procesar(exe));
    }

    [Fact]
    public void Un_gif_no_esta_permitido()
    {
        var gif = Encoding.ASCII.GetBytes("GIF89a").Concat(new byte[100]).ToArray();
        Assert.Throws<ImagenInvalidaException>(() => ProcesadorImagenes.Procesar(gif));
    }

    [Fact]
    public void Una_foto_cortada_por_la_mitad_no_pasa_como_valida()
    {
        var entera = Foto(800, 600);
        // Solo la firma: ni se puede abrir.
        Assert.Throws<ImagenInvalidaException>(() => ProcesadorImagenes.Procesar(entera[..40]));
        // Cabecera completa pero faltan datos (la conexión se cayó a mitad de subida): Skia
        // decodifica lo que hay y rellena el resto; eso NO se acepta como foto buena.
        var mitad = Assert.Throws<ImagenInvalidaException>(() => ProcesadorImagenes.Procesar(entera[..(entera.Length / 2)]));
        Assert.Contains("incompleta", mitad.Message);
        // PNG igual.
        var png = Foto(800, 600, SKEncodedImageFormat.Png);
        Assert.Throws<ImagenInvalidaException>(() => ProcesadorImagenes.Procesar(png[..(png.Length / 2)]));
    }

    [Fact]
    public void Vacio_demasiado_pesado_y_demasiado_chico_se_rechazan_con_su_mensaje()
    {
        var vacio = Assert.Throws<ImagenInvalidaException>(() => ProcesadorImagenes.Procesar([]));
        Assert.Contains("vacío", vacio.Message);

        var pesado = new byte[ProcesadorImagenes.MaximoBytes + 1];
        pesado[0] = 0xFF; pesado[1] = 0xD8; pesado[2] = 0xFF;
        var ex = Assert.Throws<ImagenInvalidaException>(() => ProcesadorImagenes.Procesar(pesado));
        Assert.Contains("pesa demasiado", ex.Message);

        var chico = Assert.Throws<ImagenInvalidaException>(() => ProcesadorImagenes.Procesar(Foto(30, 30)));
        Assert.Contains("muy chica", chico.Message);
    }

    [Fact]
    public void Los_metadatos_con_la_ubicacion_no_pasan_a_la_foto_guardada()
    {
        // Se inserta un bloque EXIF (APP1) con un texto reconocible, como el GPS que trae un celular.
        var jpg = Foto(600, 400);
        var carga = Encoding.ASCII.GetBytes("Exif\0\0GPS-LATITUD--16.5000-LONGITUD--68.1500");
        var largo = carga.Length + 2;
        var conExif = jpg[..2]
            .Concat(new byte[] { 0xFF, 0xE1, (byte)(largo >> 8), (byte)(largo & 0xFF) })
            .Concat(carga)
            .Concat(jpg[2..])
            .ToArray();
        Assert.Contains("GPS-LATITUD", Encoding.ASCII.GetString(conExif)); // la original SÍ lo trae

        var resultado = ProcesadorImagenes.Procesar(conExif);

        foreach (var bytes in resultado.Tamanos.Values)
        {
            var texto = Encoding.ASCII.GetString(bytes);
            Assert.DoesNotContain("GPS-LATITUD", texto);
            Assert.DoesNotContain("EXIF", texto);
        }
    }

    // Tabla EXIF 1–8: dónde cae cada píxel guardado (x, y) en una imagen guardada de 3×2.
    [Theory]
    [InlineData(SKEncodedOrigin.TopLeft, 3, 2, 1, 0, 1, 0)]       // normal
    [InlineData(SKEncodedOrigin.TopRight, 3, 2, 0, 0, 2, 0)]      // espejo horizontal
    [InlineData(SKEncodedOrigin.BottomRight, 3, 2, 0, 0, 2, 1)]   // girada 180°
    [InlineData(SKEncodedOrigin.BottomLeft, 3, 2, 0, 0, 0, 1)]    // espejo vertical
    [InlineData(SKEncodedOrigin.LeftTop, 2, 3, 2, 0, 0, 2)]       // transpuesta
    [InlineData(SKEncodedOrigin.RightTop, 2, 3, 0, 0, 1, 0)]      // 90° a la derecha (lo típico del celular)
    [InlineData(SKEncodedOrigin.RightBottom, 2, 3, 0, 0, 1, 2)]   // transversa
    [InlineData(SKEncodedOrigin.LeftBottom, 2, 3, 0, 0, 0, 2)]    // 90° a la izquierda
    public void La_orientacion_del_celular_se_corrige(SKEncodedOrigin origen, int anchoEsperado, int altoEsperado,
        int xGuardado, int yGuardado, int xFinal, int yFinal)
    {
        using var guardada = new SKBitmap(3, 2);
        for (var y = 0; y < 2; y++)
            for (var x = 0; x < 3; x++)
                guardada.SetPixel(x, y, new SKColor((byte)(x * 80 + 10), (byte)(y * 120 + 10), 200)); // color único por píxel

        using var corregida = ProcesadorImagenes.Orientar(guardada, origen);

        Assert.Equal((anchoEsperado, altoEsperado), (corregida.Width, corregida.Height));
        Assert.Equal(guardada.GetPixel(xGuardado, yGuardado), corregida.GetPixel(xFinal, yFinal));
    }

    // ───────────── Servicio ─────────────

    [Fact]
    public async Task Subir_guarda_la_imagen_y_sus_tres_archivos_con_la_tienda_en_la_clave()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var dto = await Servicio(db, _bd.TiendaA).SubirAsync(Foto(1600, 1200), default);

        Assert.Equal((1600, 1200), (dto.Ancho, dto.Alto));
        Assert.Equal($"/archivos/{_bd.TiendaA}/imagenes/{dto.Id}/miniatura.webp", dto.Miniatura);
        Assert.Equal(3, await db.Archivos.CountAsync());
        Assert.All(await db.Archivos.ToListAsync(), a =>
        {
            Assert.StartsWith($"{_bd.TiendaA}/imagenes/{dto.Id}/", a.Clave);
            Assert.Equal("image/webp", a.ContentType);
            Assert.Equal(a.Datos.Length, a.Bytes);
        });
        Assert.Equal((await db.Archivos.SumAsync(a => a.Bytes)), (await db.Imagenes.SingleAsync()).BytesGuardados);
    }

    [Fact]
    public async Task Una_foto_invalida_no_deja_nada_guardado()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        await Assert.ThrowsAsync<ImagenInvalidaException>(() =>
            Servicio(db, _bd.TiendaA).SubirAsync(Encoding.UTF8.GetBytes("no soy una foto"), default));

        Assert.Empty(await db.Imagenes.ToListAsync());
        Assert.Empty(await db.Archivos.ToListAsync());
    }

    [Fact]
    public async Task Sin_tienda_en_sesion_no_se_puede_subir()
    {
        await using var db = _bd.Contexto(null);
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => Servicio(db, null).SubirAsync(Foto(400, 300), default));
    }

    [Fact]
    public async Task Borrar_elimina_la_imagen_y_sus_archivos()
    {
        Guid id;
        await using (var db = _bd.Contexto(_bd.TiendaA))
            id = (await Servicio(db, _bd.TiendaA).SubirAsync(Foto(400, 300), default)).Id;

        await using (var db = _bd.Contexto(_bd.TiendaA))
            await Servicio(db, _bd.TiendaA).BorrarAsync(id, default);

        await using var verificar = _bd.Contexto(_bd.TiendaA);
        Assert.Empty(await verificar.Imagenes.ToListAsync());
        Assert.Empty(await verificar.Archivos.ToListAsync());
    }

    [Fact]
    public async Task Una_tienda_no_puede_borrar_la_foto_de_otra()
    {
        Guid id;
        await using (var db = _bd.Contexto(_bd.TiendaA))
            id = (await Servicio(db, _bd.TiendaA).SubirAsync(Foto(400, 300), default)).Id;

        await using (var db = _bd.Contexto(_bd.TiendaB))
        {
            await Assert.ThrowsAsync<ImagenNoEncontradaException>(() => Servicio(db, _bd.TiendaB).BorrarAsync(id, default));
            Assert.Empty(await db.Imagenes.ToListAsync()); // ni siquiera la ve
        }

        await using var verificar = _bd.Contexto(_bd.TiendaA);
        Assert.Single(await verificar.Imagenes.ToListAsync());
        Assert.Equal(3, await verificar.Archivos.CountAsync()); // sus archivos siguen
    }

    [Fact]
    public async Task El_almacen_devuelve_lo_guardado_y_nulo_si_no_existe()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var dto = await Servicio(db, _bd.TiendaA).SubirAsync(Foto(400, 300), default);
        var almacen = new AlmacenEnBaseDeDatos(db, TimeProvider.System);

        var clave = Imagen.ClaveDe(_bd.TiendaA, dto.Id, TamanoImagen.Catalogo);
        var archivo = await almacen.ObtenerAsync(clave, default);
        Assert.NotNull(archivo);
        Assert.Equal("image/webp", archivo.ContentType);
        Assert.True(EsWebp(archivo.Datos));
        Assert.Null(await almacen.ObtenerAsync("no/existe.webp", default));
    }
}
