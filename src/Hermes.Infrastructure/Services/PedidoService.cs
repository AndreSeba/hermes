using Hermes.Application;
using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Domain.Entities;
using Hermes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hermes.Infrastructure.Services;

// Los pedidos del carrito de la tienda pública, vistos por la dueña. Pedido y PedidoLinea son de la tienda (filtro global):
// nada acá filtra por tienda a mano. Cada cambio de estado es un UPDATE condicional: dos toques a la vez → uno gana.
public class PedidoService : IPedidoService
{
    private const int LimitePorDefecto = 30;
    private const int LimiteMaximo = 100;

    private readonly HermesDbContext _db;
    private readonly IVentaService _ventas;
    private readonly ITiendaActual _sesion;
    private readonly TimeProvider _reloj;

    public PedidoService(HermesDbContext db, IVentaService ventas, ITiendaActual sesion, TimeProvider reloj)
    {
        _db = db;
        _ventas = ventas;
        _sesion = sesion;
        _reloj = reloj;
    }

    private Guid TiendaIdDeLaSesion() => _sesion.TiendaId ?? throw new SinTiendaEnSesionException();
    private DateTime Ahora => _reloj.GetUtcNow().UtcDateTime;

    // ───────────── Lectura ─────────────

    public async Task<PaginaDePedidosDto> ListarAsync(FiltroPedidosDto filtro, CancellationToken ct)
    {
        TiendaIdDeLaSesion();
        var limite = Math.Clamp(filtro.Limite ?? LimitePorDefecto, 1, LimiteMaximo);

        var consulta = _db.Pedidos.AsNoTracking().AsQueryable();
        consulta = filtro.Vista switch
        {
            VistaPedidos.Pendientes => consulta.Where(p => p.Estado == EstadoPedido.Nuevo || p.Estado == EstadoPedido.Confirmado),
            VistaPedidos.Entregados => consulta.Where(p => p.Estado == EstadoPedido.Entregado),
            VistaPedidos.Cancelados => consulta.Where(p => p.Estado == EstadoPedido.Cancelado),
            VistaPedidos.Todos => consulta,
            _ => throw new ValidacionException("Vista de pedidos desconocida."),
        };
        // Más nuevos primero; Guid v7 = orden de llegada: cursor sin OFFSET.
        if (filtro.Cursor is { } cursor) consulta = consulta.Where(p => p.Id.CompareTo(cursor) < 0);

        var pagina = await consulta.OrderByDescending(p => p.Id).Take(limite + 1).Include(p => p.Lineas).ToListAsync(ct);
        var hayMas = pagina.Count > limite;
        if (hayMas) pagina.RemoveAt(pagina.Count - 1);

        var items = pagina.Select(ADtoResumen).ToList();
        return new PaginaDePedidosDto(items, hayMas ? items[^1].Id : null);
    }

    public async Task<ResumenPedidosDto> ResumenAsync(CancellationToken ct)
    {
        TiendaIdDeLaSesion();
        var porEstado = await _db.Pedidos.AsNoTracking()
            .Where(p => p.Estado == EstadoPedido.Nuevo || p.Estado == EstadoPedido.Confirmado)
            .GroupBy(p => p.Estado).Select(g => new { Estado = g.Key, N = g.Count() }).ToListAsync(ct);
        return new ResumenPedidosDto(
            porEstado.FirstOrDefault(x => x.Estado == EstadoPedido.Nuevo)?.N ?? 0,
            porEstado.FirstOrDefault(x => x.Estado == EstadoPedido.Confirmado)?.N ?? 0);
    }

    public async Task<PedidoDto> ObtenerAsync(Guid id, CancellationToken ct)
    {
        TiendaIdDeLaSesion();
        return await ADtoAsync(await CargarAsync(id, ct), ct);
    }

    // ───────────── Cambios de estado ─────────────

