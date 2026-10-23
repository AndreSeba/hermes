using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Domain;
using Hermes.Infrastructure.Imagenes;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Hermes.UnitTests;

public class ProductoTests : IDisposable
{
    private readonly BaseDePrueba _bd = new();

    public void Dispose() => _bd.Dispose();

    private record Servicios(ProductoService Productos, ImagenService Imagenes, CategoriaService Categorias, TiendaService Tiendas);

    private Servicios Para(HermesDbContext db, Guid? tienda)
    {
        var almacen = new AlmacenEnBaseDeDatos(db, TimeProvider.System);
        var sesion = new BaseDePrueba.TiendaFija(tienda);
        var imagenes = new ImagenService(db, almacen, sesion, TimeProvider.System);
        return new Servicios(
            new ProductoService(db, imagenes, almacen, sesion, TimeProvider.System),
            imagenes,
            new CategoriaService(db),
            new TiendaService(db, imagenes, almacen, sesion));
    }

    private static GuardarProductoDto Dto(string nombre = "Cartera de cuero Mía", decimal precio = 180, decimal costo = 0,
        Guid? categoria = null, bool visible = true, string[]? colores = null, string[]? tallas = null, Guid[]? fotos = null)
        => new(nombre, null, precio, costo, categoria, visible, colores, tallas, fotos);

    private static Task<Guid> SubirFoto(Servicios s) => s.Imagenes.SubirAsync(FotoDePrueba.Jpeg(), default).ContinueWith(t => t.Result.Id);

    // ───────────── Crear ─────────────

    [Fact]
    public async Task Un_producto_sin_colores_ni_tallas_tiene_la_variante_unica()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var s = Para(db, _bd.TiendaA);

        var p = await s.Productos.CrearAsync(Dto(costo: 110), default);

