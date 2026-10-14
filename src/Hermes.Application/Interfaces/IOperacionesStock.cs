using Hermes.Domain.Enums;

namespace Hermes.Application.Interfaces;

// CostoUnitario: lo que costó cada unidad de ESTA línea (compras: cada línea tiene su costo). Si no viene, se usa
// el `costoUnitario` general de AplicarAsync. Solo queda en el movimiento: no cambia el costo del producto.
public record LineaMovimiento(Guid VarianteId, int Cantidad, decimal? CostoUnitario = null);

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
    // `ventaId` / `compraId`: lo que lo originó (queda en el movimiento, para el historial).
    // Una Devolucion (venta anulada) y una Reversion (compra anulada) se permiten también sobre variantes que ya
    // se desactivaron: deshacer tiene que poder deshacer. Reversion resta (cantidades negativas).
    // Devuelve el saldo que quedó de cada variante tocada.
    Task<IReadOnlyDictionary<Guid, int>> AplicarAsync(
        TipoMovimiento tipo, IReadOnlyList<LineaMovimiento> lineas, string? nota, decimal? costoUnitario,
        bool ignorarSinControl, CancellationToken ct, Guid? ventaId = null, Guid? compraId = null);

    // Deja cada variante en el total indicado (Ajuste): calcula la diferencia dentro del candado.
    Task<IReadOnlyDictionary<Guid, int>> FijarTotalesAsync(
        IReadOnlyList<(Guid VarianteId, int NuevoTotal)> lineas, string? motivo, CancellationToken ct);
}
