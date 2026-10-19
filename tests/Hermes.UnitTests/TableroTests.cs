using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Domain.Entities;
using Hermes.Domain.Enums;
using Hermes.Infrastructure.Imagenes;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Services;
using Hermes.Infrastructure.Stock;
using Microsoft.Extensions.Time.Testing;

namespace Hermes.UnitTests;

// Lo que le agregamos al Inicio para el tablero: ventas por día (con la hora de quien mira), lo más vendido, la bandeja de
// Instagram separada en listos y sin precio, y las unidades de la tienda web.
public class TableroTests : IDisposable
{
    private readonly BaseDePrueba _bd = new();
    private readonly FakeTimeProvider _reloj = new(new DateTimeOffset(2026, 10, 14, 15, 0, 0, TimeSpan.Zero));

    public void Dispose() => _bd.Dispose();

    private record Lado(HermesDbContext Db, ProductoService Productos, StockService Stock, VentaService Ventas, PedidoService Pedidos, InstagramService Bandeja, InicioService Inicio);

    private Lado Para(Guid? tienda)
    {
        var db = _bd.Contexto(tienda);
        var almacen = new AlmacenEnBaseDeDatos(db, _reloj);
        var sesion = new BaseDePrueba.TiendaFija(tienda);
        var imagenes = new ImagenService(db, almacen, sesion, _reloj);
        var operaciones = new OperacionesStock(db, _reloj);
        var stock = new StockService(db, operaciones, almacen, sesion);
        var ventas = new VentaService(db, operaciones, almacen, sesion, _reloj);
        var productos = new ProductoService(db, imagenes, almacen, sesion, _reloj);
        var pedidos = new PedidoService(db, ventas, sesion, _reloj);
        var bandeja = new InstagramService(db, productos, stock, sesion, _reloj);
        return new Lado(db, productos, stock, ventas, pedidos, bandeja,
            new InicioService(ventas, new CompraService(db, operaciones, sesion, _reloj), stock, new GastoService(db, sesion, _reloj), pedidos, bandeja));
    }

    private static async Task<ProductoDto> Producto(Lado l, string nombre, decimal precio = 100, decimal costo = 40, int stock = 50)
    {
        var p = await l.Productos.CrearAsync(new GuardarProductoDto(nombre, null, precio, costo, null, true, null, null, null), default);
        if (stock > 0) await l.Stock.IngresarAsync(new(p.Id, [new(p.Variantes[0].Id, stock)]), default);
        return p;
    }

    private Task<ResultadoVenta> Vende(Lado l, ProductoDto p, int cantidad)
        => l.Ventas.RegistrarAsync(new(Guid.NewGuid(), [new(p.Variantes[0].Id, cantidad)], MetodoPago.Efectivo), default);

    // ───────────── Ventas por día ─────────────

    [Fact]
    public async Task Por_dia_devuelve_todos_los_dias_pedidos_con_cero_donde_no_se_vendio()
    {
        var l = Para(_bd.TiendaA);
        var p = await Producto(l, "Cartera", precio: 100);
        await Vende(l, p, 2);                                 // 14/10 15:00 UTC
        _reloj.Advance(TimeSpan.FromDays(2));
        await Vende(l, p, 1);                                 // 16/10

        var dias = await l.Ventas.PorDiaAsync(new(new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero), 5), default);

