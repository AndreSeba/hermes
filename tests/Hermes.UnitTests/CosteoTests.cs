using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Domain;
using Hermes.Domain.Entities;
using Hermes.Infrastructure.Imagenes;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Services;
using Hermes.Infrastructure.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hermes.UnitTests;

public class CosteoTests : IDisposable
{
    private readonly BaseDePrueba _bd = new();
    private readonly FakeTimeProvider _reloj = new(new DateTimeOffset(2026, 10, 1, 15, 0, 0, TimeSpan.Zero));

    public void Dispose() => _bd.Dispose();

    private record Mundo(HermesDbContext Db, ProductoService Productos, StockService Stock, CosteoService Costeo, ProveedorService Proveedores, CompraService Compras);

    private Mundo Para(HermesDbContext db, Guid? tienda)
    {
        var almacen = new AlmacenEnBaseDeDatos(db, _reloj);
        var sesion = new BaseDePrueba.TiendaFija(tienda);
        var imagenes = new ImagenService(db, almacen, sesion, _reloj);
        var operaciones = new OperacionesStock(db, _reloj);
        return new Mundo(db, new ProductoService(db, imagenes, almacen, sesion, _reloj), new StockService(db, operaciones, almacen, sesion),
            new CosteoService(db, sesion, _reloj), new ProveedorService(db, sesion), new CompraService(db, operaciones, sesion, _reloj));
    }

    private static Task<ProductoDto> Crear(Mundo m, decimal precio = 180, decimal costo = 0)
        => m.Productos.CrearAsync(new GuardarProductoDto("Cartera de cuero Mía", null, precio, costo, null, true, null, null, null), default);

    private static GuardarCosteoDto Costeo(decimal material = 50, decimal empaque = 5, decimal manoDeObra = 30, decimal fijo = 15, int margen = 40)
        => new(material, empaque, manoDeObra, fijo, margen);

    // ───────────── La regla ─────────────

    [Theory]
    [InlineData(100, 40, 167)]   // 100 ÷ 0,6 = 166,67 → 167
    [InlineData(60, 50, 120)]    // exacto
    [InlineData(45.5, 20, 57)]   // 56,875 → 57
    [InlineData(1, 90, 10)]
    public void El_precio_sugerido_es_costo_entre_uno_menos_margen_redondeado_hacia_arriba(double costo, int margen, double esperado)
    {
        var sugerido = ReglaCosteo.PrecioSugerido((decimal)costo, margen);

        Assert.Equal((decimal)esperado, sugerido);
        Assert.True((sugerido - (decimal)costo) / sugerido >= margen / 100m); // redondear hacia arriba nunca come el margen
    }

    [Fact]
    public void Sin_costo_no_hay_precio_sugerido()
    {
        Assert.Equal(0m, ReglaCosteo.PrecioSugerido(0, 40));
        Assert.Equal(100m, ReglaCosteo.Total(50, 5, 30, 15));
    }

    // ───────────── Guardar y leer ─────────────

