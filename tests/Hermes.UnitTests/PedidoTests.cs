using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Domain.Entities;
using Hermes.Domain.Enums;
using Hermes.Infrastructure;
using Hermes.Infrastructure.Imagenes;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Services;
using Hermes.Infrastructure.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hermes.UnitTests;

public class PedidoTests : IDisposable
{
    private readonly BaseDePrueba _bd = new();
    private readonly FakeTimeProvider _reloj = new(new DateTimeOffset(2026, 10, 14, 15, 0, 0, TimeSpan.Zero));

    public PedidoTests()
    {
        Configurar(_bd.TiendaA, t => t.Whatsapp = "+59171234567");
        Configurar(_bd.TiendaB, t => t.Whatsapp = "+59176543210");
    }

    public void Dispose() => _bd.Dispose();

    private record Duena(HermesDbContext Db, ProductoService Productos, StockService Stock, PedidoService Pedidos);

    private Duena ComoDuena(Guid tienda) => ComoDuena(_bd.Contexto(tienda), tienda);

    private Duena ComoDuena(HermesDbContext db, Guid tienda)
    {
        var almacen = new AlmacenEnBaseDeDatos(db, _reloj);
        var sesion = new BaseDePrueba.TiendaFija(tienda);
        var imagenes = new ImagenService(db, almacen, sesion, _reloj);
        var operaciones = new OperacionesStock(db, _reloj);
        var ventas = new VentaService(db, operaciones, almacen, sesion, _reloj);
        return new Duena(db, new ProductoService(db, imagenes, almacen, sesion, _reloj),
            new StockService(db, operaciones, almacen, sesion), new PedidoService(db, ventas, sesion, _reloj));
    }

    private PublicoService ComoPublico()
    {
        var contexto = new TiendaPublicaContexto();
        var db = _bd.ContextoPublico(contexto);
        return new PublicoService(db, new AlmacenEnBaseDeDatos(db, _reloj), contexto, _reloj);
    }

    private void Configurar(Guid tienda, Action<Tienda> cambios)
    {
        using var db = _bd.Contexto(null);
        var t = db.Tiendas.Single(x => x.Id == tienda);
        cambios(t);
        db.SaveChanges();
    }

    private static Task<ProductoDto> Crear(Duena d, string nombre, decimal precio = 180, string[]? colores = null, bool visible = true, bool controla = true)
        => d.Productos.CrearAsync(new GuardarProductoDto(nombre, null, precio, 100, null, visible, colores, null, null, controla), default);

    private static async Task<ProductoDto> ConStock(Duena d, string nombre, int unidades, decimal precio = 180, string[]? colores = null)
    {
        var p = await Crear(d, nombre, precio, colores);
        await d.Stock.IngresarAsync(new(p.Id, p.Variantes.Select(v => new LineaIngresoDto(v.Id, unidades)).ToList()), default);
        return p;
    }

    private static NuevoPedidoDto Pedido(params (Guid Variante, int Unidades)[] articulos)
        => new(Guid.NewGuid(), articulos.Select(a => new ArticuloPedidoDto(a.Variante, a.Unidades)).ToList(), "Ana Pérez", "7123 4567", EntregaPedido.Retiro, null, null, null);

    private Task<PedidoEnviadoDto> Pedir(NuevoPedidoDto dto, string slug = "tienda-a") => ComoPublico().PedirAsync(slug, dto, default);

    // ───────────── El cliente pide ─────────────