    public async Task<PedidoDto> ConfirmarAsync(Guid id, CancellationToken ct)
    {
        TiendaIdDeLaSesion();
        var pedido = await CargarAsync(id, ct);
        var cambiados = await _db.Pedidos.Where(p => p.Id == id && p.Estado == EstadoPedido.Nuevo)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Estado, EstadoPedido.Confirmado), ct);
        if (cambiados == 0) throw YaResuelto(await EstadoAsync(id, ct) ?? pedido.Estado);
        return await ObtenerAsync(id, ct);
    }

    public async Task<PedidoDto> CancelarAsync(Guid id, CancelarPedidoDto dto, CancellationToken ct)
    {
        TiendaIdDeLaSesion();
        await CargarAsync(id, ct);
        var motivo = Validacion.TextoOpcional(dto.Motivo, 300, "El motivo");
        var ahora = Ahora;
        var cambiados = await _db.Pedidos
            .Where(p => p.Id == id && (p.Estado == EstadoPedido.Nuevo || p.Estado == EstadoPedido.Confirmado))
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Estado, EstadoPedido.Cancelado)
                .SetProperty(p => p.MotivoCancelacion, motivo)
                .SetProperty(p => p.ResueltoEn, ahora), ct);
        if (cambiados == 0) throw YaResuelto((await EstadoAsync(id, ct))!.Value);
        return await ObtenerAsync(id, ct);
    }

    // La venta se registra con IVentaService (stock con candado, todo o nada) usando el Id del pedido como clave de
    // idempotencia: si se corta la conexión y se reintenta, devuelve la MISMA venta y no descuenta dos veces. Recién
    // después se marca Entregado. Si en el medio alguien lo canceló, la venta recién creada se anula (devuelve el stock).
    // La venta se cobra al precio ACTUAL del producto (las ventas no tienen precio editable): si cambió desde el pedido,
    // el panel lo avisa antes de entregar.
    public async Task<PedidoDto> EntregarAsync(Guid id, EntregarPedidoDto dto, CancellationToken ct)
    {
        TiendaIdDeLaSesion();
        var pedido = await CargarAsync(id, ct);
        if (pedido.Estado is EstadoPedido.Entregado or EstadoPedido.Cancelado) throw YaResuelto(pedido.Estado);

        var lineas = pedido.Lineas.OrderBy(l => l.Orden).Select(l => new LineaVentaDto(l.VarianteId, l.Cantidad)).ToList();
        var venta = (await _ventas.RegistrarAsync(new RegistrarVentaDto(pedido.Id, lineas, dto.MetodoPago, pedido.ClienteNombre), ct)).Venta;

        var ahora = Ahora;
        var cambiados = await _db.Pedidos
            .Where(p => p.Id == id && (p.Estado == EstadoPedido.Nuevo || p.Estado == EstadoPedido.Confirmado))
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Estado, EstadoPedido.Entregado)
                .SetProperty(p => p.VentaId, venta.Id)
                .SetProperty(p => p.ResueltoEn, ahora), ct);
        if (cambiados == 0)
        {
            var actual = await _db.Pedidos.AsNoTracking().FirstAsync(p => p.Id == id, ct);
            if (actual.VentaId == venta.Id) return await ObtenerAsync(id, ct); // otro pedido simultáneo ya lo entregó con esta misma venta
            if (!venta.Anulada) await _ventas.AnularAsync(venta.Id, ct);
            throw YaResuelto(actual.Estado);
        }
        return await ObtenerAsync(id, ct);
    }

    // ───────────── Interno ─────────────

    private async Task<Pedido> CargarAsync(Guid id, CancellationToken ct)
        => await _db.Pedidos.AsNoTracking().Include(p => p.Lineas).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new PedidoNoEncontradoException();

    private async Task<EstadoPedido?> EstadoAsync(Guid id, CancellationToken ct)
        => await _db.Pedidos.AsNoTracking().Where(p => p.Id == id).Select(p => (EstadoPedido?)p.Estado).FirstOrDefaultAsync(ct);

    private static PedidoYaResueltoException YaResuelto(EstadoPedido estado) => new(estado switch
    {
        EstadoPedido.Confirmado => "confirmado",
        EstadoPedido.Entregado => "entregado",
        EstadoPedido.Cancelado => "cancelado",
        _ => "sin confirmar",
    });

    private static DateTimeOffset Utc(DateTime d) => new(DateTime.SpecifyKind(d, DateTimeKind.Utc));

    private async Task<PedidoDto> ADtoAsync(Pedido p, CancellationToken ct)
    {
        var productoIds = p.Lineas.Select(l => l.ProductoId).Distinct().ToList();
        var precios = await _db.Productos.AsNoTracking().Where(x => productoIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Precio, ct);
        var lineas = p.Lineas.OrderBy(l => l.Orden)
            .Select(l => new PedidoLineaDto(l.VarianteId, l.ProductoId, l.Descripcion, l.Cantidad, l.PrecioUnitario, l.PrecioUnitario * l.Cantidad,
                precios.GetValueOrDefault(l.ProductoId, l.PrecioUnitario))).ToList();
        return new PedidoDto(
            p.Id, p.Numero, Utc(p.Fecha), p.Estado, p.ClienteNombre, p.ClienteCelular, p.Entrega, p.Direccion, p.Referencia, p.Nota,
            lineas.Sum(l => l.Subtotal), lineas.Sum(l => l.Cantidad), lineas, p.VentaId, p.MotivoCancelacion,
            p.ResueltoEn is { } r ? Utc(r) : null);
    }

    private static PedidoResumenDto ADtoResumen(Pedido p)
    {
        var lineas = p.Lineas.OrderBy(l => l.Orden).ToList();
        var detalle = string.Join(" · ", lineas.Take(2).Select(l => $"{l.Cantidad} {l.Descripcion}"))
            + (lineas.Count > 2 ? $" · y {lineas.Count - 2} más" : "");
        return new PedidoResumenDto(
            p.Id, p.Numero, Utc(p.Fecha), p.Estado, p.ClienteNombre, p.Entrega,
            lineas.Sum(l => l.PrecioUnitario * l.Cantidad), lineas.Sum(l => l.Cantidad), detalle);
    }
}