    [Fact]
    public async Task Un_producto_sin_costeo_muestra_su_costo_directo_y_el_margen_por_defecto()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m, precio: 180, costo: 110);

        var c = await m.Costeo.ObtenerAsync(p.Id, default);

        Assert.False(c.Tiene);
        Assert.Equal((0m, 40, 110m, 184m), (c.Material, c.MargenObjetivo, c.CostoTotal, c.PrecioSugerido)); // 110 ÷ 0,6 = 183,33 → 184
    }

    [Fact]
    public async Task Guardar_el_costeo_deja_el_costo_del_producto_en_el_total_y_no_toca_el_precio()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m, precio: 180, costo: 10);

        var c = await m.Costeo.GuardarAsync(p.Id, Costeo(), default);

        Assert.Equal((true, 100m, 167m, 67m), (c.Tiene, c.CostoTotal, c.PrecioSugerido, c.GananciaConSugerido));
        Assert.Equal((180m, 80m, 44.4m, EstadoPrecio.Ok), (c.PrecioActual, c.GananciaActual, c.MargenActual, c.EstadoActual));
        var guardado = await m.Productos.ObtenerAsync(p.Id, default);
        Assert.Equal((180m, 100m), (guardado.Precio, guardado.Costo)); // el precio real no se toca solo
        Assert.True((await m.Costeo.ObtenerAsync(p.Id, default)).Tiene);
    }

    [Fact]
    public async Task Cambiar_el_material_cambia_el_sugerido_pero_no_el_precio()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m, precio: 180);
        var antes = await m.Costeo.GuardarAsync(p.Id, Costeo(material: 50), default);

        var despues = await m.Costeo.GuardarAsync(p.Id, Costeo(material: 80), default);

        Assert.True(despues.PrecioSugerido > antes.PrecioSugerido);
        Assert.Equal(130m, (await m.Productos.ObtenerAsync(p.Id, default)).Costo);
        Assert.Equal(180m, (await m.Productos.ObtenerAsync(p.Id, default)).Precio);
        Assert.Equal(1, await db.Costeos.CountAsync()); // reemplaza, no duplica
    }

    [Fact]
    public async Task El_costeo_rechaza_numeros_invalidos_sin_guardar_nada()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m, costo: 77);

        await Assert.ThrowsAsync<ValidacionException>(() => m.Costeo.GuardarAsync(p.Id, Costeo(material: -1), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Costeo.GuardarAsync(p.Id, Costeo(empaque: 1.555m), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Costeo.GuardarAsync(p.Id, Costeo(0, 0, 0, 0), default)); // todo en cero
        await Assert.ThrowsAsync<ValidacionException>(() => m.Costeo.GuardarAsync(p.Id, Costeo(margen: 0), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Costeo.GuardarAsync(p.Id, Costeo(margen: 91), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Costeo.GuardarAsync(p.Id, Costeo(material: 2_000_000), default));

        Assert.Equal(0, await db.Costeos.CountAsync());
        Assert.Equal(77m, (await m.Productos.ObtenerAsync(p.Id, default)).Costo);
    }

    [Fact]
    public async Task Calcular_cuenta_sin_guardar()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m, precio: 100, costo: 20);

        var c = await m.Costeo.CalcularAsync(p.Id, Costeo(material: 60, empaque: 0, manoDeObra: 0, fijo: 0, margen: 50), default);

        Assert.Equal((120m, 60m, EstadoPrecio.Ok), (c.PrecioSugerido, c.CostoTotal, c.EstadoActual)); // con precio 100 y costo 60 hoy ganás 40
        Assert.Equal(0, await db.Costeos.CountAsync());
        Assert.Equal(20m, (await m.Productos.ObtenerAsync(p.Id, default)).Costo);
    }

    // ───────────── Quitar ─────────────

    [Fact]
    public async Task Quitar_el_costeo_conserva_el_ultimo_costo_y_es_idempotente()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m);
        await m.Costeo.GuardarAsync(p.Id, Costeo(), default);

        await m.Costeo.QuitarAsync(p.Id, default);
        await m.Costeo.QuitarAsync(p.Id, default);

        Assert.False((await m.Costeo.ObtenerAsync(p.Id, default)).Tiene);
        Assert.Equal(100m, (await m.Productos.ObtenerAsync(p.Id, default)).Costo);
        Assert.Equal(0, await db.Costeos.CountAsync());
    }

    // ───────────── Cómo se mantiene el costo ─────────────

    [Fact]
    public async Task Editar_el_producto_con_costeo_ignora_el_costo_directo_y_sin_costeo_lo_toma()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m, costo: 5);

        await m.Productos.ActualizarAsync(p.Id, new GuardarProductoDto(p.Nombre, null, 180, 40, null, true, null, null, null), default);
        Assert.Equal(40m, (await m.Productos.ObtenerAsync(p.Id, default)).Costo);

        await m.Costeo.GuardarAsync(p.Id, Costeo(), default);
        await m.Productos.ActualizarAsync(p.Id, new GuardarProductoDto(p.Nombre, null, 200, 999, null, true, null, null, null), default);

        var editado = await m.Productos.ObtenerAsync(p.Id, default);
        Assert.Equal((200m, 100m), (editado.Precio, editado.Costo)); // el precio sí cambia; el costo sigue siendo el del costeo
    }

    [Fact]
    public async Task Recibir_una_compra_de_un_producto_con_costeo_actualiza_el_material()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m, precio: 180);
        await m.Costeo.GuardarAsync(p.Id, Costeo(material: 50, empaque: 5, manoDeObra: 30, fijo: 15), default);
        var proveedor = await m.Proveedores.CrearAsync(new("Don Beto", null), default);
        var compra = (await m.Compras.RegistrarAsync(new(Guid.NewGuid(), proveedor.Id, [new(p.Variantes[0].Id, 4, 70)]), default)).Compra;

        var r = await m.Compras.RecibirAsync(compra.Id, default);

        var c = await m.Costeo.ObtenerAsync(p.Id, default);
        Assert.Equal((70m, 120m, 120m), (c.Material, c.CostoTotal, (await m.Productos.ObtenerAsync(p.Id, default)).Costo)); // 70 + 5 + 30 + 15
        Assert.Equal((5m, 30m, 15m), (c.Empaque, c.ManoDeObra, c.CostoFijo)); // lo demás no se toca
        Assert.Equal(180m, (await m.Productos.ObtenerAsync(p.Id, default)).Precio);
        Assert.Equal(180 - 120, c.GananciaActual);
        Assert.Empty(r.Avisos.Where(a => a.Estado == EstadoPrecio.Pierde));
    }

    [Fact]
    public async Task Llegaron_mas_con_costo_actualiza_el_material_si_hay_costeo()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m);
        await m.Costeo.GuardarAsync(p.Id, Costeo(material: 50, empaque: 10, manoDeObra: 0, fijo: 0), default);

        await m.Stock.IngresarAsync(new(p.Id, [new(p.Variantes[0].Id, 3)], CostoUnitario: 65m), default);

        Assert.Equal((65m, 75m), ((await m.Costeo.ObtenerAsync(p.Id, default)).Material, (await m.Productos.ObtenerAsync(p.Id, default)).Costo));
    }

    // ───────────── Aislamiento ─────────────

    [Fact]
    public async Task Una_tienda_no_ve_ni_cambia_el_costeo_de_otra()
    {
        Guid productoDeA;
        await using (var dbA = _bd.Contexto(_bd.TiendaA))
        {
            var a = Para(dbA, _bd.TiendaA);
            productoDeA = (await Crear(a)).Id;
            await a.Costeo.GuardarAsync(productoDeA, Costeo(), default);
        }

        await using var dbB = _bd.Contexto(_bd.TiendaB);
        var b = Para(dbB, _bd.TiendaB);
        await Assert.ThrowsAsync<ProductoNoEncontradoException>(() => b.Costeo.ObtenerAsync(productoDeA, default));
        await Assert.ThrowsAsync<ProductoNoEncontradoException>(() => b.Costeo.CalcularAsync(productoDeA, Costeo(), default));
        await Assert.ThrowsAsync<ProductoNoEncontradoException>(() => b.Costeo.GuardarAsync(productoDeA, Costeo(), default));
        await Assert.ThrowsAsync<ProductoNoEncontradoException>(() => b.Costeo.QuitarAsync(productoDeA, default));

        await using var verificar = _bd.Contexto(_bd.TiendaA);
        Assert.True((await Para(verificar, _bd.TiendaA).Costeo.ObtenerAsync(productoDeA, default)).Tiene);
    }

    [Fact]
    public async Task Sin_tienda_en_sesion_no_se_ve_ni_se_guarda_nada()
    {
        await using var db = _bd.Contexto(null);
        var m = Para(db, null);

        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => m.Costeo.ObtenerAsync(Guid.NewGuid(), default));
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => m.Costeo.GuardarAsync(Guid.NewGuid(), Costeo(), default));
    }
}
