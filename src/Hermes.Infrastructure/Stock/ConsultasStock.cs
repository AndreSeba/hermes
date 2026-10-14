using Hermes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hermes.Infrastructure.Stock;

// El stock NO se guarda en ningún lado: se calcula acá, sumando movimientos. (Regla de CMP: evita
// la clase de bug del número cacheado que se desincroniza.) Todas las consultas de la tienda de la
// sesión: Movimiento y Variante tienen el filtro global.
public static class ConsultasStock
{
    // variante → stock. Las variantes sin movimientos no aparecen: stock 0 (usar GetValueOrDefault).
    public static async Task<Dictionary<Guid, int>> PorVarianteAsync(
        this HermesDbContext db, IReadOnlyCollection<Guid> varianteIds, CancellationToken ct)
        => await db.Movimientos.AsNoTracking()
            .Where(m => varianteIds.Contains(m.VarianteId))
            .GroupBy(m => m.VarianteId)
            .Select(g => new { g.Key, Total = g.Sum(m => m.Cantidad) })
            .ToDictionaryAsync(x => x.Key, x => x.Total, ct);

    // producto → stock total, solo de sus variantes ACTIVAS. Los productos sin movimientos no aparecen.
    public static async Task<Dictionary<Guid, int>> PorProductoAsync(
        this HermesDbContext db, IReadOnlyCollection<Guid> productoIds, CancellationToken ct)
        => await db.Movimientos.AsNoTracking()
            .Where(m => m.Variante!.Activa && productoIds.Contains(m.Variante.ProductoId))
            .GroupBy(m => m.Variante!.ProductoId)
            .Select(g => new { g.Key, Total = g.Sum(m => m.Cantidad) })
            .ToDictionaryAsync(x => x.Key, x => x.Total, ct);
}
