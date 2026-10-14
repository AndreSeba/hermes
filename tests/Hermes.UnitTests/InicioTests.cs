using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Domain;
using Hermes.Domain.Enums;
using Hermes.Infrastructure.Imagenes;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Services;
using Hermes.Infrastructure.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hermes.UnitTests;

public class InicioTests : IDisposable
{
    private readonly BaseDePrueba _bd = new();
    private readonly FakeTimeProvider _reloj = new(new DateTimeOffset(2026, 10, 14, 15, 0, 0, TimeSpan.Zero));

    public void Dispose() => _bd.Dispose();

    private record Mundo(HermesDbContext Db, ProductoService Productos, StockService Stock, VentaService Ventas, ProveedorService Proveedores,
        CompraService Compras, GastoService Gastos, InicioService Inicio);

    private Mundo Para(HermesDbContext db, Guid? tienda)
    {
        var almacen = new AlmacenEnBaseDeDatos(db, _reloj);
        var sesion = new BaseDePrueba.TiendaFija(tienda);
        var imagenes = new ImagenService(db, almacen, sesion, _reloj);
        var operaciones = new OperacionesStock(db, _reloj);
        var stock = new StockService(db, operaciones, almacen, sesion);
        var ventas = new VentaService(db, operaciones, almacen, sesion, _reloj);
        var compras = new CompraService(db, operaciones, sesion, _reloj);
        var gastos = new GastoService(db, sesion, _reloj);
        return new Mundo(db, new ProductoService(db, imagenes, almacen, sesion, _reloj), stock, ventas, new ProveedorService(db, sesion), compras, gastos,
            new InicioService(ventas, compras, stock, gastos));
    }

    private static async Task<ProductoDto> ConStock(Mundo m, string nombre, int unidades, decimal precio = 180, decimal costo = 110)
    {
        var p = await m.Productos.CrearAsync(new GuardarProductoDto(nombre, null, precio, costo, null, true, null, null, null), default);
        if (unidades > 0) await m.Stock.IngresarAsync(new(p.Id, [new(p.Variantes[0].Id, unidades)]), default);
        return p;
    }

    private static RegistrarGastoDto Gasta(string concepto, decimal monto, DateTimeOffset? fecha = null, Guid? clave = null)
        => new(clave ?? Guid.NewGuid(), concepto, monto, fecha);

    private static Task<ResultadoVenta> Vende(Mundo m, ProductoDto p, int cantidad)
        => m.Ventas.RegistrarAsync(new(Guid.NewGuid(), [new(p.Variantes[0].Id, cantidad)], MetodoPago.Efectivo), default);