    [Fact]
    public async Task Pedir_guarda_un_pedido_nuevo_con_foto_del_precio_y_no_toca_el_stock()
    {
        var d = ComoDuena(_bd.TiendaA);
        var cartera = await ConStock(d, "Cartera", 5, 180, ["café", "negro"]);
        var aretes = await ConStock(d, "Aretes", 3, 45.5m);

        var r = await Pedir(Pedido((cartera.Variantes[1].Id, 2), (aretes.Variantes[0].Id, 1), (cartera.Variantes[1].Id, 1)));

        Assert.Equal((1, true, 585.5m, "Tienda A", "+59171234567"), (r.Numero, r.Nuevo, r.Total, r.Tienda, r.Whatsapp));
        Assert.Equal([("Cartera (negro)", 3, (decimal?)180), ("Aretes", 1, 45.5m)], r.Lineas.Select(l => (l.Descripcion, l.Unidades, l.Precio)));
        var p = (await d.Pedidos.ListarAsync(new(), default)).Items.Single();
        Assert.Equal((EstadoPedido.Nuevo, "Ana Pérez", 4), (p.Estado, p.ClienteNombre, p.Unidades));
        Assert.Equal("+59171234567", (await d.Pedidos.ObtenerAsync(p.Id, default)).ClienteCelular); // "7123 4567" normalizado
        Assert.Equal(5, (await d.Stock.ObtenerAsync(cartera.Id, default)).Variantes[1].Stock); // pedir NO descuenta

        // Si después cambia el precio, el pedido conserva el de cuando se pidió (y el detalle muestra el de hoy).
        await d.Productos.ActualizarAsync(aretes.Id, new GuardarProductoDto("Aretes", null, 50, 100, null, true, null, null, null, true), default);
        var luego = await ComoDuena(_bd.TiendaA).Pedidos.ObtenerAsync(p.Id, default);
        Assert.Equal((45.5m, 50m), (luego.Lineas[1].PrecioUnitario, luego.Lineas[1].PrecioActual));
    }

    [Fact]
    public async Task El_celular_queda_normalizado_y_el_envio_guarda_la_direccion()
    {
        var d = ComoDuena(_bd.TiendaA);
        var p = await ConStock(d, "Cartera", 5);
        var dto = Pedido((p.Variantes[0].Id, 1)) with { Celular = "+591 712-34567", Entrega = EntregaPedido.Envio, Direccion = " Av. América 123 ", Referencia = "Portón verde", Nota = "Después de las 18" };

        await Pedir(dto);

        var guardado = await d.Db.Pedidos.SingleAsync();
        Assert.Equal(("+59171234567", EntregaPedido.Envio, "Av. América 123", "Portón verde", "Después de las 18"),
            (guardado.ClienteCelular, guardado.Entrega, guardado.Direccion, guardado.Referencia, guardado.Nota));
    }

    [Fact]
    public async Task Los_numeros_son_correlativos_por_tienda()
    {
        var a = await ConStock(ComoDuena(_bd.TiendaA), "Cartera", 9);
        var b = await ConStock(ComoDuena(_bd.TiendaB), "Mochila", 9);

        var n1 = (await Pedir(Pedido((a.Variantes[0].Id, 1)))).Numero;
        var n2 = (await Pedir(Pedido((a.Variantes[0].Id, 1)))).Numero;
        var deB = (await Pedir(Pedido((b.Variantes[0].Id, 1)), "tienda-b")).Numero;

        Assert.Equal((1, 2, 1), (n1, n2, deB));
    }

    [Fact]
    public async Task La_misma_clave_devuelve_el_mismo_pedido_sin_duplicarlo()
    {
        var p = await ConStock(ComoDuena(_bd.TiendaA), "Cartera", 9);
        var dto = Pedido((p.Variantes[0].Id, 2));

        var primero = await Pedir(dto);
        var reintento = await Pedir(dto with { Articulos = [new(p.Variantes[0].Id, 7)] }); // llega distinto: igual es el mismo

        Assert.Equal((1, false, 2), (reintento.Numero, reintento.Nuevo, reintento.Lineas.Single().Unidades));
        Assert.True(primero.Nuevo);
        Assert.Equal(1, await _bd.Contexto(_bd.TiendaA).Pedidos.CountAsync());
    }

