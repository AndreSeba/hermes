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

namespace Hermes.UnitTests;

public class StockTests : IDisposable
{
    private readonly BaseDePrueba _bd = new();

    public void Dispose() => _bd.Dispose();

    private record Mundo(HermesDbContext Db, ProductoService Productos, StockService Stock, OperacionesStock Operaciones);

    private Mundo Para(HermesDbContext db, Guid? tienda)
    {
        var almacen = new AlmacenEnBaseDeDatos(db, TimeProvider.System);
        var sesion = new BaseDePrueba.TiendaFija(tienda);
        var imagenes = new ImagenService(db, almacen, sesion, TimeProvider.System);
        var operaciones = new OperacionesStock(db, TimeProvider.System);
        return new Mundo(db, new ProductoService(db, imagenes, almacen, sesion, TimeProvider.System),
            new StockService(db, operaciones, almacen, sesion), operaciones);
    }

    private static Task<ProductoDto> Crear(Mundo m, string nombre = "Cartera de cuero Mía", string[]? colores = null,
        bool controla = true, int minimo = 3, decimal precio = 180, decimal costo = 110)
        => m.Productos.CrearAsync(new GuardarProductoDto(nombre, null, precio, costo, null, true, colores, null, null, controla, minimo), default);

    private static IngresoDto Llegaron(ProductoDto p, params int[] cantidades)
        => new(p.Id, p.Variantes.Select((v, i) => new LineaIngresoDto(v.Id, i < cantidades.Length ? cantidades[i] : 0)).ToList());

    private static AjusteDto Contar(ProductoDto p, string? motivo, params int[] totales)
        => new(p.Id, p.Variantes.Select((v, i) => new LineaAjusteDto(v.Id, totales[i])).ToList(), motivo);

    // Una operación de stock completa, como la hace una venta: transacción + SaveChanges + commit.
    private static async Task Operar(Mundo m, Func<Task> operacion)
    {
        await using var tx = await m.Db.Database.BeginTransactionAsync();
        await operacion();
        await m.Db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    // ───────────── La regla ─────────────

    [Theory]
    [InlineData(false, 0, 3, EstadoStock.SinControl)]
    [InlineData(false, 99, 3, EstadoStock.SinControl)] // sin control: el número no importa
    [InlineData(true, 0, 3, EstadoStock.Agotado)]
    [InlineData(true, 1, 3, EstadoStock.Pocos)]
    [InlineData(true, 3, 3, EstadoStock.Pocos)]   // justo el mínimo: ya es "por agotarse"
    [InlineData(true, 4, 3, EstadoStock.Ok)]
    [InlineData(true, 1, 0, EstadoStock.Ok)]      // mínimo 0: solo avisa al agotarse
    public void El_estado_del_stock_sigue_la_regla(bool controla, int total, int minimo, EstadoStock esperado)
        => Assert.Equal(esperado, ReglaStock.Estado(controla, total, minimo));

    // ───────────── Llegaron más ─────────────

    [Fact]
    public async Task Un_producto_nuevo_lleva_control_y_arranca_agotado()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m);

