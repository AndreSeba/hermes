using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Domain;
using Hermes.Domain.Enums;
using Hermes.Infrastructure.Imagenes;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Services;
using Hermes.Infrastructure.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hermes.UnitTests;

public class VentaTests : IDisposable
{
    private readonly BaseDePrueba _bd = new();
    private readonly FakeTimeProvider _reloj = new(new DateTimeOffset(2026, 10, 1, 15, 0, 0, TimeSpan.Zero));

    public void Dispose() => _bd.Dispose();

    private record Mundo(HermesDbContext Db, ProductoService Productos, StockService Stock, VentaService Ventas);

    private Mundo Para(HermesDbContext db, Guid? tienda)
    {
        var almacen = new AlmacenEnBaseDeDatos(db, _reloj);
        var sesion = new BaseDePrueba.TiendaFija(tienda);
        var imagenes = new ImagenService(db, almacen, sesion, _reloj);
        var operaciones = new OperacionesStock(db, _reloj);
        return new Mundo(db, new ProductoService(db, imagenes, almacen, sesion, _reloj),
            new StockService(db, operaciones, almacen, sesion), new VentaService(db, operaciones, almacen, sesion, _reloj));
    }

    private static Task<ProductoDto> Crear(Mundo m, string nombre = "Cartera de cuero Mía", string[]? colores = null,
        bool controla = true, decimal precio = 180, decimal costo = 110)
        => m.Productos.CrearAsync(new GuardarProductoDto(nombre, null, precio, costo, null, true, colores, null, null, controla), default);

    // Crea un producto y le carga stock a cada variante.
    private static async Task<ProductoDto> ConStock(Mundo m, int unidades, string nombre = "Cartera de cuero Mía", string[]? colores = null,
        decimal precio = 180, decimal costo = 110)
    {
        var p = await Crear(m, nombre, colores, true, precio, costo);
        await m.Stock.IngresarAsync(new(p.Id, p.Variantes.Select(v => new LineaIngresoDto(v.Id, unidades)).ToList()), default);
        return p;
    }

    private static RegistrarVentaDto Vende(Guid variante, int cantidad, MetodoPago pago = MetodoPago.Efectivo, string? cliente = null, Guid? clave = null)
        => new(clave ?? Guid.NewGuid(), [new(variante, cantidad)], pago, cliente);

    private static async Task<int> StockDe(Mundo m, Guid productoId) => (await m.Stock.ObtenerAsync(productoId, default)).Stock ?? -1;

    // ───────────── Registrar ─────────────

    [Fact]
    public async Task Una_venta_guarda_total_ganancia_y_descuenta_el_stock()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await ConStock(m, 5);

        var r = await m.Ventas.RegistrarAsync(Vende(p.Variantes[0].Id, 2, MetodoPago.Qr, "  Ana  "), default);

        Assert.True(r.Creada);
        var v = r.Venta;
        Assert.Equal((360m, 140m, 2), (v.Total, v.Ganancia, v.Unidades));
        Assert.Equal((MetodoPago.Qr, "Ana", false), (v.MetodoPago, v.Cliente, v.Anulada));
        var linea = Assert.Single(v.Lineas);
        Assert.Equal(("Cartera de cuero Mía", 2, 180m, 110m, 360m, 140m), (linea.Descripcion, linea.Cantidad, linea.PrecioUnitario, linea.CostoUnitario, linea.Subtotal, linea.Ganancia));
        Assert.Equal(_reloj.GetUtcNow(), v.Fecha);
        Assert.Equal(3, await StockDe(m, p.Id));