    [Fact]
    public async Task Los_pedidos_invalidos_se_rechazan_sin_guardar_nada()
    {
        var p = await ConStock(ComoDuena(_bd.TiendaA), "Cartera", 9);
        var ok = Pedido((p.Variantes[0].Id, 1));

        await Assert.ThrowsAsync<ValidacionException>(() => Pedir(ok with { ClaveIdempotencia = Guid.Empty }));
        await Assert.ThrowsAsync<ValidacionException>(() => Pedir(ok with { Articulos = [] }));
        await Assert.ThrowsAsync<ValidacionException>(() => Pedir(ok with { Articulos = [new(p.Variantes[0].Id, 0)] }));
        await Assert.ThrowsAsync<ValidacionException>(() => Pedir(ok with { Articulos = [new(p.Variantes[0].Id, 60), new(p.Variantes[0].Id, 40)] })); // 100 juntas
        await Assert.ThrowsAsync<ValidacionException>(() => Pedir(ok with { Nombre = "  " }));
        await Assert.ThrowsAsync<ValidacionException>(() => Pedir(ok with { Nombre = new string('a', 81) }));
        await Assert.ThrowsAsync<ValidacionException>(() => Pedir(ok with { Celular = "" }));
        await Assert.ThrowsAsync<ValidacionException>(() => Pedir(ok with { Celular = "4123456" }));
        await Assert.ThrowsAsync<ValidacionException>(() => Pedir(ok with { Entrega = EntregaPedido.Envio, Direccion = " " }));
        await Assert.ThrowsAsync<ValidacionException>(() => Pedir(ok with { Entrega = (EntregaPedido)7 }));
        await Assert.ThrowsAsync<ValidacionException>(() => Pedir(ok with { Nota = new string('a', 301) }));
        Assert.Equal(0, await _bd.Contexto(_bd.TiendaA).Pedidos.CountAsync());
    }

    [Fact]
    public async Task Una_tienda_que_no_recibe_pedidos_no_acepta_el_carrito()
    {
        var p = await ConStock(ComoDuena(_bd.TiendaA), "Cartera", 9);

        Configurar(_bd.TiendaA, t => t.PedidosPorWhatsapp = false);
        await Assert.ThrowsAsync<TiendaSinPedidosException>(() => Pedir(Pedido((p.Variantes[0].Id, 1))));
        Assert.False((await ComoPublico().RevisarCarritoAsync("tienda-a", new([p.Variantes[0].Id]), default)).RecibePedidos);

        Configurar(_bd.TiendaA, t => { t.PedidosPorWhatsapp = true; t.Whatsapp = null; });
        await Assert.ThrowsAsync<TiendaSinPedidosException>(() => Pedir(Pedido((p.Variantes[0].Id, 1))));

        Configurar(_bd.TiendaA, t => { t.Whatsapp = "+59171234567"; t.Activa = false; });
        await Assert.ThrowsAsync<TiendaPublicaNoEncontradaException>(() => Pedir(Pedido((p.Variantes[0].Id, 1))));
    }

    [Fact]
    public async Task Lo_que_no_esta_a_la_venta_no_se_puede_pedir()
    {
        var d = ComoDuena(_bd.TiendaA);
        var archivado = await ConStock(d, "Archivado", 5);
        await d.Productos.ArchivarAsync(archivado.Id, true, default);
        var oculto = await Crear(d, "Oculto", visible: false);
        var conColores = await ConStock(d, "Cartera", 5, colores: ["café", "rojo"]);
        await d.Stock.AjustarAsync(new(conColores.Id, [new(conColores.Variantes[1].Id, 0)], null), default);
        await d.Productos.ActualizarAsync(conColores.Id, new GuardarProductoDto("Cartera", null, 180, 100, null, true, ["café"], null, null, true), default); // saca el rojo
        var deOtraTienda = await ConStock(ComoDuena(_bd.TiendaB), "Ajeno", 5);

        foreach (var variante in new[] { archivado.Variantes[0].Id, oculto.Variantes[0].Id, conColores.Variantes[1].Id, deOtraTienda.Variantes[0].Id, Guid.NewGuid() })
            await Assert.ThrowsAsync<ProductoNoPedibleException>(() => Pedir(Pedido((conColores.Variantes[0].Id, 1), (variante, 1))));

        Assert.Equal(0, await _bd.Contexto(_bd.TiendaA).Pedidos.CountAsync());
    }

