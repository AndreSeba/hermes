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

public class CompraTests : IDisposable
{
    private readonly BaseDePrueba _bd = new();
    private readonly FakeTimeProvider _reloj = new(new DateTimeOffset(2026, 10, 1, 15, 0, 0, TimeSpan.Zero));

    public void Dispose() => _bd.Dispose();

    private record Mundo(HermesDbContext Db, ProductoService Productos, StockService Stock, VentaService Ventas, ProveedorService Proveedores, CompraService Compras);

    private Mundo Para(HermesDbContext db, Guid? tienda)
    {
        var almacen = new AlmacenEnBaseDeDatos(db, _reloj);
        var sesion = new BaseDePrueba.TiendaFija(tienda);
        var imagenes = new ImagenService(db, almacen, sesion, _reloj);
        var operaciones = new OperacionesStock(db, _reloj);
        return new Mundo(db, new ProductoService(db, imagenes, almacen, sesion, _reloj), new StockService(db, operaciones, almacen, sesion),
            new VentaService(db, operaciones, almacen, sesion, _reloj), new ProveedorService(db, sesion), new CompraService(db, operaciones, sesion, _reloj));
    }

    private static Task<ProductoDto> Crear(Mundo m, string nombre = "Cartera de cuero Mía", string[]? colores = null,
        bool controla = true, decimal precio = 180, decimal costo = 110)
        => m.Productos.CrearAsync(new GuardarProductoDto(nombre, null, precio, costo, null, true, colores, null, null, controla), default);

    private static Task<ProveedorDto> Proveedor(Mundo m, string nombre = "Cueros Don Beto", string? telefono = "71234567")
        => m.Proveedores.CrearAsync(new(nombre, telefono), default);

    private static RegistrarCompraDto Pide(Guid proveedor, Guid variante, int cantidad, decimal costo, Guid? clave = null, bool recibida = false, bool pagada = false)
        => new(clave ?? Guid.NewGuid(), proveedor, [new(variante, cantidad, costo)], null, recibida, pagada);

    private static async Task<int> StockDe(Mundo m, Guid productoId) => (await m.Stock.ObtenerAsync(productoId, default)).Stock ?? -1;

    // ───────────── Proveedores ─────────────