        var movimiento = (await m.Stock.MovimientosAsync(p.Id, new(), default)).Items[0];
        Assert.Equal((TipoMovimiento.Venta, -2, 3, "Venta"), (movimiento.Tipo, movimiento.Cantidad, movimiento.Saldo, movimiento.Nota));
        Assert.Equal(v.Id, (await db.Movimientos.OrderByDescending(x => x.Id).FirstAsync()).VentaId);
    }

    [Fact]
    public async Task Una_venta_con_varias_variantes_y_productos_descuenta_cada_una_y_la_descripcion_aclara_la_variante()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var cartera = await ConStock(m, 4, colores: ["café", "negro"]);
        var aretes = await ConStock(m, 10, "Aretes de plata Killa", precio: 95, costo: 38);

        var r = await m.Ventas.RegistrarAsync(new(Guid.NewGuid(),
            [new(cartera.Variantes[1].Id, 1), new(aretes.Variantes[0].Id, 3), new(cartera.Variantes[0].Id, 2)], MetodoPago.Tarjeta), default);

        Assert.Equal(1 * 180 + 3 * 95 + 2 * 180, r.Venta.Total);
        Assert.Equal(6, r.Venta.Unidades);
        Assert.Contains(r.Venta.Lineas, l => l.Descripcion == "Cartera de cuero Mía (negro)");
        Assert.Contains(r.Venta.Lineas, l => l.Descripcion == "Aretes de plata Killa");
        var stock = (await m.Stock.ObtenerAsync(cartera.Id, default)).Variantes.Select(v => v.Stock).ToList();
        Assert.Equal([2, 3], stock);
        Assert.Equal(7, await StockDe(m, aretes.Id));
    }

    [Fact]
    public async Task La_venta_guarda_una_foto_del_momento_y_editar_el_producto_despues_no_la_cambia()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await ConStock(m, 5);
        var venta = (await m.Ventas.RegistrarAsync(Vende(p.Variantes[0].Id, 1), default)).Venta;

        await m.Productos.ActualizarAsync(p.Id, new GuardarProductoDto("Otro nombre", null, 999, 500, null, true, null, null, null), default);

        var despues = await m.Ventas.ObtenerAsync(venta.Id, default);
        Assert.Equal(("Cartera de cuero Mía", 180m, 110m, 180m, 70m), (despues.Lineas[0].Descripcion, despues.Lineas[0].PrecioUnitario, despues.Lineas[0].CostoUnitario, despues.Total, despues.Ganancia));
    }

    [Fact]
    public async Task Vender_mas_de_lo_que_hay_falla_y_no_guarda_ni_la_venta_ni_el_descuento()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var cartera = await ConStock(m, 3);
        var aretes = await ConStock(m, 1, "Aretes", precio: 95, costo: 38);

        // La primera línea alcanza, la segunda no: no se guarda NADA (ni siquiera la primera).
        var ex = await Assert.ThrowsAsync<StockInsuficienteException>(() => m.Ventas.RegistrarAsync(new(Guid.NewGuid(),
            [new(cartera.Variantes[0].Id, 2), new(aretes.Variantes[0].Id, 5)], MetodoPago.Efectivo), default));
        Assert.Contains("hay 1 y se necesitan 5", ex.Message);

        await using var limpio = _bd.Contexto(_bd.TiendaA);
        var m2 = Para(limpio, _bd.TiendaA);
        Assert.Equal(0, await limpio.Ventas.CountAsync());
        Assert.Equal(0, await limpio.VentaLineas.CountAsync());
        Assert.Equal(3, await StockDe(m2, cartera.Id));
        Assert.Equal(1, await StockDe(m2, aretes.Id));
    }

    [Fact]
    public async Task Vender_exactamente_lo_que_hay_deja_el_producto_agotado()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await ConStock(m, 2);

        await m.Ventas.RegistrarAsync(Vende(p.Variantes[0].Id, 2), default);

        Assert.Equal(EstadoStock.Agotado, (await m.Stock.ObtenerAsync(p.Id, default)).Estado);
        await Assert.ThrowsAsync<StockInsuficienteException>(() => m.Ventas.RegistrarAsync(Vende(p.Variantes[0].Id, 1), default));
    }

    [Fact]
    public async Task Un_producto_por_encargo_se_vende_siempre_y_no_toca_el_stock()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m, "Bolso por encargo", controla: false, precio: 220, costo: 120);

        var venta = (await m.Ventas.RegistrarAsync(Vende(p.Variantes[0].Id, 3), default)).Venta;

        Assert.Equal((660m, 300m), (venta.Total, venta.Ganancia));
        Assert.Equal(0, await db.Movimientos.CountAsync());
        Assert.True((await db.VentaLineas.SingleAsync()).DescontoStock == false);
    }

    [Fact]
    public async Task Una_venta_mixta_descuenta_solo_lo_que_lleva_control()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var conControl = await ConStock(m, 5);
        var porEncargo = await Crear(m, "Por encargo", controla: false);

        await m.Ventas.RegistrarAsync(new(Guid.NewGuid(), [new(conControl.Variantes[0].Id, 2), new(porEncargo.Variantes[0].Id, 4)], MetodoPago.Efectivo), default);

        Assert.Equal(3, await StockDe(m, conControl.Id));
        Assert.Equal(1 + 1, await db.Movimientos.CountAsync()); // la llegada + la venta; nada del por encargo
    }

    [Fact]
    public async Task Sin_costo_cargado_no_se_inventa_la_ganancia()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var sinCosto = await ConStock(m, 5, "Sin costo", precio: 40, costo: 0);
        var conCosto = await ConStock(m, 5, "Con costo", precio: 100, costo: 60);

        var solo = (await m.Ventas.RegistrarAsync(Vende(sinCosto.Variantes[0].Id, 2), default)).Venta;
        Assert.Null(solo.Ganancia);
        Assert.Null(solo.Lineas[0].CostoUnitario);
        Assert.Null(solo.Lineas[0].Ganancia);

        var mixta = (await m.Ventas.RegistrarAsync(new(Guid.NewGuid(), [new(sinCosto.Variantes[0].Id, 1), new(conCosto.Variantes[0].Id, 1)], MetodoPago.Qr), default)).Venta;
        Assert.Null(mixta.Ganancia); // basta una línea sin costo para que no se pueda decir cuánto se ganó
        Assert.Equal(40m, mixta.Lineas.Single(l => l.Descripcion == "Con costo").Ganancia);

        var resumen = await m.Ventas.ResumenAsync(new(), default);
        Assert.Equal(40m, resumen.Ganancia);          // solo suma lo que tiene costo conocido
        Assert.Equal(3, resumen.UnidadesSinCosto);    // y avisa cuántas unidades quedaron afuera
    }

    [Fact]
    public async Task No_se_vende_un_producto_archivado_ni_una_variante_que_ya_no_se_vende_ni_una_ajena()
    {
        Guid varianteDeB;
        await using (var dbB = _bd.Contexto(_bd.TiendaB))
            varianteDeB = (await Crear(Para(dbB, _bd.TiendaB))).Variantes[0].Id;

        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var archivado = await ConStock(m, 5, "Archivado");
        await m.Productos.ArchivarAsync(archivado.Id, true, default);
        var conColores = await ConStock(m, 5, "Con colores", ["rojo", "azul"]);
        await m.Stock.AjustarAsync(new(conColores.Id, conColores.Variantes.Select(v => new LineaAjusteDto(v.Id, 0)).ToList()), default);
        var sinRojo = await m.Productos.ActualizarAsync(conColores.Id, new GuardarProductoDto("Con colores", null, 180, 110, null, true, ["azul"], null, null), default);

        await Assert.ThrowsAsync<ValidacionException>(() => m.Ventas.RegistrarAsync(Vende(archivado.Variantes[0].Id, 1), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Ventas.RegistrarAsync(Vende(conColores.Variantes[0].Id, 1), default)); // "rojo": ya no se vende
        await Assert.ThrowsAsync<VarianteNoEncontradaException>(() => m.Ventas.RegistrarAsync(Vende(varianteDeB, 1), default));
        await Assert.ThrowsAsync<VarianteNoEncontradaException>(() => m.Ventas.RegistrarAsync(Vende(Guid.NewGuid(), 1), default));
        Assert.Equal(0, await db.Ventas.CountAsync());
        Assert.Single(sinRojo.Variantes);
    }

    [Fact]
    public async Task Las_entradas_invalidas_se_rechazan_sin_guardar_nada()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await ConStock(m, 5);
        var v = p.Variantes[0].Id;

        await Assert.ThrowsAsync<ValidacionException>(() => m.Ventas.RegistrarAsync(new(Guid.NewGuid(), [], MetodoPago.Efectivo), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Ventas.RegistrarAsync(Vende(v, 0), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Ventas.RegistrarAsync(Vende(v, -1), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Ventas.RegistrarAsync(Vende(v, 100_001), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Ventas.RegistrarAsync(Vende(v, 1, (MetodoPago)99), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Ventas.RegistrarAsync(Vende(v, 1, cliente: new string('x', 121)), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Ventas.RegistrarAsync(Vende(v, 1, clave: Guid.Empty), default));
        var cincuentaYUna = Enumerable.Range(0, 51).Select(_ => new LineaVentaDto(Guid.NewGuid(), 1)).ToList();
        await Assert.ThrowsAsync<ValidacionException>(() => m.Ventas.RegistrarAsync(new(Guid.NewGuid(), cincuentaYUna, MetodoPago.Efectivo), default));

        Assert.Equal(0, await db.Ventas.CountAsync());
        Assert.Equal(5, await StockDe(m, p.Id));
    }

    [Fact]
    public async Task Una_variante_repetida_en_la_venta_se_junta_en_una_sola_linea()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await ConStock(m, 5);
        var v = p.Variantes[0].Id;

        var venta = (await m.Ventas.RegistrarAsync(new(Guid.NewGuid(), [new(v, 2), new(v, 1)], MetodoPago.Efectivo), default)).Venta;

        Assert.Equal(3, Assert.Single(venta.Lineas).Cantidad);
        Assert.Equal(2, await StockDe(m, p.Id));
    }

    // ───────────── Antidoble toque ─────────────

    [Fact]
    public async Task La_misma_clave_devuelve_la_misma_venta_y_descuenta_una_sola_vez()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await ConStock(m, 10);
        var clave = Guid.NewGuid();

        var primera = await m.Ventas.RegistrarAsync(Vende(p.Variantes[0].Id, 3, clave: clave), default);
        var segunda = await m.Ventas.RegistrarAsync(Vende(p.Variantes[0].Id, 3, clave: clave), default);
        // Aunque el reintento llegue con otro contenido, es "la misma venta": se devuelve la ya registrada.
        var tercera = await m.Ventas.RegistrarAsync(Vende(p.Variantes[0].Id, 9, MetodoPago.Qr, clave: clave), default);

        Assert.True(primera.Creada);
        Assert.False(segunda.Creada);
        Assert.False(tercera.Creada);
        Assert.Equal(primera.Venta.Id, segunda.Venta.Id);
        Assert.Equal(primera.Venta.Id, tercera.Venta.Id);
        Assert.Equal(3, tercera.Venta.Unidades);
        Assert.Equal(1, await db.Ventas.CountAsync());
        Assert.Equal(7, await StockDe(m, p.Id));
    }

    [Fact]
    public async Task Dos_tiendas_pueden_usar_la_misma_clave_sin_pisarse()
    {
        var clave = Guid.NewGuid();
        await using (var dbA = _bd.Contexto(_bd.TiendaA))
        {
            var a = Para(dbA, _bd.TiendaA);
            var pa = await ConStock(a, 5);
            Assert.True((await a.Ventas.RegistrarAsync(Vende(pa.Variantes[0].Id, 1, clave: clave), default)).Creada);
        }
        await using var dbB = _bd.Contexto(_bd.TiendaB);
        var b = Para(dbB, _bd.TiendaB);
        var pb = await ConStock(b, 5);

        var deB = await b.Ventas.RegistrarAsync(Vende(pb.Variantes[0].Id, 1, clave: clave), default);

        Assert.True(deB.Creada); // no es un reintento de la tienda A: cada tienda tiene sus propias claves
    }

    // ───────────── Anular ─────────────

    [Fact]
    public async Task Anular_devuelve_el_stock_marca_la_venta_y_deja_el_rastro()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await ConStock(m, 5);
        var venta = (await m.Ventas.RegistrarAsync(Vende(p.Variantes[0].Id, 2), default)).Venta;
        _reloj.Advance(TimeSpan.FromMinutes(30));

        var anulada = await m.Ventas.AnularAsync(venta.Id, default);

        Assert.True(anulada.Anulada);
        Assert.Equal(_reloj.GetUtcNow(), anulada.AnuladaEn);
        Assert.Equal(360m, anulada.Total); // la venta conserva sus números: solo deja de contar
        Assert.Equal(5, await StockDe(m, p.Id));
        var ultimo = (await m.Stock.MovimientosAsync(p.Id, new(), default)).Items[0];
        Assert.Equal((TipoMovimiento.Devolucion, 2, 5, "Venta anulada"), (ultimo.Tipo, ultimo.Cantidad, ultimo.Saldo, ultimo.Nota));
        Assert.Equal(venta.Id, (await db.Movimientos.OrderByDescending(x => x.Id).FirstAsync()).VentaId);
    }

    [Fact]
    public async Task Una_venta_no_se_anula_dos_veces_ni_devuelve_el_stock_dos_veces()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await ConStock(m, 5);
        var venta = (await m.Ventas.RegistrarAsync(Vende(p.Variantes[0].Id, 2), default)).Venta;
        await m.Ventas.AnularAsync(venta.Id, default);

        await Assert.ThrowsAsync<VentaYaAnuladaException>(() => m.Ventas.AnularAsync(venta.Id, default));

        Assert.Equal(5, await StockDe(m, p.Id));
        await Assert.ThrowsAsync<VentaNoEncontradaException>(() => m.Ventas.AnularAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Anular_solo_devuelve_lo_que_se_habia_descontado()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m, "Se vendió por encargo", controla: false);
        var venta = (await m.Ventas.RegistrarAsync(Vende(p.Variantes[0].Id, 3), default)).Venta;

        // Después el producto pasa a llevar control y la dueña carga 4 unidades.
        await m.Productos.ActualizarAsync(p.Id, new GuardarProductoDto(p.Nombre, null, p.Precio, p.Costo, null, true, null, null, null, ControlaStock: true), default);
        await m.Stock.IngresarAsync(new(p.Id, [new(p.Variantes[0].Id, 4)]), default);

        await m.Ventas.AnularAsync(venta.Id, default);

        Assert.Equal(4, await StockDe(m, p.Id)); // no se "devolvieron" 3 unidades que nunca salieron
        Assert.DoesNotContain(TipoMovimiento.Devolucion, await db.Movimientos.Select(x => x.Tipo).ToListAsync());
    }

    [Fact]
    public async Task Anular_devuelve_el_stock_aunque_la_variante_se_haya_desactivado_despues()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await ConStock(m, 5, colores: ["rojo", "azul"]);
        var rojo = p.Variantes[0].Id;
        var venta = (await m.Ventas.RegistrarAsync(Vende(rojo, 2), default)).Venta;
        await m.Stock.AjustarAsync(new(p.Id, [new(rojo, 0), new(p.Variantes[1].Id, 5)]), default);
        await m.Productos.ActualizarAsync(p.Id, new GuardarProductoDto(p.Nombre, null, 180, 110, null, true, ["azul"], null, null), default);

        await m.Ventas.AnularAsync(venta.Id, default);

        // El movimiento de devolución quedó registrado en la variante inactiva (si se reactiva, aparece).
        Assert.Equal(2, await db.Movimientos.Where(x => x.VarianteId == rojo && x.Tipo == TipoMovimiento.Devolucion).SumAsync(x => x.Cantidad));
    }

    // ───────────── Listado y resumen ─────────────

    [Fact]
    public async Task El_listado_va_de_la_mas_nueva_a_la_mas_vieja_resume_el_detalle_y_oculta_las_anuladas()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var a = await ConStock(m, 20, "Cartera");
        var b = await ConStock(m, 20, "Aretes", precio: 95, costo: 38);
        var c = await ConStock(m, 20, "Collar", precio: 120, costo: 58);
        var primera = (await m.Ventas.RegistrarAsync(Vende(a.Variantes[0].Id, 1), default)).Venta;
        var segunda = (await m.Ventas.RegistrarAsync(new(Guid.NewGuid(), [new(a.Variantes[0].Id, 2), new(b.Variantes[0].Id, 1), new(c.Variantes[0].Id, 1)], MetodoPago.Qr), default)).Venta;
        var tercera = (await m.Ventas.RegistrarAsync(Vende(b.Variantes[0].Id, 1), default)).Venta;
        await m.Ventas.AnularAsync(tercera.Id, default);

        var visibles = (await m.Ventas.ListarAsync(new(), default)).Items;
        Assert.Equal([segunda.Id, primera.Id], visibles.Select(v => v.Id));
        // Las líneas de una venta comparten milisegundo: el orden entre ellas no está garantizado.
        Assert.EndsWith(" · y 1 más", visibles[0].Detalle);
        Assert.Equal(2, new[] { "Cartera", "Aretes", "Collar" }.Count(n => visibles[0].Detalle.Contains(n)));

        var todas = (await m.Ventas.ListarAsync(new(IncluirAnuladas: true), default)).Items;
        Assert.Equal([tercera.Id, segunda.Id, primera.Id], todas.Select(v => v.Id));
        Assert.True(todas[0].Anulada);
    }

    [Fact]
    public async Task El_listado_filtra_por_rango_de_fechas_y_pagina_por_cursor()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await ConStock(m, 50);
        var ids = new List<Guid>();
        foreach (var dia in new[] { 12, 13, 14, 14, 14 }) // 5 ventas: una el 12, una el 13 y tres el 14
        {
            _reloj.SetUtcNow(new DateTimeOffset(2026, 10, dia, 14, 0, 0, TimeSpan.Zero));
            ids.Add((await m.Ventas.RegistrarAsync(Vende(p.Variantes[0].Id, 1), default)).Venta.Id);
        }

        var hoy = (await m.Ventas.ListarAsync(new(Desde: new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero), Hasta: new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero)), default)).Items;
        Assert.Equal(3, hoy.Count);
        Assert.Equal(2, (await m.Ventas.ListarAsync(new(Hasta: new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero)), default)).Items.Count); // Hasta excluido

        var vistas = new List<Guid>();
        Guid? cursor = null;
        var paginas = 0;
        do
        {
            var pagina = await m.Ventas.ListarAsync(new(Limite: 2, Cursor: cursor), default);
            vistas.AddRange(pagina.Items.Select(v => v.Id));
            cursor = pagina.Siguiente;
            paginas++;
        } while (cursor is not null);
        Assert.Equal(3, paginas); // 2 + 2 + 1
        Assert.Equal(5, vistas.Distinct().Count());
        Assert.Equal(ids.OrderByDescending(x => x.ToString(), StringComparer.Ordinal), vistas);
    }

    [Fact]
    public async Task El_resumen_cuenta_solo_las_ventas_vigentes_del_periodo()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await ConStock(m, 50);
        _reloj.SetUtcNow(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));
        await m.Ventas.RegistrarAsync(Vende(p.Variantes[0].Id, 5), default); // fuera del período
        _reloj.SetUtcNow(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
        await m.Ventas.RegistrarAsync(Vende(p.Variantes[0].Id, 2), default);
        await m.Ventas.RegistrarAsync(Vende(p.Variantes[0].Id, 1), default);
        var anulada = (await m.Ventas.RegistrarAsync(Vende(p.Variantes[0].Id, 4), default)).Venta;
        await m.Ventas.AnularAsync(anulada.Id, default);
        var periodo = new FiltroPeriodoDto(new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero));

        var r = await m.Ventas.ResumenAsync(periodo, default);

        Assert.Equal((2, 3, 540m, 210m, 0, 1), (r.Ventas, r.Unidades, r.Total, r.Ganancia, r.UnidadesSinCosto, r.Anuladas));
        var todo = await m.Ventas.ResumenAsync(new(), default);
        Assert.Equal((3, 8), (todo.Ventas, todo.Unidades)); // sin filtro entra también la del 10; la anulada nunca
    }

    // ───────────── Elegir qué vender ─────────────

    [Fact]
    public async Task Los_productos_para_vender_van_primero_los_vendibles_y_al_final_los_agotados()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var agotado = await Crear(m, "Agotado");
        var conStock = await ConStock(m, 4, "Con stock", ["rojo", "azul"]);
        var porEncargo = await Crear(m, "Por encargo", controla: false);
        var archivado = await ConStock(m, 4, "Archivado");
        await m.Productos.ArchivarAsync(archivado.Id, true, default);

        var items = (await m.Ventas.ProductosParaVenderAsync(new(), default)).Items;

        Assert.Equal(3, items.Count); // el archivado no se ofrece
        Assert.Equal("Agotado", items[^1].Nombre);
        Assert.Equal(0, items[^1].Stock);
        var con = items.Single(i => i.Nombre == "Con stock");
        Assert.Equal(8, con.Stock);
        Assert.Equal([4, 4], con.Variantes.Select(v => v.Stock));
        Assert.Equal(["rojo", "azul"], con.Variantes.Select(v => v.Color));
        var encargo = items.Single(i => i.Nombre == "Por encargo");
        Assert.Null(encargo.Stock);
        Assert.All(encargo.Variantes, v => Assert.Null(v.Stock));
        Assert.Equal(agotado.Id, items[^1].Id);
        Assert.Equal(porEncargo.Id, encargo.Id);
    }

    [Fact]
    public async Task Los_productos_para_vender_se_buscan_y_se_paginan_sin_repetir()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        for (var i = 1; i <= 4; i++) await ConStock(m, 3, $"Vendible {i}");
        for (var i = 1; i <= 3; i++) await Crear(m, $"Agotado {i}");

        Assert.Equal(["Agotado 2"], (await m.Ventas.ProductosParaVenderAsync(new(Buscar: " AGOTADO 2 "), default)).Items.Select(i => i.Nombre));

        var vistos = new List<Guid>();
        string? cursor = null;
        do
        {
            var pagina = await m.Ventas.ProductosParaVenderAsync(new(Limite: 3, Cursor: cursor), default);
            vistos.AddRange(pagina.Items.Select(i => i.Id));
            cursor = pagina.Siguiente;
        } while (cursor is not null);

        Assert.Equal(7, vistos.Distinct().Count());
        var todos = (await m.Ventas.ProductosParaVenderAsync(new(Limite: 100), default)).Items;
        Assert.Equal(todos.Select(t => t.Id), vistos); // el mismo orden con y sin paginar
        Assert.All(todos.Take(4), t => Assert.True(t.Stock > 0));
        Assert.All(todos.Skip(4), t => Assert.Equal(0, t.Stock));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Ventas.ProductosParaVenderAsync(new(Cursor: "9.xyz"), default));
    }

    // ───────────── Aislamiento entre tiendas ─────────────

    [Fact]
    public async Task Una_tienda_no_ve_ni_anula_las_ventas_de_otra_ni_sus_productos()
    {
        Guid ventaDeA;
        await using (var dbA = _bd.Contexto(_bd.TiendaA))
        {
            var a = Para(dbA, _bd.TiendaA);
            var p = await ConStock(a, 5);
            ventaDeA = (await a.Ventas.RegistrarAsync(Vende(p.Variantes[0].Id, 2), default)).Venta.Id;
        }

        await using var dbB = _bd.Contexto(_bd.TiendaB);
        var b = Para(dbB, _bd.TiendaB);
        Assert.Empty((await b.Ventas.ListarAsync(new(IncluirAnuladas: true), default)).Items);
        Assert.Empty((await b.Ventas.ProductosParaVenderAsync(new(), default)).Items);
        Assert.Equal((0, 0, 0m), ((await b.Ventas.ResumenAsync(new(), default)) is var r ? (r.Ventas, r.Unidades, r.Total) : default));
        await Assert.ThrowsAsync<VentaNoEncontradaException>(() => b.Ventas.ObtenerAsync(ventaDeA, default));
        await Assert.ThrowsAsync<VentaNoEncontradaException>(() => b.Ventas.AnularAsync(ventaDeA, default));

        await using var verificar = _bd.Contexto(_bd.TiendaA);
        Assert.False((await Para(verificar, _bd.TiendaA).Ventas.ObtenerAsync(ventaDeA, default)).Anulada);
    }

    [Fact]
    public async Task Sin_tienda_en_sesion_no_se_ve_ni_se_vende_nada()
    {
        await using var db = _bd.Contexto(null);
        var m = Para(db, null);

        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => m.Ventas.RegistrarAsync(Vende(Guid.NewGuid(), 1), default));
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => m.Ventas.ListarAsync(new(), default));
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => m.Ventas.ProductosParaVenderAsync(new(), default));
    }

    // ───────────── Concurrencia (solo con Postgres real) ─────────────

    // Igual que en StockTests: sin HERMES_TEST_PG sale sin probar nada. Crea y borra su propia base.
    private async Task ConPostgresAsync(Func<Func<Guid?, HermesDbContext>, Guid, Task> prueba)
    {
        var conexion = Environment.GetEnvironmentVariable("HERMES_TEST_PG");
        if (string.IsNullOrWhiteSpace(conexion)) return;

        var baseDeDatos = "hermes_test_" + Guid.NewGuid().ToString("N")[..10];
        await using (var admin = new Npgsql.NpgsqlConnection(conexion + ";Database=postgres"))
        {
            await admin.OpenAsync();
            await using var crear = new Npgsql.NpgsqlCommand($"create database {baseDeDatos}", admin);
            await crear.ExecuteNonQueryAsync();
        }

        HermesDbContext Nuevo(Guid? tienda) => new(
            new DbContextOptionsBuilder<HermesDbContext>().UseNpgsql($"{conexion};Database={baseDeDatos}").UseSnakeCaseNamingConvention().Options,
            new BaseDePrueba.TiendaFija(tienda));

        try
        {
            Guid tienda;
            await using (var db = Nuevo(null))
            {
                await db.Database.EnsureCreatedAsync();
                var dueno = new Hermes.Domain.Entities.Usuario { Nombre = "D", Email = "d@x.test", PasswordHash = "x" };
                var t = new Hermes.Domain.Entities.Tienda { DuenoId = dueno.Id, Slug = "tienda-x", Nombre = "X" };
                db.AddRange(dueno, t);
                await db.SaveChangesAsync();
                tienda = t.Id;
            }
            await prueba(Nuevo, tienda);
        }
        finally
        {
            Npgsql.NpgsqlConnection.ClearAllPools();
            await using var admin = new Npgsql.NpgsqlConnection(conexion + ";Database=postgres");
            await admin.OpenAsync();
            await using var borrar = new Npgsql.NpgsqlCommand($"drop database if exists {baseDeDatos} with (force)", admin);
            await borrar.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Doce_ventas_simultaneas_de_las_ultimas_cinco_unidades_dejan_pasar_exactamente_cinco_con_Postgres_real()
    {
        await ConPostgresAsync(async (nuevo, tienda) =>
        {
            Guid variante, producto;
            await using (var db = nuevo(tienda))
            {
                var p = await ConStock(Para(db, tienda), 5);
                variante = p.Variantes[0].Id;
                producto = p.Id;
            }

            var resultados = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
            {
                await using var db = nuevo(tienda);
                try { await Para(db, tienda).Ventas.RegistrarAsync(Vende(variante, 1), default); return true; }
                catch (StockInsuficienteException) { return false; }
            }));

            Assert.Equal(5, resultados.Count(r => r));
            await using var lectura = nuevo(tienda);
            Assert.Equal(0, await StockDe(Para(lectura, tienda), producto));
            Assert.Equal(5, await lectura.Ventas.CountAsync()); // y no quedaron ventas "fantasma" de las que fallaron
            Assert.Equal(5, await lectura.VentaLineas.CountAsync());
        });
    }

    [Fact]
    public async Task Diez_reintentos_simultaneos_de_la_misma_venta_registran_una_sola_con_Postgres_real()
    {
        await ConPostgresAsync(async (nuevo, tienda) =>
        {
            Guid variante, producto;
            await using (var db = nuevo(tienda))
            {
                var p = await ConStock(Para(db, tienda), 20);
                variante = p.Variantes[0].Id;
                producto = p.Id;
            }
            var clave = Guid.NewGuid();

            var resultados = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
            {
                await using var db = nuevo(tienda);
                return await Para(db, tienda).Ventas.RegistrarAsync(Vende(variante, 3, clave: clave), default);
            }));

            Assert.Equal(1, resultados.Count(r => r.Creada));
            Assert.Single(resultados.Select(r => r.Venta.Id).Distinct());
            await using var lectura = nuevo(tienda);
            Assert.Equal(1, await lectura.Ventas.CountAsync());
            Assert.Equal(17, await StockDe(Para(lectura, tienda), producto)); // se descontó UNA vez
        });
    }

    [Fact]
    public async Task Cinco_anulaciones_simultaneas_devuelven_el_stock_una_sola_vez_con_Postgres_real()
    {
        await ConPostgresAsync(async (nuevo, tienda) =>
        {
            Guid variante, producto, venta;
            await using (var db = nuevo(tienda))
            {
                var m = Para(db, tienda);
                var p = await ConStock(m, 10);
                variante = p.Variantes[0].Id;
                producto = p.Id;
                venta = (await m.Ventas.RegistrarAsync(Vende(variante, 4), default)).Venta.Id;
            }

            var resultados = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
            {
                await using var db = nuevo(tienda);
                try { await Para(db, tienda).Ventas.AnularAsync(venta, default); return true; }
                catch (VentaYaAnuladaException) { return false; }
            }));

            Assert.Equal(1, resultados.Count(r => r));
            await using var lectura = nuevo(tienda);
            Assert.Equal(10, await StockDe(Para(lectura, tienda), producto)); // 10 − 4 + 4, no 10 + 4 × 4
        });
    }
}