    [Fact]
    public async Task Lo_agotado_se_rechaza_solo_si_la_tienda_muestra_la_disponibilidad()
    {
        var d = ComoDuena(_bd.TiendaA);
        var agotado = await Crear(d, "Agotado"); // con control y sin unidades
        var porEncargo = await Crear(d, "Por encargo", controla: false);

        var ex = await Assert.ThrowsAsync<ProductoNoPedibleException>(() => Pedir(Pedido((porEncargo.Variantes[0].Id, 1), (agotado.Variantes[0].Id, 1))));
        Assert.Contains("«Agotado» se agotó", ex.Message);
        await Pedir(Pedido((porEncargo.Variantes[0].Id, 3))); // sin control de stock: siempre se puede

        // Si la tienda oculta la disponibilidad, rechazarlo la revelaría: se acepta y la dueña lo resuelve.
        Configurar(_bd.TiendaA, t => t.MostrarDisponibilidad = false);
        await Pedir(Pedido((agotado.Variantes[0].Id, 1)));
        Assert.Equal(2, await _bd.Contexto(_bd.TiendaA).Pedidos.CountAsync());
    }

    [Fact]
    public async Task Pedir_mas_de_lo_que_hay_no_se_rechaza_para_no_revelar_el_stock_exacto()
    {
        var p = await ConStock(ComoDuena(_bd.TiendaA), "Cartera", 2);

        var r = await Pedir(Pedido((p.Variantes[0].Id, 50)));

        Assert.Equal(50, r.Lineas.Single().Unidades);
    }

    [Fact]
    public async Task Con_los_precios_ocultos_el_cliente_no_los_ve_pero_el_pedido_los_guarda()
    {
        var d = ComoDuena(_bd.TiendaA);
        var p = await ConStock(d, "Cartera", 5, 180);
        Configurar(_bd.TiendaA, t => t.MostrarPrecios = false);

        var r = await Pedir(Pedido((p.Variantes[0].Id, 2)));

        Assert.Null(r.Total);
        Assert.Null(r.Lineas.Single().Precio);
        Assert.Equal(360, (await d.Pedidos.ListarAsync(new(), default)).Items.Single().Total);
    }

    // ───────────── Revisar el carrito ─────────────

    [Fact]
    public async Task Revisar_el_carrito_devuelve_como_esta_hoy_cada_cosa_y_saca_lo_que_ya_no_existe()
    {
        var d = ComoDuena(_bd.TiendaA);
        var cartera = await ConStock(d, "Cartera", 5, 180, ["café", "negro"]);
        var agotado = await Crear(d, "Aretes", 45);
        var oculto = await ConStock(d, "Oculto", 3);
        await d.Productos.ActualizarAsync(oculto.Id, new GuardarProductoDto("Oculto", null, 180, 100, null, false, null, null, null, true), default);

        var r = await ComoPublico().RevisarCarritoAsync("tienda-a", new([agotado.Variantes[0].Id, cartera.Variantes[1].Id, oculto.Variantes[0].Id, Guid.NewGuid()]), default);

        Assert.True(r.RecibePedidos);
        Assert.Equal([("Aretes", (string?)null, (decimal?)45, (bool?)false), ("Cartera", "negro", 180, true)],
            r.Articulos.Select(a => (a.Nombre, a.Variante, a.Precio, a.Disponible)));

        Configurar(_bd.TiendaA, t => { t.MostrarPrecios = false; t.MostrarDisponibilidad = false; });
        var oculta = await ComoPublico().RevisarCarritoAsync("tienda-a", new([cartera.Variantes[0].Id]), default);
        Assert.Equal(((decimal?)null, (bool?)null), (oculta.Articulos[0].Precio, oculta.Articulos[0].Disponible));
    }

    // ───────────── La dueña los atiende ─────────────

