using Hermes.Application.Dtos;
using Hermes.Domain.Common;
using Hermes.Domain.Entities;
using Hermes.Domain.Enums;
using Hermes.Infrastructure;
using Hermes.Infrastructure.Imagenes;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Services;
using Hermes.Infrastructure.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Hermes.UnitTests;

// Row Level Security (segunda barrera). SOLO con Postgres real (HERMES_TEST_PG): crea una base con las migraciones de
// verdad y un rol SIN privilegios (como la app en producción), y comprueba que Postgres corta lo que el filtro de EF no.
// Sin la variable, las pruebas salen sin probar nada (como las demás de concurrencia).
public class SeguridadPorFilaTests
{
    private readonly FakeTimeProvider _reloj = new(DateTimeOffset.UtcNow);

    private sealed record Entorno(Func<Guid?, HermesDbContext> App, Func<ITiendaActualPrueba, HermesDbContext> AppPublica, Func<HermesDbContext> Duenio, Guid A, Guid B);

    public interface ITiendaActualPrueba : Hermes.Application.Interfaces.ITiendaActual;

    private async Task ConRlsAsync(Func<Entorno, Task> prueba)
    {
        var conexion = Environment.GetEnvironmentVariable("HERMES_TEST_PG");
        if (string.IsNullOrWhiteSpace(conexion)) return;

        var sufijo = Guid.NewGuid().ToString("N")[..10];
        var baseDeDatos = "hermes_rls_" + sufijo;
        var rol = "hermes_app_" + sufijo;
        var clave = Guid.NewGuid().ToString("N");
        await Ejecutar(conexion + ";Database=postgres", $"create database {baseDeDatos}");
        var cadenaDuenio = $"{conexion};Database={baseDeDatos}";
        var cadenaApp = new NpgsqlConnectionStringBuilder(cadenaDuenio) { Username = rol, Password = clave }.ConnectionString;

        HermesDbContext Duenio() => new(HermesDbContext.Configurar(new DbContextOptionsBuilder<HermesDbContext>(), cadenaDuenio).Options as DbContextOptions<HermesDbContext> ?? throw new(), new BaseDePrueba.TiendaFija(null));
        HermesDbContext Contexto(Hermes.Application.Interfaces.ITiendaActual t)
        {
            var o = new DbContextOptionsBuilder<HermesDbContext>();
            HermesDbContext.Configurar(o, cadenaApp).ConSeguridadPorFila();
            return new HermesDbContext(o.Options, t);
        }
        try
        {
            await using (var db = Duenio()) await db.Database.MigrateAsync();
            await Ejecutar(cadenaDuenio, $"""
                create role {rol} login password '{clave}';
                grant connect on database {baseDeDatos} to {rol};
                grant usage on schema hermes to {rol};
                grant select, insert, update, delete on all tables in schema hermes to {rol};
                grant usage, select on all sequences in schema hermes to {rol};
                """);

            Guid a, b;
            await using (var db = Duenio())
            {
                var dueno = new Usuario { Nombre = "D", Email = "d@x.test", PasswordHash = "x" };
                var ta = new Tienda { DuenoId = dueno.Id, Slug = "tienda-a", Nombre = "A", Whatsapp = "+59171234567" };
                var tb = new Tienda { DuenoId = dueno.Id, Slug = "tienda-b", Nombre = "B" };
                db.AddRange(dueno, ta, tb);
                await db.SaveChangesAsync();
                (a, b) = (ta.Id, tb.Id);
            }
            await prueba(new Entorno(t => Contexto(new BaseDePrueba.TiendaFija(t)), t => Contexto(t), Duenio, a, b));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await Ejecutar(conexion + ";Database=postgres", $"drop database if exists {baseDeDatos} with (force)");
            await Ejecutar(conexion + ";Database=postgres", $"drop role if exists {rol}");
        }
    }

    private static async Task Ejecutar(string cadena, string sql)
    {
        await using var c = new NpgsqlConnection(cadena);
        await c.OpenAsync();
        await new NpgsqlCommand(sql, c).ExecuteNonQueryAsync();
    }

