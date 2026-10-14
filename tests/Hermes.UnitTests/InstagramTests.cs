using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Instagram;
using Hermes.Domain.Entities;
using Hermes.Infrastructure.Imagenes;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Services;
using Hermes.Infrastructure.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hermes.UnitTests;

public class InstagramTests : IDisposable
{
    private readonly BaseDePrueba _bd = new();
    private readonly FakeTimeProvider _reloj = new(new DateTimeOffset(2026, 10, 14, 15, 0, 0, TimeSpan.Zero));

    public void Dispose() => _bd.Dispose();

    private record Mundo(HermesDbContext Db, ProductoService Productos, StockService Stock, InstagramService Instagram);

    private Mundo Para(HermesDbContext db, Guid? tienda)
    {
        var almacen = new AlmacenEnBaseDeDatos(db, _reloj);
        var sesion = new BaseDePrueba.TiendaFija(tienda);
        var imagenes = new ImagenService(db, almacen, sesion, _reloj);
        var productos = new ProductoService(db, imagenes, almacen, sesion, _reloj);
        var stock = new StockService(db, new OperacionesStock(db, _reloj), almacen, sesion);
        return new Mundo(db, productos, stock, new InstagramService(db, productos, stock, sesion, _reloj));
    }

    private const string Publicacion = "Cartera de cuero Mía\nBs 180\nQuedan 5\n#carteras";

    // Lo que la dueña deja al revisar: el producto tal como lo quiere en el catálogo.
    private static GuardarProductoDto Revisado(BorradorDto b, decimal? precio = null, string[]? colores = null)
        => new(b.Nombre, b.Descripcion, precio ?? b.Precio ?? 1, 0, null, true, colores ?? b.Colores.ToArray(), b.Tallas.ToArray(), null);

    // ───────────── Leer ─────────────

