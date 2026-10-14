using Hermes.Application;
using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Domain;

namespace Hermes.Infrastructure.Services;

// El Inicio no tiene datos propios: junta lo que ya calculan Ventas, Compras, Stock y Gastos para el período pedido.
// Cada servicio exige tienda en sesión, así que acá no hace falta repetirlo.
public class InicioService : IInicioService
{
    private const int MaximoParaReponer = 5;
    private const int MaximoRecientes = 6;

    private readonly IVentaService _ventas;
    private readonly ICompraService _compras;
    private readonly IStockService _stock;
    private readonly IGastoService _gastos;

    public InicioService(IVentaService ventas, ICompraService compras, IStockService stock, IGastoService gastos)
    {
        _ventas = ventas;
        _compras = compras;
        _stock = stock;
        _gastos = gastos;
    }

    public async Task<InicioDto> ResumenAsync(FiltroPeriodoDto periodo, CancellationToken ct)
    {
        // Se piden de a uno: comparten el mismo contexto de la base (no admite consultas en paralelo).
        var ventas = await _ventas.ResumenAsync(periodo, ct);
        var gastos = await _gastos.TotalAsync(periodo.Desde, periodo.Hasta, ct);
        var compras = await _compras.ResumenAsync(ct);
        var stock = await _stock.ResumenAsync(ct);
        var reponer = await _stock.ListarAsync(new FiltroStockDto(Vista: VistaStock.AReponer, Limite: MaximoParaReponer), ct);

        var recientes = await RecientesAsync(ct);

        // "Ya cubriste el X % de los gastos": la ganancia del período contra los gastos del período.
        int? cubierto = gastos > 0 ? (int)Math.Max(0, Math.Floor(ventas.Ganancia / gastos * 100)) : null;

        return new InicioDto(
            ventas.Ventas, ventas.Unidades, ventas.Total, ventas.Ganancia, ventas.UnidadesSinCosto,
            gastos, cubierto, ventas.Ganancia - gastos,
            compras.PorPagar, compras.EnCamino,
            stock.Agotados, stock.PorAgotarse,
            reponer.Items.Select(p => new ProductoParaReponerDto(p.Id, p.Nombre, p.Estado, p.Stock ?? 0)).ToList(),
            recientes);
    }

    // Las últimas ventas, compras y gastos (no anulados) mezclados por fecha: lo que pasó últimamente, sin importar el período.
    private async Task<IReadOnlyList<ActividadDto>> RecientesAsync(CancellationToken ct)
    {
        var ventas = (await _ventas.ListarAsync(new FiltroVentasDto(Limite: MaximoRecientes), ct)).Items
            .Select(v => new ActividadDto(TipoActividad.Venta, v.Id, v.Fecha, v.Detalle, v.Total));
        var compras = (await _compras.ListarAsync(new FiltroComprasDto(Limite: MaximoRecientes), ct)).Items
            .Where(c => c.Estado != EstadoCompra.Anulada)
            .Select(c => new ActividadDto(TipoActividad.Compra, c.Id, c.Fecha, c.Proveedor, c.Total));
        var gastos = (await _gastos.ListarAsync(new FiltroGastosDto(Limite: MaximoRecientes), ct)).Items
            .Select(g => new ActividadDto(TipoActividad.Gasto, g.Id, g.Fecha, g.Concepto, g.Monto));

        return ventas.Concat(compras).Concat(gastos).OrderByDescending(a => a.Fecha).ThenByDescending(a => a.Id).Take(MaximoRecientes).ToList();
    }
}