    private DateTimeOffset Hoy => _reloj.GetUtcNow();
    private static readonly FiltroPeriodoDto Octubre = new(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));

    // ───────────── Gastos ─────────────

    [Fact]
    public async Task Un_gasto_se_registra_con_el_concepto_limpio_y_la_fecha_de_hoy()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);

        var r = await m.Gastos.RegistrarAsync(Gasta("  Alquiler del local ", 800), default);

        Assert.True(r.Creado);
        Assert.Equal(("Alquiler del local", 800m, false, Hoy), (r.Gasto.Concepto, r.Gasto.Monto, r.Gasto.Anulado, r.Gasto.Fecha));
    }

    [Fact]
    public async Task Un_gasto_puede_ser_de_una_fecha_pasada_pero_no_futura()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);

        var pasado = await m.Gastos.RegistrarAsync(Gasta("Luz de septiembre", 120, new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero)), default);
        var manana = await m.Gastos.RegistrarAsync(Gasta("Con un día de margen por la zona horaria", 5, Hoy.AddHours(20)), default);

        Assert.Equal(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero), pasado.Gasto.Fecha);
        Assert.True(manana.Creado);
        await Assert.ThrowsAsync<ValidacionException>(() => m.Gastos.RegistrarAsync(Gasta("Futuro", 5, Hoy.AddDays(3)), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Gastos.RegistrarAsync(Gasta("Antiguo", 5, new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero)), default));
    }

    [Fact]
    public async Task Las_entradas_invalidas_de_un_gasto_se_rechazan_sin_guardar_nada()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);

        await Assert.ThrowsAsync<ValidacionException>(() => m.Gastos.RegistrarAsync(Gasta("  ", 10), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Gastos.RegistrarAsync(Gasta(new string('x', 121), 10), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Gastos.RegistrarAsync(Gasta("Luz", 0), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Gastos.RegistrarAsync(Gasta("Luz", -5), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Gastos.RegistrarAsync(Gasta("Luz", 10.555m), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Gastos.RegistrarAsync(Gasta("Luz", 2_000_000), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Gastos.RegistrarAsync(Gasta("Luz", 10, clave: Guid.Empty), default));

        Assert.Equal(0, await db.Gastos.CountAsync());
    }

    [Fact]
    public async Task La_misma_clave_devuelve_el_mismo_gasto_y_no_lo_duplica()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var clave = Guid.NewGuid();

        var primero = await m.Gastos.RegistrarAsync(Gasta("Alquiler", 800, clave: clave), default);
        var segundo = await m.Gastos.RegistrarAsync(Gasta("Alquiler", 800, clave: clave), default);

        Assert.True(primero.Creado);
        Assert.False(segundo.Creado);
        Assert.Equal(primero.Gasto.Id, segundo.Gasto.Id);
        Assert.Equal(1, await db.Gastos.CountAsync());
    }

    [Fact]
    public async Task Dos_tiendas_pueden_usar_la_misma_clave_de_gasto()
    {
        var clave = Guid.NewGuid();
        foreach (var tienda in new[] { _bd.TiendaA, _bd.TiendaB })
        {
            await using var db = _bd.Contexto(tienda);
            Assert.True((await Para(db, tienda).Gastos.RegistrarAsync(Gasta("Alquiler", 800, clave: clave), default)).Creado);
        }
    }

    [Fact]
    public async Task Anular_un_gasto_lo_saca_de_la_cuenta_y_no_se_anula_dos_veces()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var g = (await m.Gastos.RegistrarAsync(Gasta("Publicidad", 200), default)).Gasto;
        _reloj.Advance(TimeSpan.FromMinutes(10));

        var anulado = await m.Gastos.AnularAsync(g.Id, default);

        Assert.True(anulado.Anulado);
        Assert.Equal(Hoy, anulado.AnuladoEn);
        Assert.Equal(0m, await m.Gastos.TotalAsync(null, null, default));
        await Assert.ThrowsAsync<GastoYaAnuladoException>(() => m.Gastos.AnularAsync(g.Id, default));
        await Assert.ThrowsAsync<GastoNoEncontradoException>(() => m.Gastos.AnularAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task El_listado_va_del_mas_nuevo_oculta_anulados_filtra_por_fecha_y_el_total_es_del_periodo()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        await m.Gastos.RegistrarAsync(Gasta("De septiembre", 1000, new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero)), default);
        // Una pausa entre cada uno: los Guid v7 del mismo milisegundo no tienen orden garantizado (en la vida real no pasa).
        var luz = (await m.Gastos.RegistrarAsync(Gasta("Luz", 120), default)).Gasto;
        await Task.Delay(5);
        var alquiler = (await m.Gastos.RegistrarAsync(Gasta("Alquiler", 800), default)).Gasto;
        await Task.Delay(5);
        var errado = (await m.Gastos.RegistrarAsync(Gasta("Cargado por error", 9999), default)).Gasto;
        await m.Gastos.AnularAsync(errado.Id, default);

        var octubre = await m.Gastos.ListarAsync(new(Octubre.Desde, Octubre.Hasta), default);
        var todos = await m.Gastos.ListarAsync(new(IncluirAnulados: true), default);

        Assert.Equal([alquiler.Id, luz.Id], octubre.Items.Select(g => g.Id));
        Assert.Equal(920m, octubre.Total);
        Assert.Equal(4, todos.Items.Count);
        Assert.True(todos.Items[0].Anulado);
        Assert.Equal(1920m, (await m.Gastos.ListarAsync(new(), default)).Total); // sin filtro: septiembre también; el anulado nunca
    }

    [Fact]
    public async Task El_total_del_periodo_no_depende_de_la_pagina_y_el_cursor_no_repite()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        for (var i = 1; i <= 5; i++) await m.Gastos.RegistrarAsync(Gasta($"Gasto {i}", 100 * i), default);

        var primera = await m.Gastos.ListarAsync(new(Limite: 2), default);
        var vistos = primera.Items.Select(g => g.Id).ToList();
        var cursor = primera.Siguiente;
        while (cursor is not null)
        {
            var pagina = await m.Gastos.ListarAsync(new(Limite: 2, Cursor: cursor), default);
            vistos.AddRange(pagina.Items.Select(g => g.Id));
            cursor = pagina.Siguiente;
            Assert.Equal(1500m, pagina.Total);
        }

        Assert.Equal(5, vistos.Distinct().Count());
        Assert.Equal(1500m, primera.Total);
    }

    [Fact]
    public async Task Una_tienda_no_ve_ni_anula_los_gastos_de_otra()
    {
        Guid gastoDeA;
        await using (var dbA = _bd.Contexto(_bd.TiendaA))
            gastoDeA = (await Para(dbA, _bd.TiendaA).Gastos.RegistrarAsync(Gasta("Alquiler", 800), default)).Gasto.Id;

        await using var dbB = _bd.Contexto(_bd.TiendaB);
        var b = Para(dbB, _bd.TiendaB);
        Assert.Empty((await b.Gastos.ListarAsync(new(IncluirAnulados: true), default)).Items);
        Assert.Equal(0m, await b.Gastos.TotalAsync(null, null, default));
        await Assert.ThrowsAsync<GastoNoEncontradoException>(() => b.Gastos.AnularAsync(gastoDeA, default));
        await using var verificar = _bd.Contexto(_bd.TiendaA);
        Assert.False((await Para(verificar, _bd.TiendaA).Gastos.ListarAsync(new(), default)).Items.Single().Anulado);
    }

    // ───────────── Inicio ─────────────

    [Fact]
    public async Task Una_tienda_nueva_tiene_el_inicio_en_cero_y_sin_porcentaje()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);

        var r = await m.Inicio.ResumenAsync(Octubre, default);

        Assert.Equal((0, 0, 0m, 0m, 0m), (r.Ventas, r.Unidades, r.TotalVendido, r.Ganancia, r.Gastos));
        Assert.Null(r.PorcentajeCubierto);
        Assert.Equal((0m, 0, 0, 0), (r.PorPagar, r.EnCamino, r.Agotados, r.PorAgotarse));
        Assert.Empty(r.ParaReponer);
        Assert.Empty(r.Recientes);
    }

    [Fact]
    public async Task El_inicio_cruza_ganancia_con_gastos_y_dice_cuanto_de_los_gastos_ya_se_cubrio()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await ConStock(m, "Cartera", 20); // gana 70 por unidad
        await Vende(m, p, 5);                       // total 900, ganancia 350
        await m.Gastos.RegistrarAsync(Gasta("Alquiler", 800), default);

        var r = await m.Inicio.ResumenAsync(Octubre, default);

        Assert.Equal((1, 5, 900m, 350m), (r.Ventas, r.Unidades, r.TotalVendido, r.Ganancia));
        Assert.Equal((800m, 43, -450m), (r.Gastos, r.PorcentajeCubierto, r.Resultado)); // 350 ÷ 800 = 43,75 → 43 (se redondea hacia abajo: no se promete de más)
    }

    [Fact]
    public async Task Cuando_la_ganancia_pasa_los_gastos_el_porcentaje_pasa_de_cien_y_el_resultado_es_positivo()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await ConStock(m, "Cartera", 20);
        await Vende(m, p, 10); // ganancia 700
        await m.Gastos.RegistrarAsync(Gasta("Luz", 200), default);

        var r = await m.Inicio.ResumenAsync(Octubre, default);

        Assert.Equal((350, 500m), (r.PorcentajeCubierto, r.Resultado));
    }

    [Fact]
    public async Task Con_ganancia_negativa_el_porcentaje_cubierto_es_cero_no_negativo()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await ConStock(m, "Pierde", 10, precio: 100, costo: 150);
        await Vende(m, p, 2); // pierde 100
        await m.Gastos.RegistrarAsync(Gasta("Luz", 50), default);

        var r = await m.Inicio.ResumenAsync(Octubre, default);

        Assert.Equal((-100m, 0, -150m), (r.Ganancia, r.PorcentajeCubierto, r.Resultado));
    }

    [Fact]
    public async Task El_inicio_solo_cuenta_lo_del_periodo_y_no_lo_anulado()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await ConStock(m, "Cartera", 50);
        await m.Gastos.RegistrarAsync(Gasta("De septiembre", 1000, new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero)), default);
        var anulado = (await m.Gastos.RegistrarAsync(Gasta("Error", 777), default)).Gasto;
        await m.Gastos.AnularAsync(anulado.Id, default);
        await m.Gastos.RegistrarAsync(Gasta("Luz", 100), default);
        var venta = (await Vende(m, p, 3)).Venta;
        await m.Ventas.AnularAsync(venta.Id, default);
        await Vende(m, p, 1);

        var r = await m.Inicio.ResumenAsync(Octubre, default);

        Assert.Equal((1, 1, 180m, 70m, 100m), (r.Ventas, r.Unidades, r.TotalVendido, r.Ganancia, r.Gastos));
    }

    [Fact]
    public async Task El_inicio_dice_lo_que_se_debe_lo_que_esta_en_camino_y_que_hay_que_reponer()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var agotado = await ConStock(m, "Agotado", 0);
        var pocos = await ConStock(m, "Por agotarse", 2);
        await ConStock(m, "Bien", 20);
        var proveedor = await m.Proveedores.CrearAsync(new("Don Beto", null), default);
        await m.Compras.RegistrarAsync(new(Guid.NewGuid(), proveedor.Id, [new(pocos.Variantes[0].Id, 10, 60)]), default); // en camino: 600
        await m.Compras.RegistrarAsync(new(Guid.NewGuid(), proveedor.Id, [new(agotado.Variantes[0].Id, 1, 50)], null, Recibida: true, Pagada: false), default);

        var r = await m.Inicio.ResumenAsync(Octubre, default);

        Assert.Equal((650m, 1), (r.PorPagar, r.EnCamino));
        Assert.Equal((0, 2), (r.Agotados, r.PorAgotarse)); // el agotado llegó (recibida): ahora tiene 1, que es "pocos", como el otro (2)
        Assert.Equal(new[] { "Agotado", "Por agotarse" }, r.ParaReponer.Select(x => x.Nombre).Order());
        Assert.All(r.ParaReponer, x => Assert.Equal(EstadoStock.Pocos, x.Estado));
    }

    [Fact]
    public async Task Para_reponer_trae_los_mas_urgentes_primero_y_a_lo_sumo_cinco()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        for (var i = 0; i < 6; i++) await ConStock(m, $"Pocos {i}", 2);
        await ConStock(m, "Agotado", 0);

        var r = await m.Inicio.ResumenAsync(Octubre, default);

        Assert.Equal(5, r.ParaReponer.Count);
        Assert.Equal(EstadoStock.Agotado, r.ParaReponer[0].Estado); // el agotado va primero
        Assert.Equal((1, 6), (r.Agotados, r.PorAgotarse));           // el resumen cuenta todos, la lista muestra 5
    }

    [Fact]
    public async Task Los_recientes_mezclan_ventas_compras_y_gastos_por_fecha_sin_anuladas()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await ConStock(m, "Cartera", 50);
        var proveedor = await m.Proveedores.CrearAsync(new("Don Beto", null), default);

        await Vende(m, p, 1);                                                                                     // 1
        _reloj.Advance(TimeSpan.FromMinutes(1));
        await m.Compras.RegistrarAsync(new(Guid.NewGuid(), proveedor.Id, [new(p.Variantes[0].Id, 2, 60)]), default); // 2
        _reloj.Advance(TimeSpan.FromMinutes(1));
        await m.Gastos.RegistrarAsync(Gasta("Alquiler", 800), default);                                           // 3
        _reloj.Advance(TimeSpan.FromMinutes(1));
        var anulada = (await Vende(m, p, 1)).Venta;                                                              // anulada: no sale
        await m.Ventas.AnularAsync(anulada.Id, default);

        var r = await m.Inicio.ResumenAsync(Octubre, default);

        Assert.Equal([TipoActividad.Gasto, TipoActividad.Compra, TipoActividad.Venta], r.Recientes.Select(a => a.Tipo));
        Assert.Equal(["Alquiler", "Don Beto"], r.Recientes.Take(2).Select(a => a.Titulo));
        Assert.Equal([800m, 120m, 180m], r.Recientes.Select(a => a.Monto));
    }

    [Fact]
    public async Task Los_recientes_son_a_lo_sumo_seis()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        for (var i = 0; i < 8; i++)
        {
            await m.Gastos.RegistrarAsync(Gasta($"Gasto {i}", 10 + i), default);
            _reloj.Advance(TimeSpan.FromMinutes(1));
        }

        var r = await m.Inicio.ResumenAsync(Octubre, default);

        Assert.Equal(6, r.Recientes.Count);
        Assert.Equal("Gasto 7", r.Recientes[0].Titulo);
    }

    [Fact]
    public async Task El_inicio_de_una_tienda_no_ve_nada_de_otra_y_sin_tienda_falla()
    {
        await using (var dbA = _bd.Contexto(_bd.TiendaA))
        {
            var a = Para(dbA, _bd.TiendaA);
            var p = await ConStock(a, "Cartera", 10);
            await Vende(a, p, 2);
            await a.Gastos.RegistrarAsync(Gasta("Alquiler", 800), default);
        }

        await using var dbB = _bd.Contexto(_bd.TiendaB);
        var r = await Para(dbB, _bd.TiendaB).Inicio.ResumenAsync(Octubre, default);
        Assert.Equal((0, 0m, 0m), (r.Ventas, r.TotalVendido, r.Gastos));
        Assert.Empty(r.Recientes);

        await using var sinTienda = _bd.Contexto(null);
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => Para(sinTienda, null).Inicio.ResumenAsync(Octubre, default));
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => Para(sinTienda, null).Gastos.RegistrarAsync(Gasta("X", 1), default));
    }
}