        Assert.Equal(5, dias.Count);
        Assert.Equal([new DateOnly(2026, 10, 12), new(2026, 10, 13), new(2026, 10, 14), new(2026, 10, 15), new(2026, 10, 16)], dias.Select(d => d.Fecha));
        Assert.Equal([0m, 0m, 200m, 0m, 100m], dias.Select(d => d.Total));
        Assert.Equal([0, 0, 1, 0, 1], dias.Select(d => d.Ventas));
    }

    [Fact]
    public async Task Por_dia_usa_la_hora_de_quien_mira_no_la_del_servidor()
    {
        var l = Para(_bd.TiendaA);
        var p = await Producto(l, "Cartera", precio: 100);
        // 15/10 a las 02:00 UTC = 14/10 a las 22:00 en Bolivia (UTC-4): para quien mira, la venta es del día 14.
        _reloj.Advance(TimeSpan.FromHours(11));
        await Vende(l, p, 1);

        // Bolivia: getTimezoneOffset() = 240 y la medianoche local del 14/10 es 04:00 UTC.
        var dias = await l.Ventas.PorDiaAsync(new(new DateTimeOffset(2026, 10, 14, 4, 0, 0, TimeSpan.Zero), 2, 240), default);

        Assert.Equal([new DateOnly(2026, 10, 14), new(2026, 10, 15)], dias.Select(d => d.Fecha));
        Assert.Equal([100m, 0m], dias.Select(d => d.Total)); // con la hora del servidor (UTC) habría caído en el 15
    }

    [Fact]
    public async Task Por_dia_no_cuenta_las_anuladas_ni_las_de_otra_tienda_y_valida_los_limites()
    {
        var l = Para(_bd.TiendaA);
        var p = await Producto(l, "Cartera", precio: 100);
        var anulada = (await Vende(l, p, 3)).Venta;
        await l.Ventas.AnularAsync(anulada.Id, default);
        var otra = Para(_bd.TiendaB);
        await Vende(otra, await Producto(otra, "Ajena", precio: 999), 1);

        var dias = await l.Ventas.PorDiaAsync(new(new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero), 1), default);

        Assert.Equal(0m, dias.Single().Total);
        await Assert.ThrowsAsync<ValidacionException>(() => l.Ventas.PorDiaAsync(new(_reloj.GetUtcNow(), 0), default));
        await Assert.ThrowsAsync<ValidacionException>(() => l.Ventas.PorDiaAsync(new(_reloj.GetUtcNow(), 61), default));
        await Assert.ThrowsAsync<ValidacionException>(() => l.Ventas.PorDiaAsync(new(_reloj.GetUtcNow(), 7, 2000), default));
    }

    // ───────────── Lo que más se vende ─────────────

    [Fact]
    public async Task Lo_mas_vendido_ordena_por_unidades_con_la_ganancia_y_respeta_el_periodo()
    {
        var l = Para(_bd.TiendaA);
        var aretes = await Producto(l, "Aretes", precio: 50, costo: 20);
        var cartera = await Producto(l, "Cartera", precio: 180, costo: 110);
        var sinCosto = await Producto(l, "Collar", precio: 90, costo: 0);
        await Vende(l, aretes, 5);
        await Vende(l, cartera, 2);
        await Vende(l, sinCosto, 3);
        _reloj.Advance(TimeSpan.FromDays(40));
        await Vende(l, cartera, 20); // fuera del período pedido

        var top = await l.Ventas.MasVendidosAsync(new(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)), default);

        Assert.Equal([("Aretes", 5, (decimal?)150), ("Collar", 3, null), ("Cartera", 2, 140)], top.Select(t => (t.Nombre, t.Unidades, t.Ganancia)));
        Assert.Single(await l.Ventas.MasVendidosAsync(new(Limite: 1), default));
        Assert.Equal(22, (await l.Ventas.MasVendidosAsync(new(), default)).Single(t => t.Nombre == "Cartera").Unidades);
    }

    [Fact]
    public async Task Lo_mas_vendido_muestra_el_nombre_de_hoy_aunque_se_haya_renombrado()
    {
        var l = Para(_bd.TiendaA);
        var p = await Producto(l, "Cartera vieja");
        await Vende(l, p, 1);
        await l.Productos.ActualizarAsync(p.Id, new GuardarProductoDto("Cartera nueva", null, 100, 40, null, true, null, null, null, true), default);

        Assert.Equal("Cartera nueva", (await l.Ventas.MasVendidosAsync(new(), default)).Single().Nombre);
    }

    // ───────────── El Inicio ─────────────

    [Fact]
    public async Task El_inicio_separa_la_bandeja_en_listos_y_sin_precio_y_cuenta_las_unidades_de_la_tienda_web()
    {
        var l = Para(_bd.TiendaA);
        await l.Bandeja.ImportarAsync(new("Cartera Aurora\nBs 150"), default);
        await l.Bandeja.ImportarAsync(new("Mochila escolar\nHecha a mano"), default);
        await l.Bandeja.ImportarAsync(new("Aretes Luna\nBs 90"), default);
        var p = await Producto(l, "Collar", precio: 90, stock: 10);
        // Un pedido de la tienda pública, entregado: sus unidades cuentan "desde la tienda web".
        l.Db.Pedidos.Add(new Pedido
        {
            Numero = 1, ClaveIdempotencia = Guid.NewGuid(), Fecha = _reloj.GetUtcNow().UtcDateTime, ClienteNombre = "Ana", ClienteCelular = "+59171234567",
            Estado = EstadoPedido.Entregado, ResueltoEn = _reloj.GetUtcNow().UtcDateTime,
            Lineas = [new PedidoLinea { ProductoId = p.Id, VarianteId = p.Variantes[0].Id, Descripcion = "Collar", Cantidad = 4, PrecioUnitario = 90 }],
        });
        await l.Db.SaveChangesAsync();
        await Vende(l, p, 1);

        var octubre = new FiltroPeriodoDto(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        var inicio = await l.Inicio.ResumenAsync(octubre, default);
        var noviembre = await l.Inicio.ResumenAsync(new(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero)), default);

        Assert.Equal((2, 1, 4), (inicio.BorradoresListos, inicio.BorradoresSinPrecio, inicio.UnidadesDeLaTienda));
        Assert.Equal((2, 1, 0), (noviembre.BorradoresListos, noviembre.BorradoresSinPrecio, noviembre.UnidadesDeLaTienda)); // la bandeja no depende del período
    }

    [Fact]
    public async Task Lo_del_tablero_es_de_cada_tienda()
    {
        var a = Para(_bd.TiendaA);
        await a.Bandeja.ImportarAsync(new("Cartera\nBs 150"), default);
        var b = Para(_bd.TiendaB);

        var deB = await b.Inicio.ResumenAsync(new(), default);

        Assert.Equal((0, 0, 0), (deB.BorradoresListos, deB.BorradoresSinPrecio, deB.UnidadesDeLaTienda));
        Assert.Empty(await b.Ventas.MasVendidosAsync(new(), default));
        var sinTienda = Para(null);
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => sinTienda.Ventas.PorDiaAsync(new(_reloj.GetUtcNow(), 3), default));
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => sinTienda.Ventas.MasVendidosAsync(new(), default));
    }
}
