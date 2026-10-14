using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Domain.Entities;
using Hermes.Infrastructure.Imagenes;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace Hermes.UnitTests;

public class TiendaTests : IDisposable
{
    private readonly BaseDePrueba _bd = new();

    public void Dispose() => _bd.Dispose();

    private static ActualizarTiendaDto Datos(string nombre = "Luna Accesorios", string? descripcion = null,
        string? whatsapp = null, string color = "#F2A33A", Guid? logo = null)
        => new(nombre, descripcion, whatsapp, color, logo);

    private (TiendaService Tiendas, ImagenService Imagenes) Servicios(HermesDbContext db, Guid? tienda)
    {
        var almacen = new AlmacenEnBaseDeDatos(db, TimeProvider.System);
        var sesion = new BaseDePrueba.TiendaFija(tienda);
        var imagenes = new ImagenService(db, almacen, sesion, TimeProvider.System);
        return (new TiendaService(db, imagenes, almacen, sesion), imagenes);
    }

    private static byte[] Foto()
    {
        using var bitmap = new SKBitmap(400, 300);
        bitmap.Erase(SKColors.SteelBlue);
        using var imagen = SKImage.FromBitmap(bitmap);
        return imagen.Encode(SKEncodedImageFormat.Jpeg, 85).ToArray();
    }

    // ───────────── Ver y editar ─────────────

    [Fact]
    public async Task Obtener_devuelve_la_tienda_de_la_sesion_y_no_la_de_otra()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var (tiendas, _) = Servicios(db, _bd.TiendaA);

        var dto = await tiendas.ObtenerAsync(default);

