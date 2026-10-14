using Hermes.Application.Interfaces;
using Hermes.Domain.Entities;
using Hermes.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hermes.UnitTests;

// SQLite en memoria (no el proveedor InMemory de EF: no respeta índices únicos ni
// transacciones). Una conexión abierta por prueba = una base nueva por prueba.
public sealed class BaseDePrueba : IDisposable
{
    private readonly SqliteConnection _conexion = new("DataSource=:memory:");

    public Guid TiendaA { get; } = Guid.CreateVersion7();
    public Guid TiendaB { get; } = Guid.CreateVersion7();

    public BaseDePrueba()
    {
        _conexion.Open();
        using var db = Contexto(null);
        db.Database.EnsureCreated();

        var dueno = new Usuario { Nombre = "Dueña", Email = "duena@hermes.test", PasswordHash = "x" };
        db.Usuarios.Add(dueno);
        db.Tiendas.AddRange(
            new Tienda { Id = TiendaA, DuenoId = dueno.Id, Slug = "tienda-a", Nombre = "Tienda A" },
            new Tienda { Id = TiendaB, DuenoId = dueno.Id, Slug = "tienda-b", Nombre = "Tienda B" });
        db.SaveChanges();
    }

    public HermesDbContext Contexto(Guid? tiendaId)
    {
        var opciones = new DbContextOptionsBuilder<HermesDbContext>()
            .UseSqlite(_conexion)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new HermesDbContext(opciones, new TiendaFija(tiendaId));
    }

    public void Dispose() => _conexion.Dispose();

    private sealed record TiendaFija(Guid? TiendaId) : ITiendaActual;
}
