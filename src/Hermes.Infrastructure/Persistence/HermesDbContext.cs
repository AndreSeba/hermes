using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Domain.Common;
using Hermes.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hermes.Infrastructure.Persistence;

public class HermesDbContext : DbContext
{
    public const string Esquema = "hermes";

    private readonly ITiendaActual _tiendaActual;

    public HermesDbContext(DbContextOptions<HermesDbContext> options, ITiendaActual tiendaActual) : base(options)
    {
        _tiendaActual = tiendaActual;
    }

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Tienda> Tiendas => Set<Tienda>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<Variante> Variantes => Set<Variante>();
    public DbSet<ProductoFoto> ProductoFotos => Set<ProductoFoto>();
    public DbSet<Imagen> Imagenes => Set<Imagen>();
    public DbSet<Archivo> Archivos => Set<Archivo>();
    public DbSet<Categoria> Categorias => Set<Categoria>();

    // Lo lee el filtro global en cada consulta (EF lo parametriza por instancia del contexto),
    // así cada request ve solo su tienda. Sin tienda en sesión el filtro no devuelve nada.
    private Guid? TiendaIdActual => _tiendaActual.TiendaId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Esquema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HermesDbContext).Assembly);

        var aplicarFiltro = typeof(HermesDbContext).GetMethod(nameof(AplicarFiltroPorTienda),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        foreach (var tipo in modelBuilder.Model.GetEntityTypes().Select(e => e.ClrType)
                     .Where(t => typeof(IPerteneceATienda).IsAssignableFrom(t)))
        {
            aplicarFiltro.MakeGenericMethod(tipo).Invoke(this, [modelBuilder]);
        }
    }

    private void AplicarFiltroPorTienda<T>(ModelBuilder modelBuilder) where T : class, IPerteneceATienda
        => modelBuilder.Entity<T>().HasQueryFilter(e => e.TiendaId == TiendaIdActual);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AsignarTienda();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        AsignarTienda();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    // Los servicios nunca fijan TiendaId: se completa acá al insertar y no se puede cambiar
    // después. Así un servicio no puede escribir, ni por error, en la tienda de otro.
    private void AsignarTienda()
    {
        foreach (var entrada in ChangeTracker.Entries<IPerteneceATienda>())
        {
            if (entrada.State == EntityState.Added)
            {
                entrada.Entity.TiendaId = _tiendaActual.TiendaId ?? throw new SinTiendaEnSesionException();
            }
            else if (entrada.State == EntityState.Modified && entrada.Property(e => e.TiendaId).IsModified)
            {
                throw new InvalidOperationException("No se puede mover un registro a otra tienda.");
            }
        }
    }
}
