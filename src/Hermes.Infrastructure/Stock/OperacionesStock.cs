using Hermes.Application;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Domain.Entities;
using Hermes.Domain.Enums;
using Hermes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hermes.Infrastructure.Stock;

public class OperacionesStock : IOperacionesStock
{
    public const int MaximoPorMovimiento = 1_000_000;
    public const int MaximoSaldo = 10_000_000;

    private readonly HermesDbContext _db;
    private readonly TimeProvider _reloj;

    public OperacionesStock(HermesDbContext db, TimeProvider reloj)
    {
        _db = db;
        _reloj = reloj;
    }

    public async Task<IReadOnlyDictionary<Guid, int>> AplicarAsync(
        TipoMovimiento tipo, IReadOnlyList<LineaMovimiento> lineas, string? nota, decimal? costoUnitario,
        bool ignorarSinControl, CancellationToken ct)
    {
        ExigirTransaccion();
        if (lineas.Count == 0) throw new ValidacionException("No hay nada para registrar.");

        // Una variante repetida en la misma operación se junta en una sola línea.
        var porVariante = lineas.GroupBy(l => l.VarianteId).ToDictionary(g => g.Key, g => g.Sum(l => (long)l.Cantidad));
        foreach (var (_, cantidad) in porVariante)
        {
            var signoOk = tipo is TipoMovimiento.Ingreso or TipoMovimiento.Devolucion ? cantidad > 0 : cantidad < 0;
            if (!signoOk || Math.Abs(cantidad) > MaximoPorMovimiento)
                throw new ValidacionException($"La cantidad tiene que estar entre 1 y {MaximoPorMovimiento:N0}.");
        }

        var ids = porVariante.Keys.Order().ToList();
        var (variantes, actuales) = await BloquearYCargarAsync(ids, ct);

        var saldos = new Dictionary<Guid, int>();
        var productosTocados = new HashSet<Producto>();
        var ahora = _reloj.GetUtcNow();

        foreach (var id in ids)
        {
            var variante = variantes[id];
            var producto = variante.Producto!;
            if (!producto.ControlaStock)
            {
                if (ignorarSinControl) continue;
                throw new ProductoSinControlDeStockException(producto.Nombre);
            }
            if (!variante.Activa)
                throw new ValidacionException($"{NombreCompleto(variante)} está desactivada: no se puede mover su stock.");

            var cantidad = (int)porVariante[id];
            var actual = actuales.GetValueOrDefault(id);
            var saldo = (long)actual + cantidad;
            if (saldo < 0) throw new StockInsuficienteException(NombreCompleto(variante), actual, -cantidad);
            if (saldo > MaximoSaldo) throw new ValidacionException($"El stock no puede pasar de {MaximoSaldo:N0}.");

            _db.Movimientos.Add(new MovimientoStock
            {
                VarianteId = id, Tipo = tipo, Cantidad = cantidad, Saldo = (int)saldo, Nota = nota,
                CostoUnitario = tipo == TipoMovimiento.Ingreso && costoUnitario > 0 ? costoUnitario : null,
                CreadoEn = ahora,
            });
            saldos[id] = (int)saldo;
            productosTocados.Add(producto);
        }

        // "El costo se actualiza solo cuando llega mercadería": el último costo pagado pasa a ser el del producto.
        if (tipo == TipoMovimiento.Ingreso && costoUnitario > 0)
            foreach (var producto in productosTocados)
            {
                producto.Costo = costoUnitario.Value;
                producto.ActualizadoEn = ahora;
            }

        return saldos;
    }

    public async Task<IReadOnlyDictionary<Guid, int>> FijarTotalesAsync(
        IReadOnlyList<(Guid VarianteId, int NuevoTotal)> lineas, string? motivo, CancellationToken ct)
    {
        ExigirTransaccion();
        if (lineas.Count == 0) throw new ValidacionException("No hay nada para registrar.");
        if (lineas.Any(l => l.NuevoTotal < 0 || l.NuevoTotal > MaximoSaldo))
            throw new ValidacionException($"El stock tiene que estar entre 0 y {MaximoSaldo:N0}.");

        var nuevos = lineas.GroupBy(l => l.VarianteId).ToDictionary(g => g.Key, g => g.Last().NuevoTotal);
        var ids = nuevos.Keys.Order().ToList();
        var (variantes, actuales) = await BloquearYCargarAsync(ids, ct);

        var saldos = new Dictionary<Guid, int>();
        var cambios = 0;
        var ahora = _reloj.GetUtcNow();

        foreach (var id in ids)
        {
            var variante = variantes[id];
            if (!variante.Producto!.ControlaStock) throw new ProductoSinControlDeStockException(variante.Producto.Nombre);
            if (!variante.Activa)
                throw new ValidacionException($"{NombreCompleto(variante)} está desactivada: no se puede mover su stock.");

            var actual = actuales.GetValueOrDefault(id);
            var diferencia = nuevos[id] - actual;
            saldos[id] = nuevos[id];
            if (diferencia == 0) continue; // ya estaba así: no se inventa un movimiento

            _db.Movimientos.Add(new MovimientoStock
            {
                VarianteId = id, Tipo = TipoMovimiento.Ajuste, Cantidad = diferencia, Saldo = nuevos[id], Nota = motivo, CreadoEn = ahora,
            });
            cambios++;
        }
        if (cambios == 0) throw new ValidacionException("No cambió ninguna cantidad.");
        return saldos;
    }

    // El candado de una variante es un UPDATE que no cambia nada: toma el candado exclusivo de la fila
    // hasta el commit, ANTES de leer el stock. Sin esto el chequeo es "leer y después insertar": 40 ventas
    // simultáneas de la última unidad pasarían todas (CMP lo midió). Se toman SIEMPRE en el mismo orden
    // (por Id) para que dos operaciones sobre las mismas variantes no se traben entre sí (deadlock).
    private async Task<(Dictionary<Guid, Variante> Variantes, Dictionary<Guid, int> Actuales)> BloquearYCargarAsync(
        List<Guid> idsOrdenados, CancellationToken ct)
    {
        foreach (var id in idsOrdenados)
            await _db.Variantes.Where(v => v.Id == id).ExecuteUpdateAsync(s => s.SetProperty(v => v.Activa, v => v.Activa), ct);

        var variantes = await _db.Variantes.Include(v => v.Producto).Where(v => idsOrdenados.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        foreach (var id in idsOrdenados)
            if (!variantes.ContainsKey(id)) throw new VarianteNoEncontradaException(id); // inexistente o de otra tienda

        // Se lee DESPUÉS del candado: nadie más puede estar sumando o restando estas variantes.
        return (variantes, await _db.PorVarianteAsync(idsOrdenados, ct));
    }

    private void ExigirTransaccion()
    {
        if (_db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "OperacionesStock se usa dentro de una transacción: el candado de la variante dura hasta el commit.");
    }

    public static string Etiqueta(Variante v) => string.Join(" · ", new[] { v.Color, v.Talla }.Where(s => s.Length > 0));

    public static string NombreCompleto(Variante v)
    {
        var etiqueta = Etiqueta(v);
        return etiqueta.Length == 0 ? v.Producto!.Nombre : $"{v.Producto!.Nombre} ({etiqueta})";
    }
}
