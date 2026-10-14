using Hermes.Application;
using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Domain;
using Hermes.Domain.Entities;
using Hermes.Domain.Enums;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Stock;
using Microsoft.EntityFrameworkCore;

namespace Hermes.Infrastructure.Services;

// Compra, CompraLinea, Proveedor, Producto y Variante son de la tienda (filtro global). Las tres transiciones
// (recibir, pagar, anular) son UPDATE condicionales dentro de una transacción: con pedidos simultáneos solo uno
// gana, así el stock no se suma ni se resta dos veces. Los totales se calculan en memoria sobre las líneas.
public class CompraService : ICompraService
{
    private const int LimitePorDefecto = 30;
    private const int LimiteMaximo = 100;
    private const int MaximoPorLinea = 100_000;

    private readonly HermesDbContext _db;
    private readonly IOperacionesStock _stock;
    private readonly ITiendaActual _sesion;
    private readonly TimeProvider _reloj;

    public CompraService(HermesDbContext db, IOperacionesStock stock, ITiendaActual sesion, TimeProvider reloj)
    {
        _db = db;
        _stock = stock;
        _sesion = sesion;
        _reloj = reloj;
    }

    private void ExigirTienda() => _ = _sesion.TiendaId ?? throw new SinTiendaEnSesionException();

    // ───────────── Registrar ─────────────