    [Fact]
    public async Task Leer_dice_lo_que_entiende_y_lo_que_falta_sin_guardar_nada()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);

        var r = m.Instagram.Leer(new("Bolso de lona\n#bolsos"));

        var p = Assert.Single(r.Productos);
        Assert.Equal(("Bolso de lona", "Bolsos"), (p.Nombre, p.Categoria));
        Assert.Equal(["precio"], p.Faltan);
        Assert.Equal(["bolsos"], r.Hashtags);
        Assert.Equal(0, await db.Borradores.CountAsync());
    }

    [Fact]
    public async Task Un_texto_vacio_o_demasiado_largo_se_rechaza()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);

        await Assert.ThrowsAsync<ValidacionException>(async () => m.Instagram.Leer(new("   ")));
        await Assert.ThrowsAsync<ValidacionException>(async () => m.Instagram.Leer(new(new string('x', ParserPublicacion.MaximoTexto + 1))));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Instagram.ImportarAsync(new(""), default));
    }

    // ───────────── Importar ─────────────

    [Fact]
    public async Task Importar_deja_lo_entendido_pendiente_en_la_bandeja_y_no_toca_el_catalogo()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);

        var creados = await m.Instagram.ImportarAsync(new(Publicacion), default);

        var b = Assert.Single(creados);
        Assert.Equal((EstadoBorrador.Pendiente, "Manual", "Cartera de cuero Mía", 180m, 5, "Carteras"), (b.Estado, b.Origen, b.Nombre, b.Precio, b.Stock, b.Categoria));
        Assert.Equal(Publicacion, b.Texto);
        Assert.Empty(b.Faltan);
        Assert.Equal(0, await db.Productos.CountAsync()); // nada llega al catálogo sin que la dueña lo revise
    }

    [Fact]
    public async Task Un_carrusel_deja_un_borrador_por_producto()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);

        var creados = await m.Instagram.ImportarAsync(new("Novedades\n1. Cartera Bs 120\n2. Aretes Bs 95\n3. Collar Bs 150\n#accesorios"), default);

        Assert.Equal(["Cartera", "Aretes", "Collar"], creados.Select(b => b.Nombre));
        var bandeja = await m.Instagram.ListarAsync(new(), default);
        Assert.Equal(3, bandeja.Pendientes);
        Assert.Equal(["Cartera", "Aretes", "Collar"], bandeja.Items.Select(b => b.Nombre)); // en el orden de la publicación
    }

    [Fact]
    public async Task Un_producto_sin_precio_entra_a_la_bandeja_marcado_como_incompleto()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);

        var b = Assert.Single(await m.Instagram.ImportarAsync(new("Mochila escolar\nHecha a mano"), default));

        Assert.Null(b.Precio);
        Assert.Equal(["precio"], b.Faltan);
        Assert.Contains(b.Avisos, a => a.Contains("precio"));
    }

    [Fact]
    public async Task Si_no_se_entiende_ningun_producto_no_se_guarda_nada_y_se_explica()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);

        var ex = await Assert.ThrowsAsync<ValidacionException>(() => m.Instagram.ImportarAsync(new("#hechoamano #bolivia\n✨✨"), default));

        Assert.Contains("No pudimos leer", ex.Message);
        Assert.Equal(0, await db.Borradores.CountAsync());
    }

    [Fact]
    public async Task La_bandeja_tiene_un_tope_de_pendientes()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var texto = string.Join("\n", Enumerable.Range(1, 20).Select(i => $"{i}. Producto {i} Bs {10 + i}"));
        for (var i = 0; i < 10; i++) await m.Instagram.ImportarAsync(new(texto), default); // 200 pendientes

        var ex = await Assert.ThrowsAsync<ValidacionException>(() => m.Instagram.ImportarAsync(new("Uno más\nBs 10"), default));

        Assert.Contains("200", ex.Message);
        Assert.Equal(200, await db.Borradores.CountAsync());
    }

    // ───────────── La bandeja ─────────────

    [Fact]
    public async Task La_bandeja_lista_lo_pendiente_mas_nuevo_primero_con_cursor_y_filtro_por_estado()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var ids = new List<Guid>();
        for (var i = 1; i <= 5; i++)
        {
            ids.Add((await m.Instagram.ImportarAsync(new($"Producto {i}\nBs {10 * i}"), default))[0].Id);
            _reloj.Advance(TimeSpan.FromSeconds(1)); // el Id sale del reloj: en la vida real cada importación es otro instante
        }
        await m.Instagram.DescartarAsync(ids[0], default);

        var vistos = new List<Guid>();
        Guid? cursor = null;
        PaginaDeBorradoresDto pagina;
        do
        {
            pagina = await m.Instagram.ListarAsync(new(Cursor: cursor, Limite: 2), default);
            vistos.AddRange(pagina.Items.Select(b => b.Id));
            cursor = pagina.Siguiente;
        } while (cursor is not null);

        Assert.Equal(ids.Skip(1).Reverse(), vistos);
        Assert.Equal(4, pagina.Pendientes);
        Assert.Equal([ids[0]], (await m.Instagram.ListarAsync(new(EstadoBorrador.Descartado), default)).Items.Select(b => b.Id));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Instagram.ListarAsync(new((EstadoBorrador)9), default));
    }

    // ───────────── Publicar ─────────────

    [Fact]
    public async Task Publicar_crea_el_producto_con_lo_que_la_duena_reviso_y_carga_el_stock()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var b = (await m.Instagram.ImportarAsync(new(Publicacion), default))[0];

        // Revisó y le cambió el precio: el catálogo queda con lo que ella dejó, no con lo que se leyó.
        var r = await m.Instagram.PublicarAsync(b.Id, new(Revisado(b, precio: 200), b.Stock), default);

        Assert.Equal(("Cartera de cuero Mía", 200m), (r.Producto.Nombre, r.Producto.Precio));
        Assert.True(r.StockCargado);
        Assert.Null(r.AvisoStock);
        Assert.Equal(5, (await m.Stock.ObtenerAsync(r.Producto.Id, default)).Stock);
        var despues = await m.Instagram.ObtenerAsync(b.Id, default);
        Assert.Equal((EstadoBorrador.Publicado, r.Producto.Id), (despues.Estado, despues.ProductoId));
        Assert.Equal(0, (await m.Instagram.ListarAsync(new(), default)).Pendientes);
    }

    [Fact]
    public async Task Con_varias_opciones_no_se_reparte_el_stock_y_se_le_dice_donde_cargarlo()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var b = (await m.Instagram.ImportarAsync(new("Cartera\nBs 180\nColores: café, negro\nQuedan 5"), default))[0];

        var r = await m.Instagram.PublicarAsync(b.Id, new(Revisado(b), b.Stock), default);

        Assert.False(r.StockCargado);
        Assert.Contains("varias opciones", r.AvisoStock);
        Assert.Equal(0, (await m.Stock.ObtenerAsync(r.Producto.Id, default)).Stock);
        Assert.Equal(2, r.Producto.Variantes.Count); // el producto sí se publicó, con sus dos colores
    }

    [Fact]
    public async Task Un_producto_sin_control_de_stock_no_recibe_unidades()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var b = (await m.Instagram.ImportarAsync(new(Publicacion), default))[0];

        var r = await m.Instagram.PublicarAsync(b.Id, new(Revisado(b) with { ControlaStock = false }, 5), default);

        Assert.False(r.StockCargado);
        Assert.Contains("no lleva control", r.AvisoStock);
    }

    [Fact]
    public async Task Publicar_dos_veces_o_un_descartado_da_409_y_no_crea_otro_producto()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var b = (await m.Instagram.ImportarAsync(new(Publicacion), default))[0];
        var descartado = (await m.Instagram.ImportarAsync(new("Otro\nBs 10"), default))[0];
        await m.Instagram.PublicarAsync(b.Id, new(Revisado(b)), default);
        await m.Instagram.DescartarAsync(descartado.Id, default);

        await Assert.ThrowsAsync<BorradorYaResueltoException>(() => m.Instagram.PublicarAsync(b.Id, new(Revisado(b)), default));
        await Assert.ThrowsAsync<BorradorYaResueltoException>(() => m.Instagram.PublicarAsync(descartado.Id, new(Revisado(descartado)), default));
        Assert.Equal(1, await db.Productos.CountAsync());
        await Assert.ThrowsAsync<BorradorNoEncontradoException>(() => m.Instagram.PublicarAsync(Guid.NewGuid(), new(Revisado(b)), default));
    }

    [Fact]
    public async Task Si_el_producto_no_se_puede_crear_el_borrador_vuelve_a_la_bandeja_y_se_puede_corregir()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var b = (await m.Instagram.ImportarAsync(new("Mochila escolar\nHecha a mano"), default))[0]; // sin precio

        await Assert.ThrowsAsync<ValidacionException>(() => m.Instagram.PublicarAsync(b.Id, new(Revisado(b) with { Precio = 0 }), default));

        Assert.Equal(EstadoBorrador.Pendiente, (await m.Instagram.ObtenerAsync(b.Id, default)).Estado);
        Assert.Equal(0, await db.Productos.CountAsync());
        var r = await m.Instagram.PublicarAsync(b.Id, new(Revisado(b, precio: 150)), default); // lo corrige y sale
        Assert.Equal(150m, r.Producto.Precio);
    }

    [Fact]
    public async Task Publicar_sin_producto_en_el_cuerpo_se_rechaza()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var b = (await m.Instagram.ImportarAsync(new(Publicacion), default))[0];

        await Assert.ThrowsAsync<ValidacionException>(() => m.Instagram.PublicarAsync(b.Id, new(null!), default));
    }

    // ───────────── Descartar ─────────────

    [Fact]
    public async Task Descartar_lo_saca_de_la_bandeja_pero_no_lo_borra()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var b = (await m.Instagram.ImportarAsync(new(Publicacion), default))[0];

        var d = await m.Instagram.DescartarAsync(b.Id, default);

        Assert.Equal(EstadoBorrador.Descartado, d.Estado);
        Assert.Equal(0, (await m.Instagram.ListarAsync(new(), default)).Items.Count);
        Assert.Equal(1, await db.Borradores.CountAsync()); // queda registrado
        await Assert.ThrowsAsync<BorradorYaResueltoException>(() => m.Instagram.DescartarAsync(b.Id, default));
        await Assert.ThrowsAsync<BorradorNoEncontradoException>(() => m.Instagram.DescartarAsync(Guid.NewGuid(), default));
    }

    // ───────────── Aislamiento ─────────────

    [Fact]
    public async Task Una_tienda_no_ve_publica_ni_descarta_la_bandeja_de_otra()
    {
        Guid deA;
        await using (var dbA = _bd.Contexto(_bd.TiendaA))
            deA = (await Para(dbA, _bd.TiendaA).Instagram.ImportarAsync(new(Publicacion), default))[0].Id;

        await using var dbB = _bd.Contexto(_bd.TiendaB);
        var b = Para(dbB, _bd.TiendaB);
        var lista = await b.Instagram.ListarAsync(new(), default);
        Assert.Empty(lista.Items);
        Assert.Equal(0, lista.Pendientes);
        await Assert.ThrowsAsync<BorradorNoEncontradoException>(() => b.Instagram.ObtenerAsync(deA, default));
        await Assert.ThrowsAsync<BorradorNoEncontradoException>(() => b.Instagram.DescartarAsync(deA, default));
        await Assert.ThrowsAsync<BorradorNoEncontradoException>(() => b.Instagram.PublicarAsync(deA, new(new("X", null, 10, 0, null, true, null, null, null)), default));

        await using var verificar = _bd.Contexto(_bd.TiendaA);
        Assert.Equal(EstadoBorrador.Pendiente, (await Para(verificar, _bd.TiendaA).Instagram.ObtenerAsync(deA, default)).Estado);
        Assert.Equal(0, await verificar.Productos.CountAsync());
    }

    [Fact]
    public async Task Sin_tienda_en_sesion_no_se_lee_ni_se_importa_nada()
    {
        await using var db = _bd.Contexto(null);
        var m = Para(db, null);

        await Assert.ThrowsAsync<SinTiendaEnSesionException>(async () => m.Instagram.Leer(new("Algo\nBs 10")));
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => m.Instagram.ImportarAsync(new("Algo\nBs 10"), default));
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => m.Instagram.ListarAsync(new(), default));
    }

    // ───────────── Concurrencia (solo con Postgres real) ─────────────

    [Fact]
    public async Task Seis_publicaciones_simultaneas_del_mismo_borrador_crean_un_solo_producto_con_Postgres_real()
    {
        var conexion = Environment.GetEnvironmentVariable("HERMES_TEST_PG");
        if (string.IsNullOrWhiteSpace(conexion)) return;

        var baseDeDatos = "hermes_test_" + Guid.NewGuid().ToString("N")[..10];
        await using (var admin = new Npgsql.NpgsqlConnection(conexion + ";Database=postgres"))
        {
            await admin.OpenAsync();
            await new Npgsql.NpgsqlCommand($"create database {baseDeDatos}", admin).ExecuteNonQueryAsync();
        }
        HermesDbContext Nuevo(Guid? tienda) => new(
            new DbContextOptionsBuilder<HermesDbContext>().UseNpgsql($"{conexion};Database={baseDeDatos}").UseSnakeCaseNamingConvention().Options,
            new BaseDePrueba.TiendaFija(tienda));

        try
        {
            Guid tienda, borrador;
            await using (var db = Nuevo(null))
            {
                await db.Database.EnsureCreatedAsync();
                var dueno = new Usuario { Nombre = "D", Email = "d@x.test", PasswordHash = "x" };
                var t = new Tienda { DuenoId = dueno.Id, Slug = "tienda-x", Nombre = "X" };
                db.AddRange(dueno, t);
                await db.SaveChangesAsync();
                tienda = t.Id;
            }
            await using (var db = Nuevo(tienda))
                borrador = (await Para(db, tienda).Instagram.ImportarAsync(new(Publicacion), default))[0].Id;

            var resultados = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
            {
                await using var db = Nuevo(tienda);
                try { await Para(db, tienda).Instagram.PublicarAsync(borrador, new(new("Cartera de cuero Mía", null, 180, 0, null, true, null, null, null), 5), default); return true; }
                catch (BorradorYaResueltoException) { return false; }
            }));

            Assert.Equal(1, resultados.Count(r => r));
            await using var lectura = Nuevo(tienda);
            Assert.Equal(1, await lectura.Productos.CountAsync());
            Assert.Equal(5, (await Para(lectura, tienda).Stock.ObtenerAsync((await lectura.Productos.SingleAsync()).Id, default)).Stock); // y el stock se cargó una vez
        }
        finally
        {
            Npgsql.NpgsqlConnection.ClearAllPools();
            await using var admin = new Npgsql.NpgsqlConnection(conexion + ";Database=postgres");
            await admin.OpenAsync();
            await new Npgsql.NpgsqlCommand($"drop database if exists {baseDeDatos} with (force)", admin).ExecuteNonQueryAsync();
        }
    }
}
