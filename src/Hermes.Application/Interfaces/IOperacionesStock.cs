using Hermes.Domain.Enums;

namespace Hermes.Application.Interfaces;

public record LineaMovimiento(Guid VarianteId, int Cantidad);

// Cambiar el stock. Lo usan Stock (llegadas y correcciones) y Ventas (M6).
//
// NO guarda ni hace commit: AGREGA los movimientos al contexto y bloquea las variantes. Quien llama tiene
// que abrir una transacción ANTES (el candado dura hasta el commit; sin transacción se libera al instante
// y dos ventas simultáneas podrían dejar el stock negativo), y hacer SaveChanges + commit después. Así una
// venta y sus descuentos de stock se guardan juntos o no se guarda nada.
public interface IOperacionesStock
{
    // Suma/resta cantidades con signo (Ingreso/Devolucion: positivas; Venta: negativas).
    // `ignorarSinControl`: las variantes de productos sin control de stock se saltean en vez de fallar
    // (lo que quiere una venta que mezcla productos con y sin control).
    // Devuelve el saldo que quedó de cada variante tocada.
    Task<IReadOnlyDictionary<Guid, int>> AplicarAsync(
        TipoMovimiento tipo, IReadOnlyList<LineaMovimiento> lineas, string? nota, decimal? costoUnitario,
        bool ignorarSinControl, CancellationToken ct);

    // Deja cada variante en el total indicado (Ajuste): calcula la diferencia dentro del candado.
    Task<IReadOnlyDictionary<Guid, int>> FijarTotalesAsync(
        IReadOnlyList<(Guid VarianteId, int NuevoTotal)> lineas, string? motivo, CancellationToken ct);
}