        Assert.Equal(_bd.TiendaA, dto.Id);
        Assert.Equal("tienda-a", dto.Slug);
        Assert.Equal(Tienda.ColorPorDefecto, dto.Color);
        Assert.Null(dto.Logo);
        Assert.Null(dto.Whatsapp);
    }

    [Fact]
    public async Task Sin_tienda_en_sesion_no_se_ve_ni_se_edita_nada()
    {
        await using var db = _bd.Contexto(null);
        var (tiendas, _) = Servicios(db, null);

        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => tiendas.ObtenerAsync(default));
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => tiendas.ActualizarAsync(Datos(), default));
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => tiendas.CambiarDireccionAsync(new("otra"), default));
    }

    [Fact]
    public async Task Actualizar_normaliza_y_guarda_solo_en_la_tienda_de_la_sesion()
    {
        await using (var db = _bd.Contexto(_bd.TiendaA))
        {
            var (tiendas, _) = Servicios(db, _bd.TiendaA);
            var dto = await tiendas.ActualizarAsync(
                Datos("  Luna Accesorios  ", "   ", "  (591) 712-34567 ", "#b5532f"), default);

            Assert.Equal("Luna Accesorios", dto.Nombre);
            Assert.Null(dto.Descripcion); // vacío = sin descripción
            Assert.Equal("+59171234567", dto.Whatsapp);
            Assert.Equal("#B5532F", dto.Color);
        }

        // La otra tienda quedó intacta.
        await using var lectura = _bd.Contexto(null);
        var b = await lectura.Tiendas.SingleAsync(t => t.Id == _bd.TiendaB);
        Assert.Equal("Tienda B", b.Nombre);
        Assert.Null(b.Whatsapp);
        Assert.Equal(Tienda.ColorPorDefecto, b.Color);
        // Y lo de A quedó guardado de verdad.
        Assert.Equal("+59171234567", (await lectura.Tiendas.SingleAsync(t => t.Id == _bd.TiendaA)).Whatsapp);
    }

    [Theory]
    [InlineData("71234567", "+59171234567")]
    [InlineData("+591 71234567", "+59171234567")]
    [InlineData("591-6123-4567", "+59161234567")]
    [InlineData("(+591) 612 34567", "+59161234567")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public async Task El_whatsapp_se_guarda_siempre_con_el_mismo_formato(string entrada, string? esperado)
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var (tiendas, _) = Servicios(db, _bd.TiendaA);

        var dto = await tiendas.ActualizarAsync(Datos(whatsapp: entrada), default);

        Assert.Equal(esperado, dto.Whatsapp);
    }

    [Theory]
    [InlineData("12345")]            // muy corto
    [InlineData("51234567")]         // no empieza con 6 ni 7
    [InlineData("712345678")]        // 9 dígitos
    [InlineData("+54 9 11 1234 5678")] // de otro país
    [InlineData("71234567a")]        // letras
    [InlineData("llamame al 71234567")]
    public async Task Un_whatsapp_que_no_es_un_celular_de_bolivia_se_rechaza(string entrada)
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var (tiendas, _) = Servicios(db, _bd.TiendaA);

        await Assert.ThrowsAsync<ValidacionException>(() => tiendas.ActualizarAsync(Datos(whatsapp: entrada), default));
    }

    [Theory]
    [InlineData("rojo")]
    [InlineData("#12")]
    [InlineData("#GGGGGG")]
    [InlineData("F2A33A")]
    [InlineData("#F2A33A; background:url(x)")] // el color llega a un estilo CSS: nada de inyección
    [InlineData("")]
    public async Task Un_color_que_no_es_hexadecimal_se_rechaza(string color)
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var (tiendas, _) = Servicios(db, _bd.TiendaA);

        await Assert.ThrowsAsync<ValidacionException>(() => tiendas.ActualizarAsync(Datos(color: color), default));
    }

    [Fact]
    public async Task Nombre_vacio_y_textos_largos_se_rechazan_sin_guardar_nada()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var (tiendas, _) = Servicios(db, _bd.TiendaA);

        await Assert.ThrowsAsync<ValidacionException>(() => tiendas.ActualizarAsync(Datos(nombre: "   "), default));
        await Assert.ThrowsAsync<ValidacionException>(() => tiendas.ActualizarAsync(Datos(nombre: new string('a', 121)), default));
        await Assert.ThrowsAsync<ValidacionException>(() => tiendas.ActualizarAsync(Datos(descripcion: new string('a', 501)), default));

        await using var lectura = _bd.Contexto(null);
        Assert.Equal("Tienda A", (await lectura.Tiendas.SingleAsync(t => t.Id == _bd.TiendaA)).Nombre);
    }

    // ───────────── Logo ─────────────

    [Fact]
    public async Task Se_puede_poner_un_logo_propio_y_aparece_con_sus_tres_urls()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var (tiendas, imagenes) = Servicios(db, _bd.TiendaA);
        var logo = await imagenes.SubirAsync(Foto(), default);

        var dto = await tiendas.ActualizarAsync(Datos(logo: logo.Id), default);

        Assert.NotNull(dto.Logo);
        Assert.Equal(logo.Id, dto.Logo.Id);
        Assert.Equal($"/archivos/{_bd.TiendaA}/imagenes/{logo.Id}/catalogo.webp", dto.Logo.Catalogo);
        Assert.Equal(logo.Id, (await tiendas.ObtenerAsync(default)).Logo!.Id);
    }

    [Fact]
    public async Task No_se_puede_usar_como_logo_la_foto_de_otra_tienda()
    {
        Guid fotoDeB;
        await using (var dbB = _bd.Contexto(_bd.TiendaB))
            fotoDeB = (await Servicios(dbB, _bd.TiendaB).Imagenes.SubirAsync(Foto(), default)).Id;

        await using var db = _bd.Contexto(_bd.TiendaA);
        var (tiendas, _) = Servicios(db, _bd.TiendaA);
        await Assert.ThrowsAsync<ImagenNoEncontradaException>(() => tiendas.ActualizarAsync(Datos(logo: fotoDeB), default));
        await Assert.ThrowsAsync<ImagenNoEncontradaException>(() => tiendas.ActualizarAsync(Datos(logo: Guid.NewGuid()), default));
        Assert.Null((await tiendas.ObtenerAsync(default)).Logo);
    }

    [Fact]
    public async Task Cambiar_el_logo_borra_el_anterior_y_sus_archivos()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var (tiendas, imagenes) = Servicios(db, _bd.TiendaA);
        var primero = await imagenes.SubirAsync(Foto(), default);
        await tiendas.ActualizarAsync(Datos(logo: primero.Id), default);
        var segundo = await imagenes.SubirAsync(Foto(), default);

        await tiendas.ActualizarAsync(Datos(logo: segundo.Id), default);

        Assert.Equal(segundo.Id, (await tiendas.ObtenerAsync(default)).Logo!.Id);
        await using var lectura = _bd.Contexto(_bd.TiendaA);
        Assert.Equal([segundo.Id], await lectura.Imagenes.Select(i => i.Id).ToListAsync());
        Assert.Equal(3, await lectura.Archivos.CountAsync()); // solo los del logo nuevo
    }

    [Fact]
    public async Task Sacar_el_logo_lo_borra_y_guardar_otros_datos_no_lo_toca_si_se_reenvia()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var (tiendas, imagenes) = Servicios(db, _bd.TiendaA);
        var logo = await imagenes.SubirAsync(Foto(), default);
        await tiendas.ActualizarAsync(Datos(logo: logo.Id), default);

        // Reenviar el mismo logo (el front lo hace al editar solo el nombre) no lo borra.
        await tiendas.ActualizarAsync(Datos(nombre: "Otro nombre", logo: logo.Id), default);
        Assert.NotNull((await tiendas.ObtenerAsync(default)).Logo);
        Assert.Equal(3, await db.Archivos.CountAsync());

        // null = sin logo: se borra.
        var sin = await tiendas.ActualizarAsync(Datos(nombre: "Otro nombre", logo: null), default);
        Assert.Null(sin.Logo);
        await using var lectura = _bd.Contexto(_bd.TiendaA);
        Assert.Empty(await lectura.Imagenes.ToListAsync());
        Assert.Equal(0, await lectura.Archivos.CountAsync());
    }

    // ───────────── Dirección ─────────────

    [Fact]
    public async Task Cambiar_la_direccion_la_normaliza_y_la_guarda()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var (tiendas, _) = Servicios(db, _bd.TiendaA);

        var dto = await tiendas.CambiarDireccionAsync(new("  Luna-Accesorios "), default);

        Assert.Equal("luna-accesorios", dto.Slug);
        await using var lectura = _bd.Contexto(null);
        Assert.Equal("luna-accesorios", (await lectura.Tiendas.SingleAsync(t => t.Id == _bd.TiendaA)).Slug);
        Assert.Equal("tienda-b", (await lectura.Tiendas.SingleAsync(t => t.Id == _bd.TiendaB)).Slug);
    }

    [Fact]
    public async Task Poner_la_misma_direccion_no_es_error_ni_cambia_nada()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var (tiendas, _) = Servicios(db, _bd.TiendaA);

        var dto = await tiendas.CambiarDireccionAsync(new("tienda-a"), default);

        Assert.Equal("tienda-a", dto.Slug);
    }

    [Fact]
    public async Task Una_direccion_ocupada_reservada_o_con_formato_malo_se_rechaza()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var (tiendas, _) = Servicios(db, _bd.TiendaA);

        await Assert.ThrowsAsync<SlugNoDisponibleException>(() => tiendas.CambiarDireccionAsync(new("tienda-b"), default));
        await Assert.ThrowsAsync<ValidacionException>(() => tiendas.CambiarDireccionAsync(new("admin"), default));
        await Assert.ThrowsAsync<ValidacionException>(() => tiendas.CambiarDireccionAsync(new("con espacios"), default));
        await Assert.ThrowsAsync<ValidacionException>(() => tiendas.CambiarDireccionAsync(new("-empieza-con-guion"), default));
        await Assert.ThrowsAsync<ValidacionException>(() => tiendas.CambiarDireccionAsync(new("a"), default));
        await Assert.ThrowsAsync<ValidacionException>(() => tiendas.CambiarDireccionAsync(new(new string('a', 41)), default));

        Assert.Equal("tienda-a", (await tiendas.ObtenerAsync(default)).Slug);
    }
}
