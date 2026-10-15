using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Hermes.Infrastructure.Persistence;

// SEGUNDA BARRERA multi-tienda: Row Level Security en Postgres (migración AgregarSeguridadPorFila).
//
// La primera barrera es el filtro global de EF (HermesDbContext). Esta es independiente: aunque una consulta se escape del
// filtro (IgnoreQueryFilters olvidado, SQL a mano, un bug), Postgres devuelve y acepta SOLO filas de la tienda que diga la
// variable de sesión `hermes.tienda`. Las políticas usan `hermes.tienda_permitida(tienda_id)`:
//   "*"  → todas (solo dentro de HermesDbContext.AccesoATodasLasTiendas)
//   ""   → ninguna (sin tienda en sesión)
//   uuid → esa tienda
//
// Estos interceptores ponen la variable: al abrir cada conexión y, si cambió desde entonces (la tienda pública la fija a
// mitad del pedido, al leer el slug), antes de cada comando. Se activan con `Base:SeguridadPorFila` = true.
//
// OJO: RLS NO se aplica al dueño de las tablas ni a un superusuario (en desarrollo la app entra como `postgres`: ahí no
// cambia nada). En producción la app tiene que conectarse con un rol SIN esos privilegios (ver deploy/ y docs/DESPLIEGUE.md);
// las migraciones corren con el dueño (`ConnectionStrings:HermesDbMigraciones`).
internal static class EstadoRls
{
    // Qué valor quedó puesto en cada conexión abierta. Al devolverla al pool, Npgsql la resetea (DISCARD ALL): por eso se
    // vuelve a poner al abrir.
    private static readonly ConditionalWeakTable<DbConnection, string> Puesto = new();

    public static bool HaceFalta(DbConnection conexion, string valor)
        => !Puesto.TryGetValue(conexion, out var actual) || actual != valor;

    public static void Recordar(DbConnection conexion, string valor) => Puesto.AddOrUpdate(conexion, valor);

    public static void Olvidar(DbConnection conexion) => Puesto.Remove(conexion);

    private static DbCommand Comando(DbConnection conexion, DbTransaction? transaccion, string valor)
    {
        var c = conexion.CreateCommand();
        c.Transaction = transaccion;
        c.CommandText = "SELECT set_config('hermes.tienda', @tienda, false)";
        var p = c.CreateParameter();
        p.ParameterName = "tienda";
        p.Value = valor;
        c.Parameters.Add(p);
        return c;
    }

    public static void Poner(DbConnection conexion, DbTransaction? transaccion, string valor)
    {
        using var c = Comando(conexion, transaccion, valor);
        c.ExecuteNonQuery();
        Recordar(conexion, valor);
    }

    public static async Task PonerAsync(DbConnection conexion, DbTransaction? transaccion, string valor, CancellationToken ct)
    {
        await using var c = Comando(conexion, transaccion, valor);
        await c.ExecuteNonQueryAsync(ct);
        Recordar(conexion, valor);
    }
}

public sealed class TiendaAlAbrirConexion : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (eventData.Context is HermesDbContext db) EstadoRls.Poner(connection, null, db.TiendaParaLaBase);
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken ct = default)
    {
        if (eventData.Context is HermesDbContext db) await EstadoRls.PonerAsync(connection, null, db.TiendaParaLaBase, ct);
    }

    public override void ConnectionClosed(DbConnection connection, ConnectionEndEventData eventData) => EstadoRls.Olvidar(connection);

    public override Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData eventData)
    {
        EstadoRls.Olvidar(connection);
        return Task.CompletedTask;
    }
}

public sealed class TiendaEnCadaComando : DbCommandInterceptor
{
    private static void Asegurar(DbCommand comando, CommandEventData datos)
    {
        if (datos.Context is HermesDbContext db && comando.Connection is { } c && EstadoRls.HaceFalta(c, db.TiendaParaLaBase))
            EstadoRls.Poner(c, comando.Transaction, db.TiendaParaLaBase);
    }

    private static async Task AsegurarAsync(DbCommand comando, CommandEventData datos, CancellationToken ct)
    {
        if (datos.Context is HermesDbContext db && comando.Connection is { } c && EstadoRls.HaceFalta(c, db.TiendaParaLaBase))
            await EstadoRls.PonerAsync(c, comando.Transaction, db.TiendaParaLaBase, ct);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Asegurar(command, eventData);
        return result;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
    {
        await AsegurarAsync(command, eventData, ct);
        return result;
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Asegurar(command, eventData);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        await AsegurarAsync(command, eventData, ct);
        return result;
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Asegurar(command, eventData);
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken ct = default)
    {
        await AsegurarAsync(command, eventData, ct);
        return result;
    }
}

// Un set_config hecho DENTRO de una transacción se deshace si la transacción se deshace: la próxima vez hay que ponerlo de
// nuevo.
public sealed class TiendaTrasDeshacer : DbTransactionInterceptor
{
    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (transaction.Connection is { } c) EstadoRls.Olvidar(c);
    }

    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken ct = default)
    {
        if (transaction.Connection is { } c) EstadoRls.Olvidar(c);
        return Task.CompletedTask;
    }

    public override void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData)
    {
        if (transaction.Connection is { } c) EstadoRls.Olvidar(c);
    }

    public override Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken ct = default)
    {
        if (transaction.Connection is { } c) EstadoRls.Olvidar(c);
        return Task.CompletedTask;
    }
}

public static class SeguridadPorFila
{
    public static DbContextOptionsBuilder ConSeguridadPorFila(this DbContextOptionsBuilder opciones)
        => opciones.AddInterceptors(new TiendaAlAbrirConexion(), new TiendaEnCadaComando(), new TiendaTrasDeshacer());
}