        Assert.True(p.ControlaStock);
        Assert.Equal(3, p.StockMinimo);
        var fila = (await m.Stock.ListarAsync(new(), default)).Items.Single();
        Assert.Equal(0, fila.Stock);
        Assert.Equal(EstadoStock.Agotado, fila.Estado);
    }

    [Fact]
    public async Task Se_puede_pedir_un_solo_producto_aunque_este_archivado_o_sin_control_y_uno_inexistente_da_404()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var conControl = await Crear(m, "Con control");
        var sinControl = await Crear(m, "Sin control", controla: false);
        await m.Stock.IngresarAsync(Llegaron(conControl, 3), default);
        await m.Productos.ArchivarAsync(conControl.Id, true, default);

        var archivado = await m.Stock.ObtenerAsync(conControl.Id, default);
        Assert.Equal(3, archivado.Stock);
        Assert.Equal(EstadoStock.Pocos, archivado.Estado);
        Assert.Equal(EstadoStock.SinControl, (await m.Stock.ObtenerAsync(sinControl.Id, default)).Estado);
        await Assert.ThrowsAsync<ProductoNoEncontradoException>(() => m.Stock.ObtenerAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Llegaron_mas_suma_por_variante_y_el_total_es_la_suma()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m, colores: ["café", "negro"]);

        var fila = await m.Stock.IngresarAsync(Llegaron(p, 4, 2) with { Nota = "  Pedido de octubre " }, default);

        Assert.Equal(6, fila.Stock);
        Assert.Equal([4, 2], fila.Variantes.Select(v => v.Stock));
        Assert.Equal(EstadoStock.Ok, fila.Estado);

        var fila2 = await m.Stock.IngresarAsync(Llegaron(p, 0, 5), default); // solo la segunda
        Assert.Equal([4, 7], fila2.Variantes.Select(v => v.Stock));
        Assert.Equal(11, fila2.Stock);

        var historial = (await m.Stock.MovimientosAsync(p.Id, new(), default)).Items;
        Assert.Equal(3, historial.Count); // el 0 no inventó un movimiento
        Assert.Equal([TipoMovimiento.Ingreso, TipoMovimiento.Ingreso, TipoMovimiento.Ingreso], historial.Select(h => h.Tipo));
        Assert.Equal(7, historial[0].Saldo); // el más nuevo: negro quedó en 7
        Assert.Equal("negro", historial[0].Color);
        Assert.Equal("Pedido de octubre", historial.Single(h => h.Saldo == 4).Nota);
    }

    [Fact]
    public async Task Llegaron_mas_con_costo_actualiza_el_costo_del_producto_y_sin_costo_no_lo_toca()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m, costo: 110);

        await m.Stock.IngresarAsync(Llegaron(p, 3), default);
        Assert.Equal(110, (await m.Productos.ObtenerAsync(p.Id, default)).Costo);

        await m.Stock.IngresarAsync(Llegaron(p, 3) with { CostoUnitario = 95.50m }, default);
        var tras = await m.Productos.ObtenerAsync(p.Id, default);
        Assert.Equal(95.50m, tras.Costo);
        Assert.Equal(84.5m, tras.Ganancia); // la ganancia se recalcula sola con el costo nuevo
        Assert.Equal(95.50m, (await m.Stock.MovimientosAsync(p.Id, new(), default)).Items[0].CostoUnitario);

        await m.Stock.IngresarAsync(Llegaron(p, 1) with { CostoUnitario = 0 }, default); // 0 = "no lo sé"
        Assert.Equal(95.50m, (await m.Productos.ObtenerAsync(p.Id, default)).Costo);
    }

    [Fact]
    public async Task Llegaron_mas_rechaza_cantidades_y_costos_invalidos_sin_guardar_nada()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m);

        await Assert.ThrowsAsync<ValidacionException>(() => m.Stock.IngresarAsync(Llegaron(p, 0), default));     // nada
        await Assert.ThrowsAsync<ValidacionException>(() => m.Stock.IngresarAsync(Llegaron(p, -3), default));    // negativo
        await Assert.ThrowsAsync<ValidacionException>(() => m.Stock.IngresarAsync(Llegaron(p, 1) with { CostoUnitario = 10.555m }, default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Stock.IngresarAsync(Llegaron(p, 1) with { CostoUnitario = -5 }, default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Stock.IngresarAsync(Llegaron(p, 1_000_001), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Stock.IngresarAsync(Llegaron(p, 1) with { Nota = new string('x', 201) }, default));

        Assert.Equal(0, await db.Movimientos.CountAsync());
    }

    [Fact]
    public async Task No_se_puede_mover_el_stock_de_una_variante_ajena()
    {
        Guid varianteDeB, productoDeB;
        await using (var dbB = _bd.Contexto(_bd.TiendaB))
        {
            var b = Para(dbB, _bd.TiendaB);
            var pb = await Crear(b);
            varianteDeB = pb.Variantes[0].Id;
            productoDeB = pb.Id;
        }

        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var propio = await Crear(m);
        var otroPropio = await Crear(m, "Otro producto mío");

        // Variante de otra tienda, aunque se la meta en un producto propio.
        await Assert.ThrowsAsync<VarianteNoEncontradaException>(() => m.Stock.IngresarAsync(new(propio.Id, [new(varianteDeB, 5)]), default));
        // Variante propia pero de OTRO producto: tampoco.
        await Assert.ThrowsAsync<VarianteNoEncontradaException>(() => m.Stock.IngresarAsync(new(propio.Id, [new(otroPropio.Variantes[0].Id, 5)]), default));
        // Producto de otra tienda.
        await Assert.ThrowsAsync<ProductoNoEncontradoException>(() => m.Stock.IngresarAsync(new(productoDeB, [new(varianteDeB, 5)]), default));
        Assert.Equal(0, await db.Movimientos.CountAsync());
    }

    // ───────────── Corregir (contar) ─────────────

    [Fact]
    public async Task Corregir_deja_el_total_que_se_conto_y_guarda_la_diferencia_con_su_motivo()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m);
        await m.Stock.IngresarAsync(Llegaron(p, 10), default);

        var tras = await m.Stock.AjustarAsync(Contar(p, "  Dañada  ", 7), default);

        Assert.Equal(7, tras.Stock);
        var ultimo = (await m.Stock.MovimientosAsync(p.Id, new(), default)).Items[0];
        Assert.Equal((TipoMovimiento.Ajuste, -3, 7, "Dañada"), (ultimo.Tipo, ultimo.Cantidad, ultimo.Saldo, ultimo.Nota));

        var arriba = await m.Stock.AjustarAsync(Contar(p, null, 12), default); // sin motivo: el de siempre
        Assert.Equal(12, arriba.Stock);
        Assert.Equal("Corrección de stock", (await m.Stock.MovimientosAsync(p.Id, new(), default)).Items[0].Nota);

        var enCero = await m.Stock.AjustarAsync(Contar(p, "Se vendió todo", 0), default);
        Assert.Equal(EstadoStock.Agotado, enCero.Estado);
    }

    [Fact]
    public async Task Corregir_a_la_misma_cantidad_no_inventa_movimientos_y_los_totales_invalidos_se_rechazan()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m);
        await m.Stock.IngresarAsync(Llegaron(p, 5), default);

        var ex = await Assert.ThrowsAsync<ValidacionException>(() => m.Stock.AjustarAsync(Contar(p, null, 5), default));
        Assert.Contains("No cambió", ex.Message);
        await Assert.ThrowsAsync<ValidacionException>(() => m.Stock.AjustarAsync(Contar(p, null, -1), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Stock.AjustarAsync(Contar(p, new string('x', 201), 3), default));
        Assert.Equal(1, await db.Movimientos.CountAsync());
    }

    [Fact]
    public async Task El_saldo_de_cada_movimiento_coincide_siempre_con_la_suma_hasta_ese_punto()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m);
        await m.Stock.IngresarAsync(Llegaron(p, 10), default);
        await m.Stock.AjustarAsync(Contar(p, null, 4), default);
        await Operar(m, () => m.Operaciones.AplicarAsync(TipoMovimiento.Venta, [new(p.Variantes[0].Id, -3)], null, null, false, default));
        await m.Stock.IngresarAsync(Llegaron(p, 20), default);

        var cronologico = (await m.Stock.MovimientosAsync(p.Id, new(), default)).Items.Reverse().ToList();
        var corrido = 0;
        foreach (var mov in cronologico)
        {
            corrido += mov.Cantidad;
            Assert.Equal(corrido, mov.Saldo);
        }
        Assert.Equal(21, corrido);
        Assert.Equal(21, (await m.Stock.ListarAsync(new(), default)).Items.Single().Stock);
    }

    // ───────────── Ventas y devoluciones (las usa M6) ─────────────

    [Fact]
    public async Task Una_venta_resta_y_una_devolucion_suma()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m);
        await m.Stock.IngresarAsync(Llegaron(p, 5), default);
        var v = p.Variantes[0].Id;

        await Operar(m, () => m.Operaciones.AplicarAsync(TipoMovimiento.Venta, [new(v, -2)], "Venta", null, false, default));
        Assert.Equal(3, (await m.Stock.ListarAsync(new(), default)).Items.Single().Stock);

        await Operar(m, () => m.Operaciones.AplicarAsync(TipoMovimiento.Devolucion, [new(v, 2)], "Venta anulada", null, false, default));
        Assert.Equal(5, (await m.Stock.ListarAsync(new(), default)).Items.Single().Stock);
    }

    [Fact]
    public async Task Vender_mas_de_lo_que_hay_falla_con_el_detalle_y_no_guarda_nada()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m, colores: ["café", "negro"]);
        await m.Stock.IngresarAsync(Llegaron(p, 3, 1), default);

        // Dos líneas en la misma venta: la segunda no alcanza → no se descuenta NINGUNA (todo o nada).
        var cafe = p.Variantes[0].Id;
        var negro = p.Variantes[1].Id;
        var ex = await Assert.ThrowsAsync<StockInsuficienteException>(() =>
            Operar(m, () => m.Operaciones.AplicarAsync(TipoMovimiento.Venta, [new(cafe, -2), new(negro, -5)], null, null, false, default)));
        Assert.Contains("hay 1 y se necesitan 5", ex.Message);
        Assert.Contains("negro", ex.Message);

        await using var limpio = _bd.Contexto(_bd.TiendaA);
        var fila = (await Para(limpio, _bd.TiendaA).Stock.ListarAsync(new(), default)).Items.Single();
        Assert.Equal([3, 1], fila.Variantes.Select(v => v.Stock)); // intacto
        Assert.Equal(2, await limpio.Movimientos.CountAsync());   // solo las dos llegadas
    }

    [Fact]
    public async Task Vender_exactamente_lo_que_hay_deja_el_producto_agotado_y_una_unidad_mas_ya_no_pasa()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m);
        await m.Stock.IngresarAsync(Llegaron(p, 2), default);
        var v = p.Variantes[0].Id;

        await Operar(m, () => m.Operaciones.AplicarAsync(TipoMovimiento.Venta, [new(v, -2)], null, null, false, default));
        Assert.Equal(EstadoStock.Agotado, (await m.Stock.ListarAsync(new(), default)).Items.Single().Estado);

        await using var otro = _bd.Contexto(_bd.TiendaA);
        var m2 = Para(otro, _bd.TiendaA);
        await Assert.ThrowsAsync<StockInsuficienteException>(() =>
            Operar(m2, () => m2.Operaciones.AplicarAsync(TipoMovimiento.Venta, [new(v, -1)], null, null, false, default)));
    }

    [Fact]
    public async Task Una_variante_repetida_en_la_misma_operacion_se_junta_en_una_sola()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m);
        await m.Stock.IngresarAsync(Llegaron(p, 5), default);
        var v = p.Variantes[0].Id;

        await Operar(m, () => m.Operaciones.AplicarAsync(TipoMovimiento.Venta, [new(v, -2), new(v, -2)], null, null, false, default));

        Assert.Equal(1, (await m.Stock.ListarAsync(new(), default)).Items.Single().Stock);
        Assert.Equal(-4, (await m.Stock.MovimientosAsync(p.Id, new(), default)).Items[0].Cantidad); // un solo movimiento
    }

    [Fact]
    public async Task Las_operaciones_exigen_una_transaccion_y_el_signo_correcto()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m);
        var v = p.Variantes[0].Id;

        // Sin transacción el candado no sirve de nada: se rechaza en vez de funcionar "a veces".
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            m.Operaciones.AplicarAsync(TipoMovimiento.Ingreso, [new(v, 1)], null, null, false, default));

        await using var tx = await db.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<ValidacionException>(() => m.Operaciones.AplicarAsync(TipoMovimiento.Venta, [new(v, 3)], null, null, false, default));      // una venta suma
        await Assert.ThrowsAsync<ValidacionException>(() => m.Operaciones.AplicarAsync(TipoMovimiento.Ingreso, [new(v, -3)], null, null, false, default));    // un ingreso resta
        await Assert.ThrowsAsync<ValidacionException>(() => m.Operaciones.AplicarAsync(TipoMovimiento.Venta, [new(v, 0)], null, null, false, default));       // nada
        await Assert.ThrowsAsync<ValidacionException>(() => m.Operaciones.AplicarAsync(TipoMovimiento.Venta, [], null, null, false, default));
    }

    // ───────────── Sin control de stock ("por encargo") ─────────────

    [Fact]
    public async Task Un_producto_sin_control_no_tiene_stock_ni_acepta_movimientos()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m, "Bolso con nombre bordado", controla: false);

        var fila = (await m.Stock.ListarAsync(new(), default)).Items.Single();
        Assert.Null(fila.Stock);
        Assert.Equal(EstadoStock.SinControl, fila.Estado);
        await Assert.ThrowsAsync<ProductoSinControlDeStockException>(() => m.Stock.IngresarAsync(Llegaron(p, 5), default));
        await Assert.ThrowsAsync<ProductoSinControlDeStockException>(() => m.Stock.AjustarAsync(Contar(p, null, 5), default));
        Assert.Equal(0, await db.Movimientos.CountAsync());

        var catalogo = (await m.Productos.ListarAsync(new(), default)).Items.Single();
        Assert.Null(catalogo.Stock);
        Assert.Equal(EstadoStock.SinControl, catalogo.EstadoStock);
    }

    [Fact]
    public async Task Una_venta_mixta_salta_los_productos_sin_control_y_descuenta_los_otros()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var conControl = await Crear(m, "Con control");
        var porEncargo = await Crear(m, "Por encargo", controla: false);
        await m.Stock.IngresarAsync(Llegaron(conControl, 5), default);

        await Operar(m, () => m.Operaciones.AplicarAsync(TipoMovimiento.Venta,
            [new(conControl.Variantes[0].Id, -1), new(porEncargo.Variantes[0].Id, -1)], null, null, ignorarSinControl: true, default));

        Assert.Equal(4, (await m.Stock.ListarAsync(new(Buscar: "con control"), default)).Items.Single().Stock);
        Assert.Equal(1 + 1, await db.Movimientos.CountAsync()); // la llegada + la venta; nada del por encargo
    }

    [Fact]
    public async Task Apagar_el_control_conserva_el_historial_y_encenderlo_de_nuevo_retoma_la_suma()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m);
        await m.Stock.IngresarAsync(Llegaron(p, 8), default);

        await m.Productos.ActualizarAsync(p.Id, new GuardarProductoDto(p.Nombre, null, p.Precio, p.Costo, null, true, null, null, null, ControlaStock: false), default);
        Assert.Null((await m.Stock.ListarAsync(new(), default)).Items.Single().Stock);
        Assert.Single((await m.Stock.MovimientosAsync(p.Id, new(), default)).Items); // el historial sigue

        await m.Productos.ActualizarAsync(p.Id, new GuardarProductoDto(p.Nombre, null, p.Precio, p.Costo, null, true, null, null, null, ControlaStock: true), default);
        Assert.Equal(8, (await m.Stock.ListarAsync(new(), default)).Items.Single().Stock);
    }

    [Fact]
    public async Task El_minimo_para_avisar_se_puede_cambiar_y_tiene_limites()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m, minimo: 10);
        await m.Stock.IngresarAsync(Llegaron(p, 8), default);

        Assert.Equal(EstadoStock.Pocos, (await m.Stock.ListarAsync(new(), default)).Items.Single().Estado); // 8 ≤ 10
        await Assert.ThrowsAsync<ValidacionException>(() => Crear(m, "Otro", minimo: -1));
        await Assert.ThrowsAsync<ValidacionException>(() => Crear(m, "Otro", minimo: 100_001));
    }

    // ───────────── Variantes con stock ─────────────

    [Fact]
    public async Task Sacar_una_variante_con_stock_se_rechaza_y_no_guarda_nada()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m, colores: ["café", "negro"]);
        await m.Stock.IngresarAsync(Llegaron(p, 4, 0), default);

        GuardarProductoDto SoloNegro(string nombre) => new(nombre, null, 180, 110, null, true, ["negro"], null, null);
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => m.Productos.ActualizarAsync(p.Id, SoloNegro("Renombrada"), default));
        Assert.Contains("quedan 4 de café", ex.Message, StringComparison.OrdinalIgnoreCase);

        await using var limpio = _bd.Contexto(_bd.TiendaA);
        var intacto = await Para(limpio, _bd.TiendaA).Productos.ObtenerAsync(p.Id, default);
        Assert.Equal("Cartera de cuero Mía", intacto.Nombre);            // ni el nombre cambió
        Assert.Equal(["café", "negro"], intacto.Variantes.Select(v => v.Color));

        // Se corrige a 0 y ahora sí se puede.
        var m2 = Para(limpio, _bd.TiendaA);
        await m2.Stock.AjustarAsync(Contar(p, "Se la llevó un cliente", 0, 0), default);
        var sinCafe = await m2.Productos.ActualizarAsync(p.Id, SoloNegro("Cartera de cuero Mía"), default);
        Assert.Equal(["negro"], sinCafe.Variantes.Select(v => v.Color));
    }

    [Fact]
    public async Task Sacar_una_variante_con_stock_si_el_producto_no_lleva_control_se_permite()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m, colores: ["café", "negro"]);
        await m.Stock.IngresarAsync(Llegaron(p, 4, 4), default);

        var sinControl = await m.Productos.ActualizarAsync(p.Id,
            new GuardarProductoDto(p.Nombre, null, 180, 110, null, true, ["negro"], null, null, ControlaStock: false), default);

        Assert.Equal(["negro"], sinControl.Variantes.Select(v => v.Color));
    }

    // ───────────── Listado ─────────────

    private async Task<Dictionary<string, ProductoDto>> CincoEstados(Mundo m)
    {
        var d = new Dictionary<string, ProductoDto>();
        foreach (var (nombre, controla, llegan) in new[]
                 { ("agotado-1", true, 0), ("bien", true, 10), ("por-encargo", false, 0), ("pocos", true, 2), ("agotado-2", true, 0) })
        {
            var p = await Crear(m, nombre, controla: controla);
            if (llegan > 0) await m.Stock.IngresarAsync(Llegaron(p, llegan), default);
            d[nombre] = p;
        }
        return d;
    }

    [Fact]
    public async Task El_listado_va_de_lo_mas_urgente_a_lo_menos_y_las_vistas_filtran()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        await CincoEstados(m);

        var todos = (await m.Stock.ListarAsync(new(), default)).Items;
        Assert.Equal([EstadoStock.Agotado, EstadoStock.Agotado, EstadoStock.Pocos, EstadoStock.Ok, EstadoStock.SinControl], todos.Select(i => i.Estado));
        // Dentro de cada grupo, el más nuevo primero.
        Assert.Equal(["agotado-2", "agotado-1"], todos.Take(2).Select(i => i.Nombre));

        Assert.Equal(["agotado-2", "agotado-1", "pocos"], (await m.Stock.ListarAsync(new(Vista: VistaStock.AReponer), default)).Items.Select(i => i.Nombre));
        Assert.Equal(["por-encargo"], (await m.Stock.ListarAsync(new(Vista: VistaStock.PorEncargo), default)).Items.Select(i => i.Nombre));
        Assert.Equal(["bien"], (await m.Stock.ListarAsync(new(Buscar: " BIE "), default)).Items.Select(i => i.Nombre));
    }

    [Fact]
    public async Task El_orden_en_SQL_coincide_con_la_regla_del_dominio()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        await CincoEstados(m);

        var prioridades = (await m.Stock.ListarAsync(new(), default)).Items.Select(i => ReglaStock.Prioridad(i.Estado)).ToList();

        Assert.Equal(prioridades.Order(), prioridades); // nunca baja: SQL ordenó igual que la regla en C#
        // Y el filtro "A reponer" trae exactamente los que la regla marca como Agotado o Pocos.
        var reponer = (await m.Stock.ListarAsync(new(Vista: VistaStock.AReponer), default)).Items;
        Assert.All(reponer, i => Assert.True(i.Estado is EstadoStock.Agotado or EstadoStock.Pocos));
        Assert.Equal(3, reponer.Count);
    }

    [Fact]
    public async Task La_paginacion_por_cursor_recorre_todo_una_vez_y_en_el_mismo_orden()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        await CincoEstados(m);
        for (var i = 1; i <= 3; i++) await Crear(m, $"extra-{i}"); // 3 agotados más: 8 en total

        var esperado = (await m.Stock.ListarAsync(new(), default)).Items.Select(i => i.Id).ToList();
        var vistos = new List<Guid>();
        string? cursor = null;
        var paginas = 0;
        do
        {
            var pagina = await m.Stock.ListarAsync(new(Limite: 3, Cursor: cursor), default);
            vistos.AddRange(pagina.Items.Select(i => i.Id));
            cursor = pagina.Siguiente;
            paginas++;
        } while (cursor is not null);

        Assert.Equal(3, paginas); // 3 + 3 + 2
        Assert.Equal(esperado, vistos);
        await Assert.ThrowsAsync<ValidacionException>(() => m.Stock.ListarAsync(new(Cursor: "esto no es un cursor"), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Stock.ListarAsync(new(Cursor: "9." + Guid.NewGuid()), default));
    }

    [Fact]
    public async Task Los_archivados_no_aparecen_en_el_stock()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m);
        await m.Productos.ArchivarAsync(p.Id, true, default);

        Assert.Empty((await m.Stock.ListarAsync(new(), default)).Items);
        Assert.Equal(0, (await m.Stock.ResumenAsync(default)).Agotados);
    }

    [Fact]
    public async Task Solo_cuentan_las_variantes_activas()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m, colores: ["café", "negro"]);
        await m.Stock.IngresarAsync(Llegaron(p, 4, 6), default);
        await m.Stock.AjustarAsync(Contar(p, null, 0, 6), default); // café a 0 para poder sacarlo
        var conUna = await m.Productos.ActualizarAsync(p.Id,
            new GuardarProductoDto(p.Nombre, null, 180, 110, null, true, ["negro"], null, null), default);

        var fila = (await m.Stock.ListarAsync(new(), default)).Items.Single();

        Assert.Equal(6, fila.Stock);
        Assert.Single(fila.Variantes);
        Assert.Equal(6, (await m.Productos.ListarAsync(new(), default)).Items.Single().Stock);
        Assert.Equal(conUna.Variantes[0].Id, fila.Variantes[0].Id);
    }

    // ───────────── Resumen ─────────────

    [Fact]
    public async Task El_resumen_valora_la_mercaderia_y_cuenta_los_avisos_solo_con_productos_con_control()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var a = await Crear(m, "A", precio: 180, costo: 110);       // 10 u → costo 1100 / venta 1800
        var b = await Crear(m, "B", precio: 60, costo: 62);         // 2 u (pocos) → 124 / 120
        await Crear(m, "C", precio: 50, costo: 20);                 // agotado: no suma valor
        await Crear(m, "D", controla: false, precio: 999, costo: 1); // por encargo: no cuenta
        var archivado = await Crear(m, "E", precio: 100, costo: 50);
        await m.Stock.IngresarAsync(Llegaron(a, 10), default);
        await m.Stock.IngresarAsync(Llegaron(b, 2), default);
        await m.Stock.IngresarAsync(Llegaron(archivado, 50), default);
        await m.Productos.ArchivarAsync(archivado.Id, true, default);

        var r = await m.Stock.ResumenAsync(default);

        Assert.Equal(1100 + 124, r.ValorCosto);
        Assert.Equal(1800 + 120, r.ValorVenta);
        Assert.Equal((1, 1, 1), (r.Agotados, r.PorAgotarse, r.SinControl));
    }

    // ───────────── Historial ─────────────

    [Fact]
    public async Task El_historial_se_pagina_del_mas_nuevo_al_mas_viejo()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Crear(m);
        for (var i = 1; i <= 5; i++) await m.Stock.IngresarAsync(Llegaron(p, i), default);

        var primera = await m.Stock.MovimientosAsync(p.Id, new(Limite: 2), default);
        var segunda = await m.Stock.MovimientosAsync(p.Id, new(Limite: 2, Cursor: primera.Siguiente), default);
        var tercera = await m.Stock.MovimientosAsync(p.Id, new(Limite: 2, Cursor: segunda.Siguiente), default);

        Assert.Equal([5, 4], primera.Items.Select(i => i.Cantidad));
        Assert.Equal([3, 2], segunda.Items.Select(i => i.Cantidad));
        Assert.Equal([1], tercera.Items.Select(i => i.Cantidad));
        Assert.Null(tercera.Siguiente);
    }

    // ───────────── Aislamiento entre tiendas ─────────────

    [Fact]
    public async Task Una_tienda_no_ve_ni_toca_el_stock_de_otra()
    {
        ProductoDto deA;
        await using (var dbA = _bd.Contexto(_bd.TiendaA))
        {
            var a = Para(dbA, _bd.TiendaA);
            deA = await Crear(a);
            await a.Stock.IngresarAsync(Llegaron(deA, 7), default);
        }

        await using var dbB = _bd.Contexto(_bd.TiendaB);
        var b = Para(dbB, _bd.TiendaB);
        Assert.Empty((await b.Stock.ListarAsync(new(), default)).Items);
        var resumen = await b.Stock.ResumenAsync(default);
        Assert.Equal((0m, 0m, 0, 0, 0), (resumen.ValorCosto, resumen.ValorVenta, resumen.Agotados, resumen.PorAgotarse, resumen.SinControl));
        await Assert.ThrowsAsync<ProductoNoEncontradoException>(() => b.Stock.ObtenerAsync(deA.Id, default));
        await Assert.ThrowsAsync<ProductoNoEncontradoException>(() => b.Stock.MovimientosAsync(deA.Id, new(), default));
        await Assert.ThrowsAsync<ProductoNoEncontradoException>(() => b.Stock.IngresarAsync(Llegaron(deA, 1), default));
        await Assert.ThrowsAsync<ProductoNoEncontradoException>(() => b.Stock.AjustarAsync(Contar(deA, null, 0), default));

        await using var verificar = _bd.Contexto(_bd.TiendaA);
        Assert.Equal(7, (await Para(verificar, _bd.TiendaA).Stock.ListarAsync(new(), default)).Items.Single().Stock);
    }

    [Fact]
    public async Task Sin_tienda_en_sesion_no_se_ve_ni_se_mueve_nada()
    {
        await using var db = _bd.Contexto(null);
        var m = Para(db, null);

        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => m.Stock.IngresarAsync(new(Guid.NewGuid(), [new(Guid.NewGuid(), 1)]), default));
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => m.Stock.AjustarAsync(new(Guid.NewGuid(), [new(Guid.NewGuid(), 1)]), default));
    }

    // ───────────── Concurrencia (solo con Postgres real) ─────────────

    // SQLite no tiene candados de fila, así que esto solo corre si se pasa una conexión a Postgres en la
    // variable HERMES_TEST_PG (por ejemplo "Host=localhost;Username=postgres;Password=..."): sin ella, sale
    // sin probar nada. Crea y borra su propia base. Es la prueba que justifica el candado de la variante:
    // sin él, "leer el stock y después insertar" deja pasar todas las ventas de la última unidad.
    [Fact]
    public async Task Ventas_simultaneas_de_la_ultima_unidad_solo_dejan_pasar_las_que_hay_con_Postgres_real()
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
            Guid tienda, variante;
            await using (var db = Nuevo(null))
            {
                await db.Database.EnsureCreatedAsync();
                var dueno = new Hermes.Domain.Entities.Usuario { Nombre = "D", Email = "d@x.test", PasswordHash = "x" };
                var t = new Hermes.Domain.Entities.Tienda { DuenoId = dueno.Id, Slug = "tienda-x", Nombre = "X" };
                db.AddRange(dueno, t);
                await db.SaveChangesAsync();
                tienda = t.Id;
            }
            await using (var db = Nuevo(tienda))
            {
                var m = Para(db, tienda);
                var p = await Crear(m);
                variante = p.Variantes[0].Id;
                await m.Stock.IngresarAsync(Llegaron(p, 5), default);
            }

            // 12 ventas de 1 unidad al mismo tiempo, cada una con su propia conexión y transacción.
            var resultados = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
            {
                await using var db = Nuevo(tienda);
                var m = Para(db, tienda);
                try
                {
                    await Operar(m, () => m.Operaciones.AplicarAsync(TipoMovimiento.Venta, [new(variante, -1)], null, null, false, default));
                    return true;
                }
                catch (StockInsuficienteException) { return false; }
            }));

            Assert.Equal(5, resultados.Count(r => r));
            Assert.Equal(7, resultados.Count(r => !r));
            await using var lectura = Nuevo(tienda);
            Assert.Equal(0, (await Para(lectura, tienda).Stock.ListarAsync(new(), default)).Items.Single().Stock);
            var saldos = await lectura.Movimientos.OrderBy(x => x.Id).Select(x => x.Saldo).ToListAsync();
            Assert.All(saldos, s => Assert.True(s >= 0));
            Assert.Equal(6, saldos.Count); // 1 llegada + 5 ventas
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
}