    [Fact]
    public async Task Un_proveedor_se_crea_con_el_telefono_normalizado_y_sin_espacios_sobrantes()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);

        var p = await m.Proveedores.CrearAsync(new("  Cueros Don Beto  ", "712 34567", "  Entrega los viernes "), default);

        Assert.Equal(("Cueros Don Beto", "+59171234567", "Entrega los viernes", true), (p.Nombre, p.Telefono, p.Notas, p.Activo));
        Assert.Equal((0m, 0, 0), (p.PorPagar, p.EnCamino, p.Compras));
    }

    [Fact]
    public async Task Un_proveedor_rechaza_datos_invalidos_y_nombres_repetidos_sin_distinguir_mayusculas()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        await Proveedor(m);

        await Assert.ThrowsAsync<ValidacionException>(() => m.Proveedores.CrearAsync(new("  ", null), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Proveedores.CrearAsync(new(new string('x', 121), null), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Proveedores.CrearAsync(new("Otro", "12345"), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Proveedores.CrearAsync(new("Otro", null, new string('n', 301)), default));
        await Assert.ThrowsAsync<ProveedorDuplicadoException>(() => m.Proveedores.CrearAsync(new("CUEROS don beto", null), default));
        Assert.Single(await m.Proveedores.ListarAsync(true, default));
    }

    [Fact]
    public async Task Un_proveedor_se_edita_y_puede_conservar_su_propio_nombre()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var a = await Proveedor(m);
        var b = await Proveedor(m, "Hilos Rosa", null);

        var editado = await m.Proveedores.ActualizarAsync(a.Id, new("Cueros Don Beto", "68888888", "Nuevo celular"), default);

        Assert.Equal(("+59168888888", "Nuevo celular"), (editado.Telefono, editado.Notas));
        await Assert.ThrowsAsync<ProveedorDuplicadoException>(() => m.Proveedores.ActualizarAsync(b.Id, new("cueros don beto", null), default));
        await Assert.ThrowsAsync<ProveedorNoEncontradoException>(() => m.Proveedores.ActualizarAsync(Guid.NewGuid(), new("X", null), default));
    }

    [Fact]
    public async Task Un_proveedor_se_desactiva_en_vez_de_borrarse_y_no_recibe_pedidos()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Proveedor(m);
        var producto = await Crear(m);

        var inactivo = await m.Proveedores.CambiarActivoAsync(p.Id, false, default);

        Assert.False(inactivo.Activo);
        Assert.Empty(await m.Proveedores.ListarAsync(false, default));
        Assert.Single(await m.Proveedores.ListarAsync(true, default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Compras.RegistrarAsync(Pide(p.Id, producto.Variantes[0].Id, 1, 50), default));
        Assert.True((await m.Proveedores.CambiarActivoAsync(p.Id, true, default)).Activo);
        Assert.True((await m.Compras.RegistrarAsync(Pide(p.Id, producto.Variantes[0].Id, 1, 50), default)).Creada);
    }

    [Fact]
    public async Task El_proveedor_muestra_lo_que_se_le_debe_y_lo_que_esta_en_camino()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Proveedor(m);
        var producto = await Crear(m);
        var v = producto.Variantes[0].Id;

        await m.Compras.RegistrarAsync(Pide(p.Id, v, 10, 50), default);                      // 500 sin recibir ni pagar
        await m.Compras.RegistrarAsync(Pide(p.Id, v, 2, 60, recibida: true), default);        // 120 recibida, sin pagar
        await m.Compras.RegistrarAsync(Pide(p.Id, v, 4, 70, recibida: true, pagada: true), default); // pagada
        var anulada = (await m.Compras.RegistrarAsync(Pide(p.Id, v, 1, 99), default)).Compra;
        await m.Compras.AnularAsync(anulada.Id, default);                                      // no cuenta

        var dto = (await m.Proveedores.ListarAsync(false, default)).Single();

        Assert.Equal((620m, 1, 3), (dto.PorPagar, dto.EnCamino, dto.Compras));
    }

    [Fact]
    public async Task El_detalle_del_proveedor_trae_el_ultimo_precio_de_cada_producto_sin_contar_anuladas()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Proveedor(m);
        var cartera = await Crear(m);
        var aretes = await Crear(m, "Aretes");

        await m.Compras.RegistrarAsync(Pide(p.Id, cartera.Variantes[0].Id, 1, 100), default);
        await m.Compras.RegistrarAsync(Pide(p.Id, cartera.Variantes[0].Id, 1, 90), default);
        await m.Compras.RegistrarAsync(Pide(p.Id, aretes.Variantes[0].Id, 1, 30), default);
        var errada = (await m.Compras.RegistrarAsync(Pide(p.Id, cartera.Variantes[0].Id, 1, 5), default)).Compra;
        await m.Compras.AnularAsync(errada.Id, default);

        var detalle = await m.Proveedores.ObtenerAsync(p.Id, default);

        Assert.Equal(["Aretes", "Cartera de cuero Mía"], detalle.UltimosPrecios.Select(u => u.Producto));
        Assert.Equal([30m, 90m], detalle.UltimosPrecios.Select(u => u.CostoUnitario));
        await Assert.ThrowsAsync<ProveedorNoEncontradoException>(() => m.Proveedores.ObtenerAsync(Guid.NewGuid(), default));
    }

    // ───────────── Registrar ─────────────

    [Fact]
    public async Task Una_compra_nueva_queda_en_camino_sin_tocar_stock_ni_costo()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Proveedor(m);
        var producto = await Crear(m, colores: ["café", "negro"]);

        var r = await m.Compras.RegistrarAsync(new(Guid.NewGuid(), p.Id, [new(producto.Variantes[1].Id, 4, 95.5m), new(producto.Variantes[0].Id, 6, 90)], "  Pedido de octubre "), default);

        Assert.True(r.Creada);
        var c = r.Compra;
        Assert.Equal((EstadoCompra.EnCamino, false, 10, 4 * 95.5m + 6 * 90m), (c.Estado, c.Pagada, c.Unidades, c.Total));
        Assert.Equal(("Cueros Don Beto", "+59171234567", "Pedido de octubre", _reloj.GetUtcNow()), (c.Proveedor, c.ProveedorTelefono, c.Notas, c.Fecha));
        Assert.Equal(["Cartera de cuero Mía (café)", "Cartera de cuero Mía (negro)"], c.Lineas.Select(l => l.Descripcion)); // en el orden de las variantes
        Assert.Equal(0, await StockDe(m, producto.Id));
        Assert.Equal(110m, (await m.Productos.ObtenerAsync(producto.Id, default)).Costo);
        Assert.Equal(0, await db.Movimientos.CountAsync(x => x.Tipo == TipoMovimiento.Ingreso));
    }

    [Fact]
    public async Task Se_puede_anotar_una_compra_que_ya_llego_y_ya_se_pago()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Proveedor(m);
        var producto = await Crear(m);

        var c = (await m.Compras.RegistrarAsync(Pide(p.Id, producto.Variantes[0].Id, 8, 70, recibida: true, pagada: true), default)).Compra;

        Assert.Equal((EstadoCompra.Recibida, true), (c.Estado, c.Pagada));
        Assert.NotNull(c.RecibidaEn);
        Assert.NotNull(c.PagadaEn);
        Assert.Equal(8, await StockDe(m, producto.Id));
        Assert.Equal(70m, (await m.Productos.ObtenerAsync(producto.Id, default)).Costo);
    }

    [Fact]
    public async Task Las_entradas_invalidas_se_rechazan_sin_guardar_nada()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Proveedor(m);
        var producto = await Crear(m);
        var v = producto.Variantes[0].Id;

        await Assert.ThrowsAsync<ValidacionException>(() => m.Compras.RegistrarAsync(new(Guid.NewGuid(), p.Id, [], null), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Compras.RegistrarAsync(Pide(p.Id, v, 0, 50), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Compras.RegistrarAsync(Pide(p.Id, v, 100_001, 50), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Compras.RegistrarAsync(Pide(p.Id, v, 1, 0), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Compras.RegistrarAsync(Pide(p.Id, v, 1, -5), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Compras.RegistrarAsync(Pide(p.Id, v, 1, 10.555m), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Compras.RegistrarAsync(Pide(p.Id, v, 1, 2_000_000), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Compras.RegistrarAsync(Pide(p.Id, v, 1, 50, Guid.Empty), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Compras.RegistrarAsync(Pide(Guid.Empty, v, 1, 50), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Compras.RegistrarAsync(new(Guid.NewGuid(), p.Id, [new(v, 1, 50)], new string('n', 301)), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Compras.RegistrarAsync(new(Guid.NewGuid(), p.Id, [new(v, 1, 50), new(v, 2, 60)]), default)); // la misma variante dos veces
        var muchas = Enumerable.Range(0, 51).Select(_ => new LineaCompraDto(Guid.NewGuid(), 1, 10)).ToList();
        await Assert.ThrowsAsync<ValidacionException>(() => m.Compras.RegistrarAsync(new(Guid.NewGuid(), p.Id, muchas), default));

        Assert.Equal(0, await db.Compras.CountAsync());
    }

    [Fact]
    public async Task No_se_compra_a_un_proveedor_ni_un_producto_ajeno_archivado_o_que_ya_no_se_vende()
    {
        Guid proveedorDeB, varianteDeB;
        await using (var dbB = _bd.Contexto(_bd.TiendaB))
        {
            var b = Para(dbB, _bd.TiendaB);
            proveedorDeB = (await Proveedor(b)).Id;
            varianteDeB = (await Crear(b)).Variantes[0].Id;
        }
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Proveedor(m);
        var archivado = await Crear(m, "Archivado");
        await m.Productos.ArchivarAsync(archivado.Id, true, default);
        var conColores = await Crear(m, "Con colores", ["rojo", "azul"]);
        await m.Productos.ActualizarAsync(conColores.Id, new GuardarProductoDto("Con colores", null, 180, 110, null, true, ["azul"], null, null), default);

        await Assert.ThrowsAsync<ProveedorNoEncontradoException>(() => m.Compras.RegistrarAsync(Pide(proveedorDeB, conColores.Variantes[1].Id, 1, 50), default));
        await Assert.ThrowsAsync<VarianteNoEncontradaException>(() => m.Compras.RegistrarAsync(Pide(p.Id, varianteDeB, 1, 50), default));
        await Assert.ThrowsAsync<VarianteNoEncontradaException>(() => m.Compras.RegistrarAsync(Pide(p.Id, Guid.NewGuid(), 1, 50), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Compras.RegistrarAsync(Pide(p.Id, archivado.Variantes[0].Id, 1, 50), default));
        await Assert.ThrowsAsync<ValidacionException>(() => m.Compras.RegistrarAsync(Pide(p.Id, conColores.Variantes[0].Id, 1, 50), default)); // "rojo": ya no se vende
        Assert.Equal(0, await db.Compras.CountAsync());
    }

    [Fact]
    public async Task La_misma_clave_devuelve_la_misma_compra_y_no_suma_el_stock_dos_veces()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Proveedor(m);
        var producto = await Crear(m);
        var clave = Guid.NewGuid();

        var primera = await m.Compras.RegistrarAsync(Pide(p.Id, producto.Variantes[0].Id, 5, 60, clave, recibida: true), default);
        var segunda = await m.Compras.RegistrarAsync(Pide(p.Id, producto.Variantes[0].Id, 5, 60, clave, recibida: true), default);
        var tercera = await m.Compras.RegistrarAsync(Pide(p.Id, producto.Variantes[0].Id, 99, 1, clave), default);

        Assert.True(primera.Creada);
        Assert.False(segunda.Creada);
        Assert.False(tercera.Creada);
        Assert.Equal(primera.Compra.Id, tercera.Compra.Id);
        Assert.Equal(1, await db.Compras.CountAsync());
        Assert.Equal(5, await StockDe(m, producto.Id));
    }

    [Fact]
    public async Task Dos_tiendas_pueden_usar_la_misma_clave_sin_pisarse()
    {
        var clave = Guid.NewGuid();
        foreach (var tienda in new[] { _bd.TiendaA, _bd.TiendaB })
        {
            await using var db = _bd.Contexto(tienda);
            var m = Para(db, tienda);
            var p = await Proveedor(m);
            var producto = await Crear(m);
            Assert.True((await m.Compras.RegistrarAsync(Pide(p.Id, producto.Variantes[0].Id, 1, 50, clave), default)).Creada);
        }
    }

    // ───────────── Recibir ─────────────

    [Fact]
    public async Task Recibir_suma_el_stock_pone_el_costo_de_la_compra_y_deja_el_rastro()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Proveedor(m);
        var producto = await Crear(m, colores: ["café", "negro"]);
        var compra = (await m.Compras.RegistrarAsync(new(Guid.NewGuid(), p.Id, [new(producto.Variantes[0].Id, 4, 95.5m), new(producto.Variantes[1].Id, 2, 95.5m)]), default)).Compra;
        _reloj.Advance(TimeSpan.FromDays(3));

        var r = await m.Compras.RecibirAsync(compra.Id, default);

        Assert.Equal((EstadoCompra.Recibida, _reloj.GetUtcNow()), (r.Compra.Estado, r.Compra.RecibidaEn));
        Assert.Equal(6, await StockDe(m, producto.Id));
        Assert.Equal(95.5m, (await m.Productos.ObtenerAsync(producto.Id, default)).Costo);
        var movimientos = await db.Movimientos.Where(x => x.CompraId == compra.Id).ToListAsync();
        Assert.Equal(2, movimientos.Count);
        Assert.All(movimientos, x => Assert.Equal((TipoMovimiento.Ingreso, 95.5m, "Compra"), (x.Tipo, x.CostoUnitario, x.Nota)));
        Assert.All(await db.CompraLineas.ToListAsync(), l => Assert.True(l.SumoStock));
    }

    [Fact]
    public async Task Recibir_dos_veces_o_una_anulada_se_rechaza_sin_sumar_de_nuevo()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Proveedor(m);
        var producto = await Crear(m);
        var v = producto.Variantes[0].Id;
        var compra = (await m.Compras.RegistrarAsync(Pide(p.Id, v, 5, 60), default)).Compra;
        var anulada = (await m.Compras.RegistrarAsync(Pide(p.Id, v, 9, 60), default)).Compra;
        await m.Compras.AnularAsync(anulada.Id, default);
        await m.Compras.RecibirAsync(compra.Id, default);

        var yaRecibida = await Assert.ThrowsAsync<CompraEstadoInvalidoException>(() => m.Compras.RecibirAsync(compra.Id, default));
        var deAnulada = await Assert.ThrowsAsync<CompraEstadoInvalidoException>(() => m.Compras.RecibirAsync(anulada.Id, default));

        Assert.Contains("ya estaba recibida", yaRecibida.Message);
        Assert.Contains("anulada", deAnulada.Message);
        Assert.Equal(5, await StockDe(m, producto.Id));
        await Assert.ThrowsAsync<CompraNoEncontradaException>(() => m.Compras.RecibirAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Recibir_avisa_los_margenes_flojos_con_el_costo_nuevo()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Proveedor(m);
        var pierde = await Crear(m, "Pierde", precio: 100, costo: 50);
        var bajo = await Crear(m, "Bajo", precio: 100, costo: 50);
        var bien = await Crear(m, "Bien", precio: 100, costo: 50);
        var compra = (await m.Compras.RegistrarAsync(new(Guid.NewGuid(), p.Id,
            [new(pierde.Variantes[0].Id, 1, 120), new(bajo.Variantes[0].Id, 1, 85), new(bien.Variantes[0].Id, 1, 60)]), default)).Compra;

        var r = await m.Compras.RecibirAsync(compra.Id, default);

        Assert.Equal(["Bajo", "Pierde"], r.Avisos.Select(a => a.Producto));
        Assert.Equal((EstadoPrecio.Bajo, 85m, 100m), (r.Avisos[0].Estado, r.Avisos[0].Costo, r.Avisos[0].Precio));
        Assert.Equal(EstadoPrecio.Pierde, r.Avisos[1].Estado);
    }

    [Fact]
    public async Task Recibir_un_producto_por_encargo_actualiza_el_costo_pero_no_cuenta_unidades()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Proveedor(m);
        var conControl = await Crear(m);
        var porEncargo = await Crear(m, "Material", controla: false, precio: 100, costo: 10);
        var compra = (await m.Compras.RegistrarAsync(new(Guid.NewGuid(), p.Id, [new(conControl.Variantes[0].Id, 3, 70), new(porEncargo.Variantes[0].Id, 5, 25)]), default)).Compra;

        await m.Compras.RecibirAsync(compra.Id, default);

        Assert.Equal(3, await StockDe(m, conControl.Id));
        Assert.Equal(25m, (await m.Productos.ObtenerAsync(porEncargo.Id, default)).Costo);
        Assert.Equal(1, await db.Movimientos.CountAsync(x => x.CompraId == compra.Id));
        var lineas = await db.CompraLineas.ToListAsync();
        Assert.True(lineas.Single(l => l.ProductoId == conControl.Id).SumoStock);
        Assert.False(lineas.Single(l => l.ProductoId == porEncargo.Id).SumoStock);
    }

    [Fact]
    public async Task Si_algo_falla_al_recibir_no_queda_nada_a_medias()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Proveedor(m);
        var producto = await Crear(m, colores: ["rojo", "azul"]);
        var compra = (await m.Compras.RegistrarAsync(new(Guid.NewGuid(), p.Id, [new(producto.Variantes[0].Id, 4, 70), new(producto.Variantes[1].Id, 2, 70)]), default)).Compra;
        // Después de pedirla, el rojo deja de venderse: recibirla no puede sumar stock a algo desactivado.
        await m.Productos.ActualizarAsync(producto.Id, new GuardarProductoDto(producto.Nombre, null, 180, 110, null, true, ["azul"], null, null), default);

        await Assert.ThrowsAsync<ValidacionException>(() => m.Compras.RecibirAsync(compra.Id, default));

        await using var limpio = _bd.Contexto(_bd.TiendaA);
        var m2 = Para(limpio, _bd.TiendaA);
        Assert.Equal(EstadoCompra.EnCamino, (await m2.Compras.ObtenerAsync(compra.Id, default)).Estado); // la marca de "recibida" se deshizo
        Assert.Equal(0, await limpio.Movimientos.CountAsync(x => x.CompraId == compra.Id));
        Assert.Equal(110m, (await m2.Productos.ObtenerAsync(producto.Id, default)).Costo);
    }

    // ───────────── Pagar ─────────────

    [Fact]
    public async Task Se_puede_pagar_antes_o_despues_de_recibir_pero_una_sola_vez()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Proveedor(m);
        var producto = await Crear(m);
        var v = producto.Variantes[0].Id;
        var anticipo = (await m.Compras.RegistrarAsync(Pide(p.Id, v, 2, 50), default)).Compra;
        _reloj.Advance(TimeSpan.FromHours(1));

        var pagada = await m.Compras.PagarAsync(anticipo.Id, default);

        Assert.Equal((true, EstadoCompra.EnCamino, _reloj.GetUtcNow()), (pagada.Pagada, pagada.Estado, pagada.PagadaEn));
        await Assert.ThrowsAsync<CompraEstadoInvalidoException>(() => m.Compras.PagarAsync(anticipo.Id, default));
        var recibida = (await m.Compras.RecibirAsync(anticipo.Id, default)).Compra;
        Assert.True(recibida.Pagada);
        await Assert.ThrowsAsync<CompraNoEncontradaException>(() => m.Compras.PagarAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Una_compra_anulada_no_se_paga()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Proveedor(m);
        var producto = await Crear(m);
        var compra = (await m.Compras.RegistrarAsync(Pide(p.Id, producto.Variantes[0].Id, 2, 50), default)).Compra;
        await m.Compras.AnularAsync(compra.Id, default);

        var ex = await Assert.ThrowsAsync<CompraEstadoInvalidoException>(() => m.Compras.PagarAsync(compra.Id, default));

        Assert.Contains("anulada", ex.Message);
    }

    // ───────────── Anular ─────────────

    [Fact]
    public async Task Anular_una_compra_que_no_llego_no_toca_el_stock()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Proveedor(m);
        var producto = await Crear(m);
        var compra = (await m.Compras.RegistrarAsync(Pide(p.Id, producto.Variantes[0].Id, 5, 60), default)).Compra;
        _reloj.Advance(TimeSpan.FromMinutes(5));

        var anulada = await m.Compras.AnularAsync(compra.Id, default);

        Assert.Equal((EstadoCompra.Anulada, _reloj.GetUtcNow()), (anulada.Estado, anulada.AnuladaEn));
        Assert.Equal(300m, anulada.Total); // conserva sus números
        Assert.Equal(0, await db.Movimientos.CountAsync());
        await Assert.ThrowsAsync<CompraEstadoInvalidoException>(() => m.Compras.AnularAsync(compra.Id, default));
        await Assert.ThrowsAsync<CompraNoEncontradaException>(() => m.Compras.AnularAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Anular_una_compra_recibida_resta_el_stock_y_deja_el_registro()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Proveedor(m);
        var producto = await Crear(m);
        var compra = (await m.Compras.RegistrarAsync(Pide(p.Id, producto.Variantes[0].Id, 8, 60, recibida: true), default)).Compra;
        await m.Stock.IngresarAsync(new(producto.Id, [new(producto.Variantes[0].Id, 2)]), default); // 2 más por otro lado

        var anulada = await m.Compras.AnularAsync(compra.Id, default);

        Assert.Equal(EstadoCompra.Anulada, anulada.Estado);
        Assert.Equal(2, await StockDe(m, producto.Id));
        var ultimo = await db.Movimientos.Where(x => x.CompraId == compra.Id).OrderByDescending(x => x.Id).FirstAsync();
        Assert.Equal((TipoMovimiento.Reversion, -8, "Compra anulada"), (ultimo.Tipo, ultimo.Cantidad, ultimo.Nota));
        Assert.Equal(2, await db.Movimientos.CountAsync(x => x.CompraId == compra.Id)); // la llegada y la reversión: nada se borró
    }

    [Fact]
    public async Task No_se_anula_una_compra_recibida_si_esas_unidades_ya_se_vendieron()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Proveedor(m);
        var producto = await Crear(m);
        var v = producto.Variantes[0].Id;
        var compra = (await m.Compras.RegistrarAsync(Pide(p.Id, v, 5, 60, recibida: true), default)).Compra;
        await m.Ventas.RegistrarAsync(new(Guid.NewGuid(), [new(v, 3)], MetodoPago.Efectivo), default);

        var ex = await Assert.ThrowsAsync<StockInsuficienteException>(() => m.Compras.AnularAsync(compra.Id, default));

        Assert.Contains("hay 2 y se necesitan 5", ex.Message);
        await using var limpio = _bd.Contexto(_bd.TiendaA);
        var m2 = Para(limpio, _bd.TiendaA);
        Assert.Equal(EstadoCompra.Recibida, (await m2.Compras.ObtenerAsync(compra.Id, default)).Estado); // sigue vigente
        Assert.Equal(2, await StockDe(m2, producto.Id));
    }

    [Fact]
    public async Task Anular_solo_resta_lo_que_se_habia_sumado()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Proveedor(m);
        var porEncargo = await Crear(m, "Por encargo", controla: false);
        var conColores = await Crear(m, "Con colores", ["rojo", "azul"]);
        var compra = (await m.Compras.RegistrarAsync(new(Guid.NewGuid(), p.Id,
            [new(porEncargo.Variantes[0].Id, 4, 20), new(conColores.Variantes[0].Id, 3, 30), new(conColores.Variantes[1].Id, 1, 30)], null, true), default)).Compra;
        // Después de recibirla, el producto por encargo empieza a llevar control y la dueña carga 6 unidades.
        await m.Productos.ActualizarAsync(porEncargo.Id, new GuardarProductoDto("Por encargo", null, 180, 110, null, true, null, null, null, ControlaStock: true), default);
        await m.Stock.IngresarAsync(new(porEncargo.Id, [new(porEncargo.Variantes[0].Id, 6)]), default);

        await m.Compras.AnularAsync(compra.Id, default);

        // Del por encargo no se restan 4 unidades que nunca se sumaron: quedan sus 6. Lo de colores vuelve a 0.
        Assert.Equal(6, await StockDe(m, porEncargo.Id));
        Assert.Equal(0, await StockDe(m, conColores.Id));
        Assert.Equal(-3, await db.Movimientos.Where(x => x.CompraId == compra.Id && x.Tipo == TipoMovimiento.Reversion && x.VarianteId == conColores.Variantes[0].Id).SumAsync(x => x.Cantidad));
        Assert.DoesNotContain(await db.Movimientos.Where(x => x.CompraId == compra.Id).ToListAsync(), x => x.VarianteId == porEncargo.Variantes[0].Id);
    }

    [Fact]
    public async Task Anular_no_devuelve_el_costo_anterior_y_lo_deja_dicho()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Proveedor(m);
        var producto = await Crear(m);
        var compra = (await m.Compras.RegistrarAsync(Pide(p.Id, producto.Variantes[0].Id, 2, 75, recibida: true), default)).Compra;

        await m.Compras.AnularAsync(compra.Id, default);

        // No hay forma de saber "cuál era el costo bueno" (pudo cambiar a mano en el medio): se queda el último.
        Assert.Equal(75m, (await m.Productos.ObtenerAsync(producto.Id, default)).Costo);
    }

    // ───────────── Listado, resumen y comparador ─────────────

    [Fact]
    public async Task El_listado_separa_en_camino_por_pagar_y_todas_con_la_mas_nueva_primero()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Proveedor(m);
        var q = await Proveedor(m, "Hilos Rosa", null);
        var producto = await Crear(m);
        var v = producto.Variantes[0].Id;
        var camino = (await m.Compras.RegistrarAsync(Pide(p.Id, v, 1, 50), default)).Compra;                          // en camino, sin pagar
        var recibidaSinPagar = (await m.Compras.RegistrarAsync(Pide(q.Id, v, 1, 50, recibida: true), default)).Compra; // recibida, sin pagar
        var hecha = (await m.Compras.RegistrarAsync(Pide(p.Id, v, 1, 50, recibida: true, pagada: true), default)).Compra; // todo listo
        var anulada = (await m.Compras.RegistrarAsync(Pide(p.Id, v, 1, 50), default)).Compra;
        await m.Compras.AnularAsync(anulada.Id, default);

        Assert.Equal([hecha.Id, recibidaSinPagar.Id, camino.Id], (await m.Compras.ListarAsync(new(VistaCompras.Todas, Limite: 100), default)).Items.Where(i => i.Estado != EstadoCompra.Anulada).Select(i => i.Id));
        Assert.Equal(4, (await m.Compras.ListarAsync(new(), default)).Items.Count); // "Todas" muestra también la anulada, marcada
        Assert.Equal([camino.Id], (await m.Compras.ListarAsync(new(VistaCompras.EnCamino), default)).Items.Select(i => i.Id));
        Assert.Equal([recibidaSinPagar.Id, camino.Id], (await m.Compras.ListarAsync(new(VistaCompras.PorPagar), default)).Items.Select(i => i.Id));
        Assert.Equal([camino.Id, hecha.Id], (await m.Compras.ListarAsync(new(ProveedorId: p.Id), default)).Items.Where(i => i.Estado != EstadoCompra.Anulada).Select(i => i.Id).OrderBy(x => x == hecha.Id));
    }

    [Fact]
    public async Task El_listado_resume_el_detalle_y_pagina_por_cursor_sin_repetir()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Proveedor(m);
        var a = await Crear(m, "Cartera"); var b = await Crear(m, "Aretes"); var c = await Crear(m, "Collar");
        await m.Compras.RegistrarAsync(new(Guid.NewGuid(), p.Id, [new(a.Variantes[0].Id, 2, 50), new(b.Variantes[0].Id, 3, 20), new(c.Variantes[0].Id, 1, 30)]), default);
        for (var i = 0; i < 4; i++) await m.Compras.RegistrarAsync(Pide(p.Id, a.Variantes[0].Id, 1, 50), default);

        var primera = await m.Compras.ListarAsync(new(Limite: 2), default);
        var vistas = new List<Guid>(primera.Items.Select(x => x.Id));
        Guid? cursor = primera.Siguiente;
        while (cursor is not null)
        {
            var pagina = await m.Compras.ListarAsync(new(Limite: 2, Cursor: cursor), default);
            vistas.AddRange(pagina.Items.Select(x => x.Id));
            cursor = pagina.Siguiente;
        }

        Assert.Equal(5, vistas.Distinct().Count());
        var grande = (await m.Compras.ListarAsync(new(Limite: 100), default)).Items.Last();
        Assert.EndsWith(" · y 1 más", grande.Detalle);
        Assert.Equal((6, 190m), (grande.Unidades, grande.Total));
    }

    [Fact]
    public async Task El_resumen_cuenta_lo_que_se_debe_y_lo_que_esta_en_camino_sin_las_anuladas()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var p = await Proveedor(m);
        var producto = await Crear(m);
        var v = producto.Variantes[0].Id;
        await m.Compras.RegistrarAsync(Pide(p.Id, v, 10, 50), default);                         // 500: en camino y por pagar
        await m.Compras.RegistrarAsync(Pide(p.Id, v, 2, 60, recibida: true), default);           // 120: por pagar
        await m.Compras.RegistrarAsync(Pide(p.Id, v, 4, 70, pagada: true), default);             // 280: en camino, ya pagada
        await m.Compras.RegistrarAsync(Pide(p.Id, v, 1, 80, recibida: true, pagada: true), default);
        var anulada = (await m.Compras.RegistrarAsync(Pide(p.Id, v, 1, 999), default)).Compra;
        await m.Compras.AnularAsync(anulada.Id, default);

        var r = await m.Compras.ResumenAsync(default);

        Assert.Equal((620m, 2, 2, 780m), (r.PorPagar, r.ComprasPorPagar, r.EnCamino, r.ValorEnCamino));
    }

    [Fact]
    public async Task El_comparador_ordena_por_el_ultimo_costo_de_cada_proveedor_y_marca_el_mas_barato()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var barato = await Proveedor(m, "Barato", "71111111");
        var caro = await Proveedor(m, "Caro", null);
        var nuevo = await Proveedor(m, "Nuevo que no vendió esto", null);
        var producto = await Crear(m);
        var v = producto.Variantes[0].Id;
        await m.Compras.RegistrarAsync(Pide(caro.Id, v, 1, 120), default);
        await m.Compras.RegistrarAsync(Pide(barato.Id, v, 1, 100), default);
        await m.Compras.RegistrarAsync(Pide(barato.Id, v, 1, 95), default);            // el último gana
        var errada = (await m.Compras.RegistrarAsync(Pide(caro.Id, v, 1, 10), default)).Compra;
        await m.Compras.AnularAsync(errada.Id, default);                                 // y la anulada no cuenta

        var r = await m.Compras.CompararAsync(producto.Id, default);

        Assert.Equal(["Barato", "Caro"], r.Select(x => x.Proveedor));
        Assert.Equal([95m, 120m], r.Select(x => x.CostoUnitario));
        Assert.Equal([true, false], r.Select(x => x.EsElMasBarato));
        Assert.Equal("+59171111111", r[0].Telefono);
        Assert.DoesNotContain(r, x => x.ProveedorId == nuevo.Id);
        await Assert.ThrowsAsync<ProductoNoEncontradoException>(() => m.Compras.CompararAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Con_costos_iguales_los_dos_son_los_mas_baratos()
    {
        await using var db = _bd.Contexto(_bd.TiendaA);
        var m = Para(db, _bd.TiendaA);
        var a = await Proveedor(m, "A", null);
        var b = await Proveedor(m, "B", null);
        var producto = await Crear(m);
        await m.Compras.RegistrarAsync(Pide(a.Id, producto.Variantes[0].Id, 1, 50), default);
        await m.Compras.RegistrarAsync(Pide(b.Id, producto.Variantes[0].Id, 1, 50), default);

        Assert.All(await m.Compras.CompararAsync(producto.Id, default), x => Assert.True(x.EsElMasBarato));
    }

    // ───────────── Aislamiento entre tiendas ─────────────

    [Fact]
    public async Task Una_tienda_no_ve_ni_recibe_ni_paga_ni_anula_las_compras_ni_los_proveedores_de_otra()
    {
        Guid compraDeA, proveedorDeA, productoDeA;
        await using (var dbA = _bd.Contexto(_bd.TiendaA))
        {
            var a = Para(dbA, _bd.TiendaA);
            var p = await Proveedor(a);
            var producto = await Crear(a);
            proveedorDeA = p.Id;
            productoDeA = producto.Id;
            compraDeA = (await a.Compras.RegistrarAsync(Pide(p.Id, producto.Variantes[0].Id, 5, 60), default)).Compra.Id;
        }

        await using var dbB = _bd.Contexto(_bd.TiendaB);
        var b = Para(dbB, _bd.TiendaB);
        Assert.Empty(await b.Proveedores.ListarAsync(true, default));
        Assert.Empty((await b.Compras.ListarAsync(new(), default)).Items);
        Assert.Equal((0m, 0), ((await b.Compras.ResumenAsync(default)) is var r ? (r.PorPagar, r.EnCamino) : default));
        await Assert.ThrowsAsync<ProveedorNoEncontradoException>(() => b.Proveedores.ObtenerAsync(proveedorDeA, default));
        await Assert.ThrowsAsync<ProveedorNoEncontradoException>(() => b.Proveedores.CambiarActivoAsync(proveedorDeA, false, default));
        await Assert.ThrowsAsync<CompraNoEncontradaException>(() => b.Compras.ObtenerAsync(compraDeA, default));
        await Assert.ThrowsAsync<CompraNoEncontradaException>(() => b.Compras.RecibirAsync(compraDeA, default));
        await Assert.ThrowsAsync<CompraNoEncontradaException>(() => b.Compras.PagarAsync(compraDeA, default));
        await Assert.ThrowsAsync<CompraNoEncontradaException>(() => b.Compras.AnularAsync(compraDeA, default));
        await Assert.ThrowsAsync<ProductoNoEncontradoException>(() => b.Compras.CompararAsync(productoDeA, default));

        await using var verificar = _bd.Contexto(_bd.TiendaA);
        Assert.Equal(EstadoCompra.EnCamino, (await Para(verificar, _bd.TiendaA).Compras.ObtenerAsync(compraDeA, default)).Estado);
    }

    [Fact]
    public async Task Sin_tienda_en_sesion_no_se_ve_ni_se_compra_nada()
    {
        await using var db = _bd.Contexto(null);
        var m = Para(db, null);

        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => m.Proveedores.ListarAsync(false, default));
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => m.Proveedores.CrearAsync(new("X", null), default));
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => m.Compras.RegistrarAsync(Pide(Guid.NewGuid(), Guid.NewGuid(), 1, 10), default));
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => m.Compras.ListarAsync(new(), default));
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => m.Compras.ResumenAsync(default));
    }

    // ───────────── Concurrencia (solo con Postgres real) ─────────────

    // Igual que en VentaTests: sin HERMES_TEST_PG sale sin probar nada. Crea y borra su propia base.
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
    public async Task Seis_recepciones_simultaneas_de_la_misma_compra_suman_el_stock_una_sola_vez_con_Postgres_real()
    {
        await ConPostgresAsync(async (nuevo, tienda) =>
        {
            Guid compra, producto;
            await using (var db = nuevo(tienda))
            {
                var m = Para(db, tienda);
                var p = await Proveedor(m);
                var prod = await Crear(m);
                producto = prod.Id;
                compra = (await m.Compras.RegistrarAsync(Pide(p.Id, prod.Variantes[0].Id, 7, 60), default)).Compra.Id;
            }

            var resultados = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
            {
                await using var db = nuevo(tienda);
                try { await Para(db, tienda).Compras.RecibirAsync(compra, default); return true; }
                catch (CompraEstadoInvalidoException) { return false; }
            }));

            Assert.Equal(1, resultados.Count(r => r));
            await using var lectura = nuevo(tienda);
            Assert.Equal(7, await StockDe(Para(lectura, tienda), producto));
            Assert.Equal(1, await lectura.Movimientos.CountAsync(x => x.CompraId == compra));
        });
    }

    [Fact]
    public async Task Recibir_y_anular_a_la_vez_nunca_dejan_stock_de_una_compra_anulada_con_Postgres_real()
    {
        await ConPostgresAsync(async (nuevo, tienda) =>
        {
            for (var vuelta = 0; vuelta < 5; vuelta++)
            {
                Guid compra, producto;
                await using (var db = nuevo(tienda))
                {
                    var m = Para(db, tienda);
                    var p = (await m.Proveedores.ListarAsync(true, default)).FirstOrDefault() ?? await Proveedor(m);
                    var prod = await Crear(m, $"Producto {vuelta}");
                    producto = prod.Id;
                    compra = (await m.Compras.RegistrarAsync(Pide(p.Id, prod.Variantes[0].Id, 4, 60), default)).Compra.Id;
                }

                await Task.WhenAll(
                    Task.Run(async () => { await using var db = nuevo(tienda); try { await Para(db, tienda).Compras.RecibirAsync(compra, default); } catch (DominioException) { } }),
                    Task.Run(async () => { await using var db = nuevo(tienda); try { await Para(db, tienda).Compras.AnularAsync(compra, default); } catch (DominioException) { } }));

                // Gane quien gane: una compra anulada no puede dejar unidades, y una vigente recibida tiene todas.
                await using var lectura = nuevo(tienda);
                var m2 = Para(lectura, tienda);
                var estado = (await m2.Compras.ObtenerAsync(compra, default)).Estado;
                var stock = await StockDe(m2, producto);
                Assert.True(estado == EstadoCompra.Anulada ? stock == 0 : stock == 4 || (estado == EstadoCompra.EnCamino && stock == 0), $"estado {estado}, stock {stock}");
                Assert.Equal(estado == EstadoCompra.Recibida ? 4 : 0, stock);
            }
        });
    }

    [Fact]
    public async Task Diez_reintentos_simultaneos_de_la_misma_compra_registran_una_sola_con_Postgres_real()
    {
        await ConPostgresAsync(async (nuevo, tienda) =>
        {
            Guid proveedor, variante, producto;
            await using (var db = nuevo(tienda))
            {
                var m = Para(db, tienda);
                proveedor = (await Proveedor(m)).Id;
                var prod = await Crear(m);
                variante = prod.Variantes[0].Id;
                producto = prod.Id;
            }
            var clave = Guid.NewGuid();

            var resultados = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
            {
                await using var db = nuevo(tienda);
                return await Para(db, tienda).Compras.RegistrarAsync(Pide(proveedor, variante, 3, 60, clave, recibida: true), default);
            }));

            Assert.Equal(1, resultados.Count(r => r.Creada));
            Assert.Single(resultados.Select(r => r.Compra.Id).Distinct());
            await using var lectura = nuevo(tienda);
            Assert.Equal(1, await lectura.Compras.CountAsync());
            Assert.Equal(3, await StockDe(Para(lectura, tienda), producto)); // se sumó UNA vez
        });
    }
}