    private async Task<ProductoDto> CrearProducto(HermesDbContext db, Guid tienda, string nombre, int stock = 0)
    {
        var almacen = new AlmacenEnBaseDeDatos(db, _reloj);
        var sesion = new BaseDePrueba.TiendaFija(tienda);
        var p = await new ProductoService(db, new ImagenService(db, almacen, sesion, _reloj), almacen, sesion, _reloj)
            .CrearAsync(new GuardarProductoDto(nombre, null, 100, 40, null, true, null, null, null), default);
        if (stock > 0)
            await new StockService(db, new OperacionesStock(db, _reloj), almacen, sesion).IngresarAsync(new(p.Id, [new(p.Variantes[0].Id, stock)]), default);
        return p;
    }

    [Fact]
    public async Task Postgres_corta_lo_que_se_escapa_del_filtro_de_EF()
    {
        await ConRlsAsync(async e =>
        {
            await using (var db = e.App(e.A)) await CrearProducto(db, e.A, "De A");
            await using (var db = e.App(e.B)) { await CrearProducto(db, e.B, "De B 1"); await CrearProducto(db, e.B, "De B 2"); }

            // IgnoreQueryFilters = sin la primera barrera: igual se ve solo lo propio.
            await using (var db = e.App(e.A))
                Assert.Equal(["De A"], await db.Productos.IgnoreQueryFilters().Select(p => p.Nombre).ToListAsync());
            await using (var db = e.App(e.B))
                Assert.Equal(2, await db.Variantes.IgnoreQueryFilters().CountAsync());
            // Sin tienda en sesión: nada.
            await using (var db = e.App(null))
                Assert.Equal(0, await db.Productos.IgnoreQueryFilters().CountAsync());
            // Dentro de AccesoATodasLasTiendas: todo.
            await using (var db = e.App(e.A))
            using (db.AccesoATodasLasTiendas())
                Assert.Equal(3, await db.Productos.IgnoreQueryFilters().CountAsync());
            // Y al salir del bloque vuelve a ser solo la propia (la variable se vuelve a poner en la misma conexión).
            await using (var db = e.App(e.A))
            {
                await db.Database.OpenConnectionAsync();
                using (db.AccesoATodasLasTiendas()) Assert.Equal(3, await db.Productos.IgnoreQueryFilters().CountAsync());
                Assert.Equal(1, await db.Productos.IgnoreQueryFilters().CountAsync());
            }
        });
    }