    [Fact]
    public async Task Confirmar_y_cancelar_pasan_una_sola_vez()
    {
        var d = ComoDuena(_bd.TiendaA);
        var p = await ConStock(d, "Cartera", 5);
        await Pedir(Pedido((p.Variantes[0].Id, 1)));
        await Pedir(Pedido((p.Variantes[0].Id, 1)));
        var ids = (await d.Pedidos.ListarAsync(new(), default)).Items.Select(x => x.Id).ToList(); // más nuevo primero

        Assert.Equal(new ResumenPedidosDto(2, 0), await d.Pedidos.ResumenAsync(default));
        Assert.Equal(EstadoPedido.Confirmado, (await d.Pedidos.ConfirmarAsync(ids[1], default)).Estado);
        await Assert.ThrowsAsync<PedidoYaResueltoException>(() => d.Pedidos.ConfirmarAsync(ids[1], default));
        Assert.Equal(new ResumenPedidosDto(1, 1), await d.Pedidos.ResumenAsync(default));

        var cancelado = await d.Pedidos.CancelarAsync(ids[0], new("  El cliente no respondió "), default);
        Assert.Equal((EstadoPedido.Cancelado, "El cliente no respondió", _reloj.GetUtcNow()), (cancelado.Estado, cancelado.MotivoCancelacion, cancelado.ResueltoEn));
        await Assert.ThrowsAsync<PedidoYaResueltoException>(() => d.Pedidos.CancelarAsync(ids[0], new(null), default));
        await Assert.ThrowsAsync<PedidoYaResueltoException>(() => d.Pedidos.ConfirmarAsync(ids[0], default));
        await Assert.ThrowsAsync<PedidoYaResueltoException>(() => d.Pedidos.EntregarAsync(ids[0], new(MetodoPago.Efectivo), default));

        Assert.Equal([ids[1]], (await d.Pedidos.ListarAsync(new(), default)).Items.Select(x => x.Id));
        Assert.Equal([ids[0]], (await d.Pedidos.ListarAsync(new(VistaPedidos.Cancelados), default)).Items.Select(x => x.Id));
        Assert.Equal(2, (await d.Pedidos.ListarAsync(new(VistaPedidos.Todos), default)).Items.Count);
        await Assert.ThrowsAsync<PedidoNoEncontradoException>(() => d.Pedidos.ConfirmarAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Entregar_registra_la_venta_descuenta_el_stock_y_no_se_repite()
    {
        var d = ComoDuena(_bd.TiendaA);
        var cartera = await ConStock(d, "Cartera", 5, 180, ["café", "negro"]);
        var porEncargo = await Crear(d, "Collar", 90, controla: false);
        await Pedir(Pedido((cartera.Variantes[0].Id, 2), (porEncargo.Variantes[0].Id, 1)));
        var id = (await d.Pedidos.ListarAsync(new(), default)).Items.Single().Id;

        var entregado = await d.Pedidos.EntregarAsync(id, new(MetodoPago.Qr), default);

        Assert.Equal(EstadoPedido.Entregado, entregado.Estado);
        var venta = await ComoDuena(_bd.TiendaA).Db.Ventas.Include(v => v.Lineas).SingleAsync();
        Assert.Equal((venta.Id, MetodoPago.Qr, "Ana Pérez", id), (entregado.VentaId!.Value, venta.MetodoPago, venta.Cliente, venta.ClaveIdempotencia));
        Assert.Equal(450, venta.Lineas.Sum(l => l.PrecioUnitario * l.Cantidad));
        Assert.Equal(3, (await ComoDuena(_bd.TiendaA).Stock.ObtenerAsync(cartera.Id, default)).Variantes[0].Stock);
        await Assert.ThrowsAsync<PedidoYaResueltoException>(() => ComoDuena(_bd.TiendaA).Pedidos.EntregarAsync(id, new(MetodoPago.Qr), default));
        Assert.Equal(1, await _bd.Contexto(_bd.TiendaA).Ventas.CountAsync());
        Assert.Equal([id], (await d.Pedidos.ListarAsync(new(VistaPedidos.Entregados), default)).Items.Select(x => x.Id));
    }

    [Fact]
    public async Task Sin_stock_suficiente_no_se_entrega_y_el_pedido_queda_como_estaba()
    {
        var d = ComoDuena(_bd.TiendaA);
        var p = await ConStock(d, "Cartera", 2);
        await Pedir(Pedido((p.Variantes[0].Id, 5)));
        var id = (await d.Pedidos.ListarAsync(new(), default)).Items.Single().Id;

        await Assert.ThrowsAsync<StockInsuficienteException>(() => d.Pedidos.EntregarAsync(id, new(MetodoPago.Efectivo), default));

        var otra = ComoDuena(_bd.TiendaA);
        Assert.Equal(EstadoPedido.Nuevo, (await otra.Pedidos.ObtenerAsync(id, default)).Estado);
        Assert.Equal(0, await otra.Db.Ventas.CountAsync());
        await otra.Stock.IngresarAsync(new(p.Id, [new(p.Variantes[0].Id, 3)]), default); // llegaron más
        Assert.Equal(EstadoPedido.Entregado, (await otra.Pedidos.EntregarAsync(id, new(MetodoPago.Efectivo), default)).Estado);
    }

    [Fact]
    public async Task Otra_tienda_no_ve_ni_atiende_los_pedidos()
    {
        var p = await ConStock(ComoDuena(_bd.TiendaA), "Cartera", 5);
        await Pedir(Pedido((p.Variantes[0].Id, 1)));
        var id = (await ComoDuena(_bd.TiendaA).Pedidos.ListarAsync(new(), default)).Items.Single().Id;

        var b = ComoDuena(_bd.TiendaB);

        Assert.Empty((await b.Pedidos.ListarAsync(new(VistaPedidos.Todos), default)).Items);
        Assert.Equal(new ResumenPedidosDto(0, 0), await b.Pedidos.ResumenAsync(default));
        await Assert.ThrowsAsync<PedidoNoEncontradoException>(() => b.Pedidos.ObtenerAsync(id, default));
        await Assert.ThrowsAsync<PedidoNoEncontradoException>(() => b.Pedidos.ConfirmarAsync(id, default));
        await Assert.ThrowsAsync<PedidoNoEncontradoException>(() => b.Pedidos.CancelarAsync(id, new(null), default));
        await Assert.ThrowsAsync<PedidoNoEncontradoException>(() => b.Pedidos.EntregarAsync(id, new(MetodoPago.Efectivo), default));
        Assert.Equal(EstadoPedido.Nuevo, (await ComoDuena(_bd.TiendaA).Pedidos.ObtenerAsync(id, default)).Estado);
    }

    [Fact]
    public async Task Sin_tienda_en_sesion_no_se_ve_nada()
    {
        var db = _bd.Contexto(null);
        var sinSesion = new PedidoService(db, null!, new BaseDePrueba.TiendaFija(null), _reloj);

        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => sinSesion.ListarAsync(new(), default));
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => sinSesion.ResumenAsync(default));
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => sinSesion.ConfirmarAsync(Guid.NewGuid(), default));
    }

    // ───────────── Concurrencia (solo con Postgres real) ─────────────

    private async Task ConPostgresAsync(Func<Func<Guid?, HermesDbContext>, Func<TiendaPublicaContexto, HermesDbContext>, Guid, Task> prueba)
    {
        var conexion = Environment.GetEnvironmentVariable("HERMES_TEST_PG");
        if (string.IsNullOrWhiteSpace(conexion)) return;

        var baseDeDatos = "hermes_test_" + Guid.NewGuid().ToString("N")[..10];
        await using (var admin = new Npgsql.NpgsqlConnection(conexion + ";Database=postgres"))
        {
            await admin.OpenAsync();
            await new Npgsql.NpgsqlCommand($"create database {baseDeDatos}", admin).ExecuteNonQueryAsync();
        }
        DbContextOptions<HermesDbContext> Opciones() => new DbContextOptionsBuilder<HermesDbContext>()
            .UseNpgsql($"{conexion};Database={baseDeDatos}").UseSnakeCaseNamingConvention().Options;
        HermesDbContext Nuevo(Guid? tienda) => new(Opciones(), new BaseDePrueba.TiendaFija(tienda));
        HermesDbContext Publico(TiendaPublicaContexto contexto) => new(Opciones(), contexto);
        try
        {
            Guid tienda;
            await using (var db = Nuevo(null))
            {
                await db.Database.EnsureCreatedAsync();
                var dueno = new Usuario { Nombre = "D", Email = "d@x.test", PasswordHash = "x" };
                var t = new Tienda { DuenoId = dueno.Id, Slug = "tienda-x", Nombre = "X", Whatsapp = "+59171234567" };
                db.AddRange(dueno, t);
                await db.SaveChangesAsync();
                tienda = t.Id;
            }
            await prueba(Nuevo, Publico, tienda);
        }
        finally
        {
            Npgsql.NpgsqlConnection.ClearAllPools();
            await using var admin = new Npgsql.NpgsqlConnection(conexion + ";Database=postgres");
            await admin.OpenAsync();
            await new Npgsql.NpgsqlCommand($"drop database if exists {baseDeDatos} with (force)", admin).ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Ocho_pedidos_simultaneos_reciben_numeros_distintos_con_Postgres_real()
    {
        await ConPostgresAsync(async (nuevo, publico, tienda) =>
        {
            Guid variante;
            await using (var db = nuevo(tienda)) variante = (await ConStock(ComoDuena(db, tienda), "Cartera", 50)).Variantes[0].Id;

            var numeros = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
            {
                var contexto = new TiendaPublicaContexto();
                await using var db = publico(contexto);
                return (await new PublicoService(db, new AlmacenEnBaseDeDatos(db, _reloj), contexto, _reloj).PedirAsync("tienda-x", Pedido((variante, 1)), default)).Numero;
            }));

            Assert.Equal(Enumerable.Range(1, 8), numeros.Order());
        });
    }

    [Fact]
    public async Task Seis_reintentos_simultaneos_con_la_misma_clave_dejan_un_solo_pedido_con_Postgres_real()
    {
        await ConPostgresAsync(async (nuevo, publico, tienda) =>
        {
            Guid variante;
            await using (var db = nuevo(tienda)) variante = (await ConStock(ComoDuena(db, tienda), "Cartera", 50)).Variantes[0].Id;
            var dto = Pedido((variante, 2));

            var resultados = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
            {
                var contexto = new TiendaPublicaContexto();
                await using var db = publico(contexto);
                return await new PublicoService(db, new AlmacenEnBaseDeDatos(db, _reloj), contexto, _reloj).PedirAsync("tienda-x", dto, default);
            }));

            Assert.Equal(1, resultados.Count(r => r.Nuevo));
            Assert.All(resultados, r => Assert.Equal(1, r.Numero));
            await using var lectura = nuevo(tienda);
            Assert.Equal(1, await lectura.Pedidos.CountAsync());
        });
    }

    [Fact]
    public async Task Cinco_entregas_simultaneas_registran_una_sola_venta_con_Postgres_real()
    {
        await ConPostgresAsync(async (nuevo, publico, tienda) =>
        {
            Guid producto, pedido;
            await using (var db = nuevo(tienda))
            {
                var p = await ConStock(ComoDuena(db, tienda), "Cartera", 10);
                producto = p.Id;
                var contexto = new TiendaPublicaContexto();
                await using var dbp = publico(contexto);
                await new PublicoService(dbp, new AlmacenEnBaseDeDatos(dbp, _reloj), contexto, _reloj).PedirAsync("tienda-x", Pedido((p.Variantes[0].Id, 3)), default);
                pedido = (await ComoDuena(db, tienda).Pedidos.ListarAsync(new(), default)).Items.Single().Id;
            }

            var resultados = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
            {
                await using var db = nuevo(tienda);
                try { await ComoDuena(db, tienda).Pedidos.EntregarAsync(pedido, new(MetodoPago.Efectivo), default); return true; }
                catch (PedidoYaResueltoException) { return false; }
            }));

            await using var lectura = nuevo(tienda);
            Assert.Equal(1, await lectura.Ventas.CountAsync(v => !v.Anulada));
            Assert.Equal(7, (await ComoDuena(lectura, tienda).Stock.ObtenerAsync(producto, default)).Variantes[0].Stock); // descontó UNA vez
            Assert.True(resultados.Count(r => r) >= 1);
        });
    }
}