    public async Task<ResultadoCompra> RegistrarAsync(RegistrarCompraDto dto, CancellationToken ct)
    {
        ExigirTienda();
        var (pedidas, notas) = Validar(dto);

        if (await BuscarPorClaveAsync(dto.ClaveIdempotencia, ct) is { } yaRegistrada)
            return new ResultadoCompra(yaRegistrada, false);

        var proveedor = await _db.Proveedores.AsNoTracking().FirstOrDefaultAsync(p => p.Id == dto.ProveedorId, ct)
            ?? throw new ProveedorNoEncontradoException(dto.ProveedorId); // inexistente o de otra tienda
        if (!proveedor.Activo) throw new ValidacionException($"{proveedor.Nombre} está desactivado: reactivalo para pedirle.");

        var ids = pedidas.Select(l => l.VarianteId).ToList();
        var variantes = await _db.Variantes.Include(v => v.Producto).Where(v => ids.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        foreach (var l in pedidas)
        {
            if (!variantes.TryGetValue(l.VarianteId, out var v)) throw new VarianteNoEncontradaException(l.VarianteId);
            if (v.Producto!.Archivado) throw new ValidacionException($"{v.Producto.Nombre} está archivado: desarchivalo para comprarlo.");
            if (!v.Activa) throw new ValidacionException($"{OperacionesStock.NombreCompleto(v)} ya no se vende.");
        }

        var ahora = _reloj.GetUtcNow().UtcDateTime;
        var compra = new Compra
        {
            ProveedorId = proveedor.Id, Fecha = ahora, Notas = notas, ClaveIdempotencia = dto.ClaveIdempotencia,
            Pagada = dto.Pagada, PagadaEn = dto.Pagada ? ahora : null,
        };
        foreach (var l in pedidas.OrderBy(l => variantes[l.VarianteId].Orden))
        {
            var v = variantes[l.VarianteId];
            var descripcion = OperacionesStock.NombreCompleto(v);
            compra.Lineas.Add(new CompraLinea
            {
                Orden = compra.Lineas.Count, ProductoId = v.ProductoId, VarianteId = v.Id, Cantidad = l.Cantidad, CostoUnitario = l.CostoUnitario,
                Descripcion = descripcion.Length > 200 ? descripcion[..200] : descripcion,
            });
        }
        if (dto.Recibida) { compra.Recibida = true; compra.RecibidaEn = ahora; }

        IReadOnlyList<AvisoMargenDto> avisos = [];
        try
        {
            await using var transaccion = await _db.Database.BeginTransactionAsync(ct);

            // Primero se inserta (reserva la clave única): un reintento simultáneo espera acá y después choca.
            _db.Compras.Add(compra);
            await _db.SaveChangesAsync(ct);

            if (dto.Recibida) avisos = await AplicarRecepcionAsync(compra.Id, compra.Lineas, ahora, ct);
            await _db.SaveChangesAsync(ct);
            await transaccion.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            if (await BuscarPorClaveAsync(dto.ClaveIdempotencia, ct) is { } ganadora) return new ResultadoCompra(ganadora, false);
            throw;
        }
        catch
        {
            _db.ChangeTracker.Clear();
            throw;
        }

        return new ResultadoCompra(ADto(compra, proveedor), true);
    }

    private static (List<LineaCompraDto> Lineas, string? Notas) Validar(RegistrarCompraDto dto)
    {
        if (dto.ClaveIdempotencia == Guid.Empty) throw new ValidacionException("Falta la clave de la compra.");
        if (dto.ProveedorId == Guid.Empty) throw new ValidacionException("Elegí a quién le comprás.");
        var notas = Validacion.TextoOpcional(dto.Notas, 300, "Las notas");

        var lineas = (dto.Lineas ?? []).ToList();
        if (lineas.Count == 0) throw new ValidacionException("Agregá al menos un producto.");
        if (lineas.Count > Compra.MaximoLineas) throw new ValidacionException($"Una compra puede tener hasta {Compra.MaximoLineas} productos distintos.");
        // Una variante dos veces con costos distintos sería ambiguo: se pide una sola línea.
        if (lineas.GroupBy(l => l.VarianteId).Any(g => g.Count() > 1))
            throw new ValidacionException("Un producto aparece dos veces en la compra. Juntalos en una sola línea.");
        foreach (var l in lineas)
        {
            if (l.Cantidad < 1 || l.Cantidad > MaximoPorLinea)
                throw new ValidacionException($"La cantidad de cada producto tiene que estar entre 1 y {MaximoPorLinea:N0}.");
            Validacion.Monto(l.CostoUnitario, "El costo por unidad", permitirCero: false);
        }
        return (lineas, notas);
    }

    private async Task<CompraDto?> BuscarPorClaveAsync(Guid clave, CancellationToken ct)
    {
        var existente = await _db.Compras.AsNoTracking().Include(c => c.Lineas).Include(c => c.Proveedor)
            .FirstOrDefaultAsync(c => c.ClaveIdempotencia == clave, ct);
        return existente is null ? null : ADto(existente, existente.Proveedor!);
    }

    // ───────────── Recibir ─────────────

    public async Task<ResultadoRecibirDto> RecibirAsync(Guid id, CancellationToken ct)
    {
        ExigirTienda();
        IReadOnlyList<AvisoMargenDto> avisos;
        try
        {
            await using var transaccion = await _db.Database.BeginTransactionAsync(ct);
            var ahora = _reloj.GetUtcNow().UtcDateTime;

            // Una sola fila gana: si dos pedidos reciben la misma compra a la vez, el otro no suma el stock de nuevo.
            var marcadas = await _db.Compras.Where(c => c.Id == id && !c.Recibida && !c.Anulada)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Recibida, true).SetProperty(c => c.RecibidaEn, ahora), ct);
            if (marcadas == 0) throw await ExplicarRechazoAsync(id, "recibir", ct);

            var lineas = await _db.CompraLineas.Where(l => l.CompraId == id).ToListAsync(ct);
            avisos = await AplicarRecepcionAsync(id, lineas, ahora, ct);
            await _db.SaveChangesAsync(ct);
            await transaccion.CommitAsync(ct);
        }
        catch
        {
            _db.ChangeTracker.Clear();
            throw;
        }
        return new ResultadoRecibirDto(await ObtenerAsync(id, ct), avisos);
    }

    // Suma el stock, deja el costo de la compra como costo de cada producto y devuelve los márgenes flojos.
    // Va dentro de la transacción de quien llama (el candado de las variantes dura hasta el commit).
    private async Task<IReadOnlyList<AvisoMargenDto>> AplicarRecepcionAsync(
        Guid compraId, IReadOnlyList<CompraLinea> lineas, DateTime ahora, CancellationToken ct)
    {
        var productoIds = lineas.Select(l => l.ProductoId).Distinct().ToList();
        var productos = await _db.Productos.Where(p => productoIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        await _stock.AplicarAsync(TipoMovimiento.Ingreso, lineas.Select(l => new LineaMovimiento(l.VarianteId, l.Cantidad, l.CostoUnitario)).ToList(),
            "Compra", null, ignorarSinControl: true, ct, compraId: compraId);

        foreach (var l in lineas)
        {
            var producto = productos[l.ProductoId];
            l.SumoStock = producto.ControlaStock;
            // "El costo se actualiza cuando llega mercadería": el de la compra pasa a ser el del producto.
            producto.Costo = l.CostoUnitario;
            producto.ActualizadoEn = _reloj.GetUtcNow();
        }

        return productos.Values
            .Select(p => (Producto: p, Estado: MargenProducto.Estado(p.Precio, p.Costo)))
            .Where(x => x.Estado is EstadoPrecio.Pierde or EstadoPrecio.Bajo)
            .OrderBy(x => x.Producto.Nombre)
            .Select(x => new AvisoMargenDto(x.Producto.Id, x.Producto.Nombre, x.Producto.Precio, x.Producto.Costo, x.Estado))
            .ToList();
    }

    // ───────────── Pagar ─────────────

    public async Task<CompraDto> PagarAsync(Guid id, CancellationToken ct)
    {
        ExigirTienda();
        var ahora = _reloj.GetUtcNow().UtcDateTime;
        var marcadas = await _db.Compras.Where(c => c.Id == id && !c.Pagada && !c.Anulada)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Pagada, true).SetProperty(c => c.PagadaEn, ahora), ct);
        if (marcadas == 0) throw await ExplicarRechazoAsync(id, "pagar", ct);
        return await ObtenerAsync(id, ct);
    }

    // ───────────── Anular ─────────────

    public async Task<CompraDto> AnularAsync(Guid id, CancellationToken ct)
    {
        ExigirTienda();
        try
        {
            await using var transaccion = await _db.Database.BeginTransactionAsync(ct);
            var ahora = _reloj.GetUtcNow().UtcDateTime;

            // Marcarla anulada TOMA el candado de la fila: una recepción simultánea espera y después ya no puede
            // (su UPDATE exige !Anulada). Recién después se lee si estaba recibida.
            var marcadas = await _db.Compras.Where(c => c.Id == id && !c.Anulada)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Anulada, true).SetProperty(c => c.AnuladaEn, ahora), ct);
            if (marcadas == 0) throw await ExplicarRechazoAsync(id, "anular", ct);

            var recibida = await _db.Compras.AsNoTracking().Where(c => c.Id == id).Select(c => c.Recibida).FirstAsync(ct);
            if (recibida)
            {
                // Resta solo lo que sumó (SumoStock). Si esas unidades ya se vendieron: 409 y no se anula nada.
                var restar = await _db.CompraLineas.AsNoTracking().Where(l => l.CompraId == id && l.SumoStock)
                    .Select(l => new { l.VarianteId, l.Cantidad }).ToListAsync(ct);
                if (restar.Count > 0)
                {
                    await _stock.AplicarAsync(TipoMovimiento.Reversion,
                        restar.GroupBy(l => l.VarianteId).Select(g => new LineaMovimiento(g.Key, -g.Sum(l => l.Cantidad))).ToList(),
                        "Compra anulada", null, ignorarSinControl: true, ct, compraId: id);
                    await _db.SaveChangesAsync(ct);
                }
            }
            await transaccion.CommitAsync(ct);
        }
        catch
        {
            _db.ChangeTracker.Clear();
            throw;
        }
        return await ObtenerAsync(id, ct);
    }

    // Cuando un UPDATE condicional no tocó nada: ¿no existe, o ya estaba en ese estado?
    private async Task<DominioException> ExplicarRechazoAsync(Guid id, string accion, CancellationToken ct)
    {
        var c = await _db.Compras.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Anulada, x.Recibida, x.Pagada }).FirstOrDefaultAsync(ct);
        if (c is null) return new CompraNoEncontradaException(id);
        if (c.Anulada) return new CompraEstadoInvalidoException(accion == "anular" ? "Esa compra ya estaba anulada." : "Esa compra está anulada: no se puede " + accion + ".");
        if (accion == "recibir" && c.Recibida) return new CompraEstadoInvalidoException("Esa compra ya estaba recibida.");
        if (accion == "pagar" && c.Pagada) return new CompraEstadoInvalidoException("Esa compra ya estaba pagada.");
        return new CompraEstadoInvalidoException($"No se pudo {accion} la compra.");
    }

    // ───────────── Lectura ─────────────

    public async Task<CompraDto> ObtenerAsync(Guid id, CancellationToken ct)
    {
        ExigirTienda();
        var compra = await _db.Compras.AsNoTracking().Include(c => c.Lineas).Include(c => c.Proveedor).FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new CompraNoEncontradaException(id);
        return ADto(compra, compra.Proveedor!);
    }

    public async Task<PaginaDeComprasDto> ListarAsync(FiltroComprasDto filtro, CancellationToken ct)
    {
        ExigirTienda();
        if (!Enum.IsDefined(filtro.Vista)) throw new ValidacionException("La vista pedida no existe.");
        var limite = Math.Clamp(filtro.Limite ?? LimitePorDefecto, 1, LimiteMaximo);

        var consulta = _db.Compras.AsNoTracking().AsQueryable();
        consulta = filtro.Vista switch
        {
            VistaCompras.EnCamino => consulta.Where(c => !c.Anulada && !c.Recibida),
            VistaCompras.PorPagar => consulta.Where(c => !c.Anulada && !c.Pagada),
            _ => consulta,
        };
        if (filtro.ProveedorId is { } proveedorId) consulta = consulta.Where(c => c.ProveedorId == proveedorId);
        if (filtro.Cursor is { } cursor) consulta = consulta.Where(c => c.Id.CompareTo(cursor) < 0);

        var pagina = await consulta.OrderByDescending(c => c.Id).Take(limite + 1).Include(c => c.Lineas).Include(c => c.Proveedor).ToListAsync(ct);
        var hayMas = pagina.Count > limite;
        if (hayMas) pagina.RemoveAt(pagina.Count - 1);

        var items = pagina.Select(ADtoResumen).ToList();
        return new PaginaDeComprasDto(items, hayMas ? items[^1].Id : null);
    }

    public async Task<ResumenComprasDto> ResumenAsync(CancellationToken ct)
    {
        ExigirTienda();
        var filas = await _db.CompraLineas.AsNoTracking().Where(l => !l.Compra!.Anulada)
            .Select(l => new { l.CompraId, l.Compra!.Recibida, l.Compra.Pagada, l.Cantidad, l.CostoUnitario }).ToListAsync(ct);
        return new ResumenComprasDto(
            filas.Where(f => !f.Pagada).Sum(f => f.Cantidad * f.CostoUnitario),
            filas.Where(f => !f.Pagada).Select(f => f.CompraId).Distinct().Count(),
            filas.Where(f => !f.Recibida).Select(f => f.CompraId).Distinct().Count(),
            filas.Where(f => !f.Recibida).Sum(f => f.Cantidad * f.CostoUnitario));
    }

    public async Task<IReadOnlyList<PrecioProveedorDto>> CompararAsync(Guid productoId, CancellationToken ct)
    {
        ExigirTienda();
        if (!await _db.Productos.AnyAsync(p => p.Id == productoId, ct)) throw new ProductoNoEncontradoException(productoId);

        var lineas = await _db.CompraLineas.AsNoTracking()
            .Where(l => l.ProductoId == productoId && !l.Compra!.Anulada)
            .Select(l => new { l.Compra!.ProveedorId, Proveedor = l.Compra.Proveedor!.Nombre, l.Compra.Proveedor.Telefono, l.CompraId, l.Compra.Fecha, l.CostoUnitario })
            .ToListAsync(ct);
        var ultimos = lineas.GroupBy(l => l.ProveedorId).Select(g => g.OrderByDescending(l => l.CompraId).First()).ToList();
        if (ultimos.Count == 0) return [];

        var minimo = ultimos.Min(u => u.CostoUnitario);
        return ultimos.OrderBy(u => u.CostoUnitario).ThenBy(u => u.Proveedor)
            .Select(u => new PrecioProveedorDto(u.ProveedorId, u.Proveedor, u.Telefono, u.CostoUnitario, Utc(u.Fecha), u.CostoUnitario == minimo)).ToList();
    }

    // ───────────── DTOs ─────────────

    private static DateTimeOffset Utc(DateTime d) => new(DateTime.SpecifyKind(d, DateTimeKind.Utc));

    private static EstadoCompra EstadoDe(Compra c) => c.Anulada ? EstadoCompra.Anulada : c.Recibida ? EstadoCompra.Recibida : EstadoCompra.EnCamino;

    private static CompraDto ADto(Compra c, Proveedor p)
    {
        var lineas = c.Lineas.OrderBy(l => l.Orden)
            .Select(l => new CompraLineaDto(l.Id, l.ProductoId, l.VarianteId, l.Descripcion, l.Cantidad, l.CostoUnitario, l.Cantidad * l.CostoUnitario)).ToList();
        return new CompraDto(c.Id, Utc(c.Fecha), p.Id, p.Nombre, p.Telefono, EstadoDe(c), c.Pagada,
            c.RecibidaEn is { } r ? Utc(r) : null, c.PagadaEn is { } pa ? Utc(pa) : null, c.AnuladaEn is { } a ? Utc(a) : null,
            c.Notas, lineas.Sum(l => l.Cantidad), lineas.Sum(l => l.Subtotal), lineas);
    }

    private static CompraResumenDto ADtoResumen(Compra c)
    {
        var primeras = c.Lineas.OrderBy(l => l.Orden).Take(2).Select(l => $"{l.Cantidad} {l.Descripcion}").ToList();
        var detalle = string.Join(" · ", primeras) + (c.Lineas.Count > 2 ? $" · y {c.Lineas.Count - 2} más" : "");
        return new CompraResumenDto(c.Id, Utc(c.Fecha), c.ProveedorId, c.Proveedor!.Nombre, EstadoDe(c), c.Pagada,
            c.Lineas.Sum(l => l.Cantidad), c.Lineas.Sum(l => l.Cantidad * l.CostoUnitario), detalle);
    }
}