        Assert.Equal("Cartera de cuero Mía", p.Nombre);
        var unica = Assert.Single(p.Variantes);
        Assert.Null(unica.Color);
        Assert.Null(unica.Talla);
        Assert.Equal(70, p.Ganancia);
        Assert.Equal(EstadoPrecio.Ok, p.EstadoPrecio);
        Assert.False(p.Archivado);
        Assert.True(p.VisibleEnTienda);
    }

    [Fact]
    public async Task Las_variantes_son_colores_por_tallas_y_no_distinguen_mayusculas()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var s = Para(db, _bd.TiendaA);

        var seis = await s.Productos.CrearAsync(Dto(colores: ["café", "Negro"], tallas: ["S", "M", "L"]), default);
        Assert.Equal(6, seis.Variantes.Count);
        Assert.Contains(seis.Variantes, v => v is { Color: "café", Talla: "M" });

        var repetidos = await s.Productos.CrearAsync(Dto(colores: ["Rojo", " rojo ", "ROJO", "", "  "]), default);
        var rojo = Assert.Single(repetidos.Variantes);
        Assert.Equal("Rojo", rojo.Color); // queda la primera forma escrita
        Assert.Null(rojo.Talla);

        var soloTallas = await s.Productos.CrearAsync(Dto(tallas: ["38", "40"]), default);
        Assert.Equal(["38", "40"], soloTallas.Variantes.Select(v => v.Talla));
        Assert.All(soloTallas.Variantes, v => Assert.Null(v.Color));
    }

    [Fact]
    public async Task Demasiadas_combinaciones_se_rechazan_con_un_mensaje_claro()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var s = Para(db, _bd.TiendaA);
        var ocho = Enumerable.Range(1, 8).Select(i => $"x{i}").ToArray();

        var ex = await Assert.ThrowsAsync<ValidacionException>(() => s.Productos.CrearAsync(Dto(colores: ocho, tallas: ocho), default));
        Assert.Contains("64", ex.Message);
    }

    [Theory]
    [InlineData("   ", 100, 0)]        // sin nombre
    [InlineData("X", 0, 0)]            // precio 0
    [InlineData("X", -5, 0)]           // precio negativo
    [InlineData("X", 10.555, 0)]       // más de 2 decimales
    [InlineData("X", 1_000_001, 0)]    // un cero de más
    [InlineData("X", 100, -1)]         // costo negativo
    [InlineData("X", 100, 1.005)]      // costo con 3 decimales
    public async Task Nombre_precio_y_costo_invalidos_se_rechazan_sin_guardar_nada(string nombre, double precio, double costo)
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var s = Para(db, _bd.TiendaA);

        await Assert.ThrowsAsync<ValidacionException>(() => s.Productos.CrearAsync(Dto(nombre, (decimal)precio, (decimal)costo), default));
        Assert.Empty((await s.Productos.ListarAsync(new(), default)).Items);
    }

    [Fact]
    public async Task Textos_demasiado_largos_se_rechazan()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var s = Para(db, _bd.TiendaA);

        await Assert.ThrowsAsync<ValidacionException>(() => s.Productos.CrearAsync(Dto(nombre: new string('a', 121)), default));
        await Assert.ThrowsAsync<ValidacionException>(() => s.Productos.CrearAsync(Dto() with { Descripcion = new string('a', 1001) }, default));
        await Assert.ThrowsAsync<ValidacionException>(() => s.Productos.CrearAsync(Dto(colores: [new string('a', 31)]), default));
    }

    // ───────────── Categoría ─────────────

    [Fact]
    public async Task La_categoria_tiene_que_ser_una_activa_de_la_misma_tienda()
    {
        await using var dbB = _bd.Contexto(_bd.TiendaB);
        var deB = await Para(dbB, _bd.TiendaB).Categorias.CrearAsync(new("Joyería"), default);

        await using var db = _bd.Contexto(_bd.TiendaA);
        var s = Para(db, _bd.TiendaA);
        var propia = await s.Categorias.CrearAsync(new("Carteras"), default);

        Assert.Equal(propia.Id, (await s.Productos.CrearAsync(Dto(categoria: propia.Id), default)).CategoriaId);
        await Assert.ThrowsAsync<CategoriaNoEncontradaException>(() => s.Productos.CrearAsync(Dto(categoria: deB.Id), default));
        await Assert.ThrowsAsync<CategoriaNoEncontradaException>(() => s.Productos.CrearAsync(Dto(categoria: Guid.NewGuid()), default));

        await s.Categorias.ActualizarAsync(propia.Id, new("Carteras", false), default);
        await Assert.ThrowsAsync<CategoriaNoEncontradaException>(() => s.Productos.CrearAsync(Dto(categoria: propia.Id), default));
    }

    // ───────────── Fotos ─────────────

    [Fact]
    public async Task Las_fotos_se_guardan_en_el_orden_pedido_y_la_primera_es_la_portada()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var s = Para(db, _bd.TiendaA);
        Guid[] ids = [await SubirFoto(s), await SubirFoto(s), await SubirFoto(s)];

        var p = await s.Productos.CrearAsync(Dto(fotos: [ids[2], ids[0], ids[1]]), default);
        Assert.Equal([ids[2], ids[0], ids[1]], p.Fotos.Select(f => f.Id));

        var lista = await s.Productos.ListarAsync(new(), default);
        Assert.Equal(ids[2], lista.Items.Single().Portada!.Id);
    }

    [Fact]
    public async Task No_se_puede_usar_la_foto_de_otra_tienda_ni_una_que_ya_es_de_otro_producto_ni_el_logo()
    {
        Guid deB;
        await using (var dbB = _bd.Contexto(_bd.TiendaB))
            deB = await SubirFoto(Para(dbB, _bd.TiendaB));

        await using var db = _bd.Contexto(_bd.TiendaA);
        var s = Para(db, _bd.TiendaA);
        await Assert.ThrowsAsync<ImagenNoEncontradaException>(() => s.Productos.CrearAsync(Dto(fotos: [deB]), default));
        await Assert.ThrowsAsync<ImagenNoEncontradaException>(() => s.Productos.CrearAsync(Dto(fotos: [Guid.NewGuid()]), default));

        var usada = await SubirFoto(s);
        await s.Productos.CrearAsync(Dto(fotos: [usada]), default);
        await Assert.ThrowsAsync<ImagenEnUsoException>(() => s.Productos.CrearAsync(Dto("Otro", fotos: [usada]), default));

        var logo = await SubirFoto(s);
        await s.Tiendas.ActualizarAsync(new("Tienda A", null, null, "#F2A33A", logo), default);
        await Assert.ThrowsAsync<ImagenEnUsoException>(() => s.Productos.CrearAsync(Dto("Con logo", fotos: [logo]), default));
    }

    [Fact]
    public async Task Hay_un_maximo_de_fotos_y_no_pueden_repetirse()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var s = Para(db, _bd.TiendaA);
        var foto = await SubirFoto(s);

        await Assert.ThrowsAsync<ValidacionException>(() => s.Productos.CrearAsync(Dto(fotos: [foto, foto]), default));
        var nueve = Enumerable.Range(0, 9).Select(_ => Guid.NewGuid()).ToArray();
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => s.Productos.CrearAsync(Dto(fotos: nueve), default));
        Assert.Contains("8 fotos", ex.Message);
    }

    // ───────────── Editar ─────────────

    [Fact]
    public async Task Reordenar_cambia_la_portada_y_sacar_una_foto_la_borra_de_verdad()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var s = Para(db, _bd.TiendaA);
        Guid[] ids = [await SubirFoto(s), await SubirFoto(s), await SubirFoto(s)];
        var p = await s.Productos.CrearAsync(Dto(fotos: ids), default);

        var reordenado = await s.Productos.ActualizarAsync(p.Id, Dto(fotos: [ids[1], ids[0], ids[2]]), default);
        Assert.Equal([ids[1], ids[0], ids[2]], reordenado.Fotos.Select(f => f.Id));
        Assert.Equal(9, await db.Archivos.CountAsync());

        var sinUna = await s.Productos.ActualizarAsync(p.Id, Dto(fotos: [ids[1], ids[2]]), default);
        Assert.Equal([ids[1], ids[2]], sinUna.Fotos.Select(f => f.Id));
        Assert.DoesNotContain(ids[0], await db.Imagenes.Select(i => i.Id).ToListAsync());
        Assert.Equal(6, await db.Archivos.CountAsync()); // los 3 archivos de la foto quitada ya no están
    }

    [Fact]
    public async Task Sacar_un_color_lo_desactiva_y_ponerlo_de_nuevo_reactiva_la_misma_variante()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var s = Para(db, _bd.TiendaA);
        var p = await s.Productos.CrearAsync(Dto(colores: ["rojo", "azul"]), default);
        var idAzul = p.Variantes.Single(v => v.Color == "azul").Id;

        var sinAzul = await s.Productos.ActualizarAsync(p.Id, Dto(colores: ["rojo"]), default);
        Assert.Equal(["rojo"], sinAzul.Variantes.Select(v => v.Color));
        // No se borró: sigue en la base, desactivada (en M5 tendrá su historial de stock).
        Assert.False((await db.Variantes.IgnoreQueryFilters().SingleAsync(v => v.Id == idAzul)).Activa);

        var conAzulOtraVez = await s.Productos.ActualizarAsync(p.Id, Dto(colores: ["rojo", "AZUL"]), default);
        Assert.Contains(conAzulOtraVez.Variantes, v => v.Id == idAzul && v.Color == "AZUL");
        Assert.Equal(2, await db.Variantes.CountAsync()); // ninguna fila nueva
    }

    [Fact]
    public async Task Pasar_de_unico_a_colores_desactiva_unico_y_crea_las_nuevas()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var s = Para(db, _bd.TiendaA);
        var p = await s.Productos.CrearAsync(Dto(), default);

        var conColores = await s.Productos.ActualizarAsync(p.Id, Dto(colores: ["café", "negro"]), default);

        Assert.Equal(["café", "negro"], conColores.Variantes.Select(v => v.Color));
        Assert.Equal(3, await db.Variantes.CountAsync()); // Único (inactiva) + 2
    }

    [Fact]
    public async Task Editar_actualiza_los_datos_y_se_ve_en_el_listado()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var s = Para(db, _bd.TiendaA);
        var p = await s.Productos.CrearAsync(Dto(precio: 100, costo: 0), default);

        var editado = await s.Productos.ActualizarAsync(p.Id,
            Dto("Cartera Mía v2", 150.50m, 90, visible: false) with { Descripcion = "  Hecha a mano  " }, default);

        Assert.Equal("Cartera Mía v2", editado.Nombre);
        Assert.Equal("Hecha a mano", editado.Descripcion);
        Assert.Equal(150.50m, editado.Precio);
        Assert.Equal(60.50m, editado.Ganancia);
        Assert.False(editado.VisibleEnTienda);
        Assert.Equal("Cartera Mía v2", (await s.Productos.ListarAsync(new(), default)).Items.Single().Nombre);
    }

    [Fact]
    public async Task La_pagina_numerada_trae_cada_tramo_sin_repetir_y_acota_una_pagina_fuera_de_rango()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var s = Para(db, _bd.TiendaA);
        for (var i = 1; i <= 5; i++) await s.Productos.CrearAsync(Dto($"Producto {i}"), default);

        var p1 = await s.Productos.ListarAsync(new(Limite: 2, Pagina: 1), default);
        var p2 = await s.Productos.ListarAsync(new(Limite: 2, Pagina: 2), default);
        var p3 = await s.Productos.ListarAsync(new(Limite: 2, Pagina: 3), default);
        var fuera = await s.Productos.ListarAsync(new(Limite: 2, Pagina: 99), default);

        Assert.Equal((5, 3), (p1.Total, p1.TotalPaginas));
        Assert.Equal([2, 2, 1], [p1.Items.Count, p2.Items.Count, p3.Items.Count]);
        Assert.Equal(5, p1.Items.Concat(p2.Items).Concat(p3.Items).Select(i => i.Id).Distinct().Count());
        Assert.Equal(3, fuera.Pagina); // se acota a la última
        Assert.Equal(p3.Items.Select(i => i.Id), fuera.Items.Select(i => i.Id));
    }

    // ───────────── Archivar ─────────────

    [Fact]
    public async Task Archivar_saca_el_producto_del_catalogo_sin_borrarlo_y_se_puede_desarchivar()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var s = Para(db, _bd.TiendaA);
        var p = await s.Productos.CrearAsync(Dto(), default);
        await s.Productos.CrearAsync(Dto("Otro"), default);

        Assert.True((await s.Productos.ArchivarAsync(p.Id, true, default)).Archivado);
        Assert.Equal(["Otro"], (await s.Productos.ListarAsync(new(), default)).Items.Select(i => i.Nombre));
        Assert.Equal([p.Nombre], (await s.Productos.ListarAsync(new(Archivados: true), default)).Items.Select(i => i.Nombre));
        Assert.Equal(p.Id, (await s.Productos.ObtenerAsync(p.Id, default)).Id); // sigue existiendo

        Assert.False((await s.Productos.ArchivarAsync(p.Id, false, default)).Archivado);
        Assert.Equal(2, (await s.Productos.ListarAsync(new(), default)).Items.Count);
        Assert.Empty((await s.Productos.ListarAsync(new(Archivados: true), default)).Items);
    }

    // ───────────── Listado ─────────────

    [Fact]
    public async Task Buscar_ignora_mayusculas_y_filtra_por_categoria()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var s = Para(db, _bd.TiendaA);
        var carteras = await s.Categorias.CrearAsync(new("Carteras"), default);
        await s.Productos.CrearAsync(Dto("Cartera de cuero Mía", categoria: carteras.Id), default);
        await s.Productos.CrearAsync(Dto("Aretes de plata Killa"), default);
        await s.Productos.CrearAsync(Dto("Bolso awayo Inti", categoria: carteras.Id), default);

        Assert.Equal(["Cartera de cuero Mía"], (await s.Productos.ListarAsync(new(Buscar: "  CARTERA "), default)).Items.Select(i => i.Nombre));
        Assert.Empty((await s.Productos.ListarAsync(new(Buscar: "no existe"), default)).Items);
        Assert.Equal(2, (await s.Productos.ListarAsync(new(CategoriaId: carteras.Id), default)).Items.Count);
        // Los comodines de LIKE no se interpretan como tales.
        Assert.Empty((await s.Productos.ListarAsync(new(Buscar: "%"), default)).Items);
        Assert.Equal("Carteras", (await s.Productos.ListarAsync(new(CategoriaId: carteras.Id), default)).Items[0].Categoria);
    }

    [Theory]
    [InlineData(100, 0, EstadoPrecio.SinCosto)]   // no se cargó el costo
    [InlineData(60, 62, EstadoPrecio.Pierde)]     // perdés Bs 2 (la billetera del prototipo)
    [InlineData(100, 100, EstadoPrecio.Pierde)]   // ganancia 0 también es perder
    [InlineData(35, 32, EstadoPrecio.Bajo)]       // pulsera: gana Bs 3 de 35
    [InlineData(100, 81, EstadoPrecio.Bajo)]      // 19 % < 20 %
    [InlineData(100, 80, EstadoPrecio.Ok)]        // justo 20 %: está bien
    [InlineData(180, 110, EstadoPrecio.Ok)]
    public void El_estado_del_precio_sigue_la_regla_del_prototipo(double precio, double costo, EstadoPrecio esperado)
        => Assert.Equal(esperado, MargenProducto.Estado((decimal)precio, (decimal)costo));

    [Fact]
    public async Task El_filtro_Revisar_precio_trae_exactamente_los_que_el_estado_marca_como_Pierde_o_Bajo()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var s = Para(db, _bd.TiendaA);
        (decimal precio, decimal costo)[] casos = [(100, 0), (60, 62), (100, 100), (35, 32), (100, 81), (100, 80), (180, 110), (95, 38), (10.50m, 10.40m)];
        foreach (var (precio, costo) in casos)
            await s.Productos.CrearAsync(Dto($"P {precio}/{costo}", precio, costo), default);

        var todos = (await s.Productos.ListarAsync(new(), default)).Items;
        var esperados = todos.Where(p => p.EstadoPrecio is EstadoPrecio.Pierde or EstadoPrecio.Bajo).Select(p => p.Id).Order().ToList();
        var filtrados = (await s.Productos.ListarAsync(new(RevisarPrecio: true), default)).Items.Select(p => p.Id).Order().ToList();

        Assert.NotEmpty(esperados);
        Assert.Equal(esperados, filtrados);
        Assert.DoesNotContain(todos.Where(p => p.EstadoPrecio == EstadoPrecio.SinCosto).Select(p => p.Id), filtrados.Contains);
    }

    [Fact]
    public async Task La_paginacion_por_cursor_recorre_todo_una_sola_vez_y_en_orden()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var s = Para(db, _bd.TiendaA);
        for (var i = 1; i <= 7; i++) await s.Productos.CrearAsync(Dto($"Producto {i}"), default);

        var vistos = new List<Guid>();
        var tamanos = new List<int>();
        Guid? cursor = null;
        do
        {
            var pagina = await s.Productos.ListarAsync(new(Limite: 3, Cursor: cursor), default);
            tamanos.Add(pagina.Items.Count);
            vistos.AddRange(pagina.Items.Select(p => p.Id));
            cursor = pagina.Siguiente;
        } while (cursor is not null);

        Assert.Equal([3, 3, 1], tamanos);
        Assert.Equal(7, vistos.Distinct().Count()); // ninguno repetido ni salteado
        // Más nuevos primero (Guid v7: el texto en minúsculas ordena por fecha de creación).
        Assert.Equal(vistos.Select(v => v.ToString()).OrderByDescending(x => x, StringComparer.Ordinal), vistos.Select(v => v.ToString()));
    }

    [Fact]
    public async Task La_pagina_exacta_no_inventa_una_pagina_siguiente_vacia()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var s = Para(db, _bd.TiendaA);
        for (var i = 1; i <= 4; i++) await s.Productos.CrearAsync(Dto($"Producto {i}"), default);

        var pagina = await s.Productos.ListarAsync(new(Limite: 4), default);

        Assert.Equal(4, pagina.Items.Count);
        Assert.Null(pagina.Siguiente);

        // Un límite absurdo se acota (1 a 60) en vez de fallar o devolver todo.
        var uno = await s.Productos.ListarAsync(new(Limite: 0), default);
        Assert.Single(uno.Items);
        Assert.NotNull(uno.Siguiente);
        Assert.Equal(4, (await s.Productos.ListarAsync(new(Limite: 100_000), default)).Items.Count);
    }

    [Fact]
    public async Task El_listado_trae_la_cantidad_de_variantes_activas_y_la_portada()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var s = Para(db, _bd.TiendaA);
        var foto = await SubirFoto(s);
        await s.Productos.CrearAsync(Dto(colores: ["a", "b", "c"], fotos: [foto]), default);

        var item = (await s.Productos.ListarAsync(new(), default)).Items.Single();

        Assert.Equal(3, item.Variantes);
        Assert.Equal($"/archivos/{_bd.TiendaA}/imagenes/{foto}/catalogo.webp", item.Portada!.Catalogo);
    }

    // ───────────── Aislamiento entre tiendas ─────────────

    [Fact]
    public async Task Una_tienda_no_ve_ni_edita_ni_archiva_los_productos_de_otra()
    {
        Guid deA;
        await using (var dbA = _bd.Contexto(_bd.TiendaA))
            deA = (await Para(dbA, _bd.TiendaA).Productos.CrearAsync(Dto(colores: ["rojo"]), default)).Id;

        await using var dbB = _bd.Contexto(_bd.TiendaB);
        var b = Para(dbB, _bd.TiendaB);

        Assert.Empty((await b.Productos.ListarAsync(new(), default)).Items);
        Assert.Empty((await b.Productos.ListarAsync(new(Buscar: "cartera"), default)).Items);
        await Assert.ThrowsAsync<ProductoNoEncontradoException>(() => b.Productos.ObtenerAsync(deA, default));
        await Assert.ThrowsAsync<ProductoNoEncontradoException>(() => b.Productos.ActualizarAsync(deA, Dto("Robado"), default));
        await Assert.ThrowsAsync<ProductoNoEncontradoException>(() => b.Productos.ArchivarAsync(deA, true, default));

        await using var verificar = _bd.Contexto(_bd.TiendaA);
        var intacto = await Para(verificar, _bd.TiendaA).Productos.ObtenerAsync(deA, default);
        Assert.Equal("Cartera de cuero Mía", intacto.Nombre);
        Assert.False(intacto.Archivado);
    }

    [Fact]
    public async Task Sin_tienda_en_sesion_no_se_ve_ni_se_crea_nada()
    {
        await using var db = _bd.Contexto(null);
        var s = Para(db, null);

        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => s.Productos.ListarAsync(new(), default));
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => s.Productos.CrearAsync(Dto(), default));
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => s.Productos.ObtenerAsync(Guid.NewGuid(), default));
    }
}
