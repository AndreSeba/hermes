using Hermes.Domain.Entities;
using Hermes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hermes.Infrastructure.Planes;

// Qué plan tiene una tienda AHORA y cuánto usa. Es lo único que decide eso: ProductoService, ImagenService, PlanService y el
// panel del administrador lo comparten. Siempre recibe el Id de la tienda (explícito) e ignora el filtro global: el
// administrador mira tiendas que no son la suya, y para la dueña el Id sale de su sesión.
public class PlanesDeTienda
{
    private readonly HermesDbContext _db;
    private readonly TimeProvider _reloj;
    private readonly PlanesOptions _opciones;

    public PlanesDeTienda(HermesDbContext db, TimeProvider reloj, IOptions<PlanesOptions> opciones)
    {
        _db = db;
        _reloj = reloj;
        _opciones = opciones.Value;
    }

    public DateTime Ahora => _reloj.GetUtcNow().UtcDateTime;

    // La suscripción vigente de más reciente comienzo; sin ninguna, el plan por defecto de la configuración.
    public async Task<(Plan Plan, Suscripcion? Suscripcion)> VigenteAsync(Guid tiendaId, CancellationToken ct)
    {
        var ahora = Ahora;
        var s = await _db.Suscripciones.AsNoTracking().Include(x => x.Plan)
            .Where(x => x.TiendaId == tiendaId && x.Desde <= ahora && (x.Hasta == null || x.Hasta > ahora))
            .OrderByDescending(x => x.Desde).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        if (s is not null) return (s.Plan!, s);

        var codigo = string.IsNullOrWhiteSpace(_opciones.PlanSinSuscripcion) ? Plan.CodigoGratis : _opciones.PlanSinSuscripcion;
        var porDefecto = await _db.Planes.AsNoTracking().FirstOrDefaultAsync(p => p.Codigo == codigo, ct)
            ?? await _db.Planes.AsNoTracking().FirstAsync(p => p.Codigo == Plan.CodigoGratis, ct);
        return (porDefecto, null);
    }

    public record Uso(int Productos, long Bytes);

    // Productos (no archivados) y espacio de fotos de varias tiendas a la vez.
    public async Task<Dictionary<Guid, Uso>> UsoAsync(IReadOnlyCollection<Guid> tiendaIds, CancellationToken ct)
    {
        var productos = await _db.Productos.IgnoreQueryFilters().AsNoTracking()
            .Where(p => tiendaIds.Contains(p.TiendaId) && !p.Archivado)
            .GroupBy(p => p.TiendaId).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N, ct);
        var bytes = await _db.Imagenes.IgnoreQueryFilters().AsNoTracking()
            .Where(i => tiendaIds.Contains(i.TiendaId))
            .GroupBy(i => i.TiendaId).Select(g => new { Id = g.Key, B = g.Sum(i => (long)i.BytesGuardados) }).ToDictionaryAsync(x => x.Id, x => x.B, ct);
        return tiendaIds.ToDictionary(id => id, id => new Uso(productos.GetValueOrDefault(id), bytes.GetValueOrDefault(id)));
    }

    public static decimal AMb(long bytes) => Math.Round(bytes / 1_048_576m, 1);
}