    [Fact]
    public async Task Postgres_no_deja_escribir_ni_tocar_filas_de_otra_tienda_ni_con_SQL_a_mano()
    {
        await ConRlsAsync(async e =>
        {
            Guid deB;
            await using (var db = e.App(e.B)) deB = (await CrearProducto(db, e.B, "De B")).Id;

            await using (var db = e.App(e.A))
            {
                // UPDATE / DELETE sobre todo: solo alcanza a las filas propias (cero acá).
                Assert.Equal(0, await db.Database.ExecuteSqlRawAsync("update hermes.productos set nombre = 'hackeado'"));
                Assert.Equal(0, await db.Database.ExecuteSqlRawAsync("delete from hermes.producto_fotos"));
                // INSERT con la tienda de otro: la política lo rechaza (42501).
                var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                    "insert into hermes.categorias (id, tienda_id, nombre, activa) values ({0}, {1}, 'X', true)", Guid.NewGuid(), e.B));
                Assert.Equal("42501", ex.SqlState);
            }
            await using (var db = e.Duenio())
                Assert.Equal("De B", (await db.Productos.IgnoreQueryFilters().SingleAsync(p => p.Id == deB)).Nombre);
        });
    }

    [Fact]
    public async Task Ventas_pedidos_publicos_y_transacciones_funcionan_con_la_barrera_puesta()
    {
        await ConRlsAsync(async e =>
        {
            ProductoDto p;
            await using (var db = e.App(e.A)) p = await CrearProducto(db, e.A, "Cartera", stock: 5);

            // La tienda pública fija la tienda A MITAD del pedido (al leer el slug), con la conexión ya usada.
            var contexto = new TiendaPublicaContexto();
            await using (var db = e.AppPublica(new TiendaPublicaPrueba(contexto)))
            {
                var r = await new PublicoService(db, new AlmacenEnBaseDeDatos(db, _reloj), contexto, _reloj)
                    .PedirAsync("tienda-a", new(Guid.NewGuid(), [new(p.Variantes[0].Id, 2)], "Ana", "71234567", EntregaPedido.Retiro, null, null, null), default);
                Assert.Equal(1, r.Numero);
            }

            // La dueña registra una venta (transacción + candado por variante) y entrega el pedido.
            await using (var db = e.App(e.A))
            {
                var sesion = new BaseDePrueba.TiendaFija(e.A);
                var almacen = new AlmacenEnBaseDeDatos(db, _reloj);
                var ventas = new VentaService(db, new OperacionesStock(db, _reloj), almacen, sesion, _reloj);
                await ventas.RegistrarAsync(new(Guid.NewGuid(), [new(p.Variantes[0].Id, 1)], MetodoPago.Efectivo), default);
                var pedidos = new PedidoService(db, ventas, sesion, _reloj);
                var id = (await pedidos.ListarAsync(new(), default)).Items.Single().Id;
                Assert.Equal(EstadoPedido.Entregado, (await pedidos.EntregarAsync(id, new(MetodoPago.Qr), default)).Estado);
                Assert.Equal(2, (await new StockService(db, new OperacionesStock(db, _reloj), almacen, sesion).ObtenerAsync(p.Id, default)).Variantes[0].Stock);
            }

            // Una venta que falla (sin stock) deshace su transacción; la conexión sigue sirviendo para la misma tienda.
            await using (var db = e.App(e.A))
            {
                var sesion = new BaseDePrueba.TiendaFija(e.A);
                var ventas = new VentaService(db, new OperacionesStock(db, _reloj), new AlmacenEnBaseDeDatos(db, _reloj), sesion, _reloj);
                await db.Database.OpenConnectionAsync();
                await Assert.ThrowsAnyAsync<Exception>(() => ventas.RegistrarAsync(new(Guid.NewGuid(), [new(p.Variantes[0].Id, 50)], MetodoPago.Efectivo), default));
                Assert.Equal(2, await db.Ventas.CountAsync());
            }
            // La otra tienda no ve nada de esto.
            await using (var db = e.App(e.B))
                Assert.Equal((0, 0), (await db.Ventas.IgnoreQueryFilters().CountAsync(), await db.Pedidos.IgnoreQueryFilters().CountAsync()));
        });
    }

    [Fact]
    public async Task Toda_tabla_de_una_tienda_tiene_su_politica()
    {
        await ConRlsAsync(async e =>
        {
            await using var db = e.Duenio();
            var delModelo = db.Model.GetEntityTypes()
                .Where(t => typeof(IPerteneceATienda).IsAssignableFrom(t.ClrType))
                .Select(t => t.GetTableName()!).Order().ToList();
            var conPolitica = await db.Database.SqlQueryRaw<string>(
                "select tablename as \"Value\" from pg_policies where schemaname = 'hermes' and policyname = 'por_tienda' order by 1").ToListAsync();
            var conRls = await db.Database.SqlQueryRaw<string>(
                "select c.relname as \"Value\" from pg_class c join pg_namespace n on n.oid = c.relnamespace where n.nspname = 'hermes' and c.relrowsecurity order by 1").ToListAsync();

            Assert.Equal(delModelo, conPolitica);
            Assert.Equal(delModelo, conRls);
        });
    }

    private sealed class TiendaPublicaPrueba(TiendaPublicaContexto contexto) : ITiendaActualPrueba
    {
        public Guid? TiendaId => contexto.TiendaId;
    }
}
