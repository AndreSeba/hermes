using Hermes.Application;
using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Domain;
using Hermes.Domain.Enums;
using Hermes.Infrastructure.Imagenes;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Stock;
using Microsoft.EntityFrameworkCore;

namespace Hermes.Infrastructure.Services;

// Producto, Variante y MovimientoStock son de la tienda (filtro global): nada acá filtra por tienda a
// mano. Una id de otra tienda "no existe".
public class StockService : IStockService
{
    private const int LimitePorDefecto = 24;
    private const int LimiteMaximo = 60;
    private const string MotivoPorDefecto = "Corrección de stock";

    private readonly HermesDbContext _db;
    private readonly IOperacionesStock _operaciones;
    private readonly IAlmacenArchivos _almacen;
    private readonly ITiendaActual _sesion;

    public StockService(HermesDbContext db, IOperacionesStock operaciones, IAlmacenArchivos almacen, ITiendaActual sesion)
    {
        _db = db;
        _operaciones = operaciones;
        _almacen = almacen;
        _sesion = sesion;
    }

    private Guid TiendaIdDeLaSesion() => _sesion.TiendaId ?? throw new SinTiendaEnSesionException();

    // ───────────── Lectura ─────────────

    public async Task<PaginaDeStockDto> ListarAsync(FiltroStockDto filtro, CancellationToken ct)
    {
        var limite = Math.Clamp(filtro.Limite ?? LimitePorDefecto, 1, LimiteMaximo);
        var cursor = LeerCursor(filtro.Cursor);

        var productos = _db.Productos.AsNoTracking().Where(p => !p.Archivado);
        if (!string.IsNullOrWhiteSpace(filtro.Buscar))
        {
            var texto = filtro.Buscar.Trim().ToLower();
            productos = productos.Where(p => p.Nombre.ToLower().Contains(texto));
        }

        // Prioridad = ReglaStock.Prioridad (0 agotado, 1 por agotarse, 2 bien, 3 por encargo), calculada en SQL
        // para poder ordenar y filtrar sin traer todo. Hay una prueba que compara las dos.
        var conPrioridad = productos
            .Select(p => new
            {
                p.Id,
                Total = p.Variantes.Where(v => v.Activa).Sum(v => v.Movimientos.Sum(m => m.Cantidad)),
                p.ControlaStock,
                p.StockMinimo,
            })
            .Select(x => new
            {
                x.Id,
                Prioridad = !x.ControlaStock ? 3 : x.Total <= 0 ? 0 : x.Total <= x.StockMinimo ? 1 : 2,
            });

        conPrioridad = filtro.Vista switch
        {
            VistaStock.AReponer => conPrioridad.Where(x => x.Prioridad <= 1),
            VistaStock.PorEncargo => conPrioridad.Where(x => x.Prioridad == 3),
            _ => conPrioridad,
        };
        // Paginación por cursor sobre (prioridad ↑, id ↓): "continuar después de este" sin OFFSET.
        if (cursor is { } c)
            conPrioridad = conPrioridad.Where(x => x.Prioridad > c.Prioridad || (x.Prioridad == c.Prioridad && x.Id.CompareTo(c.Id) < 0));

        var pagina = await conPrioridad.OrderBy(x => x.Prioridad).ThenByDescending(x => x.Id).Take(limite + 1).ToListAsync(ct);
        var hayMas = pagina.Count > limite;
        if (hayMas) pagina.RemoveAt(pagina.Count - 1);

        var filas = await FilasAsync(pagina.Select(x => x.Id).ToList(), ct);
        var items = pagina.Select(x => filas[x.Id]).ToList();
        var siguiente = hayMas ? $"{pagina[^1].Prioridad}.{pagina[^1].Id}" : null;
        return new PaginaDeStockDto(items, siguiente);
    }

    public async Task<ResumenStockDto> ResumenAsync(CancellationToken ct)
    {
        var filas = await _db.Productos.AsNoTracking().Where(p => !p.Archivado)
            .Select(p => new
            {
                p.ControlaStock, p.Costo, p.Precio, p.StockMinimo,
                Total = p.Variantes.Where(v => v.Activa).Sum(v => v.Movimientos.Sum(m => m.Cantidad)),
            })
            .ToListAsync(ct);

        decimal valorCosto = 0, valorVenta = 0;
        int agotados = 0, pocos = 0, sinControl = 0;
        foreach (var f in filas)
        {
            switch (ReglaStock.Estado(f.ControlaStock, f.Total, f.StockMinimo))
            {
                case EstadoStock.SinControl: sinControl++; continue;
                case EstadoStock.Agotado: agotados++; break;
                case EstadoStock.Pocos: pocos++; break;
            }
            valorCosto += f.Total * f.Costo;
            valorVenta += f.Total * f.Precio;
        }
        return new ResumenStockDto(valorCosto, valorVenta, agotados, pocos, sinControl);
    }

    public async Task<ProductoStockDto> ObtenerAsync(Guid productoId, CancellationToken ct)
    {
        TiendaIdDeLaSesion();
        var filas = await FilasAsync([productoId], ct);
        return filas.TryGetValue(productoId, out var fila) ? fila : throw new ProductoNoEncontradoException(productoId);
    }

    public async Task<PaginaDeMovimientosDto> MovimientosAsync(Guid productoId, FiltroMovimientosDto filtro, CancellationToken ct)
    {
        if (!await _db.Productos.AnyAsync(p => p.Id == productoId, ct)) throw new ProductoNoEncontradoException(productoId);
        var limite = Math.Clamp(filtro.Limite ?? 20, 1, 100);

        var consulta = _db.Movimientos.AsNoTracking().Where(m => m.Variante!.ProductoId == productoId);
        if (filtro.Cursor is { } cursor) consulta = consulta.Where(m => m.Id.CompareTo(cursor) < 0);

        // Id v7: ordenado por fecha de creación (los del mismo milisegundo son simultáneos de verdad).
        var filas = await consulta.OrderByDescending(m => m.Id).Take(limite + 1)
            .Select(m => new { m.Id, m.VarianteId, m.Variante!.Color, m.Variante.Talla, m.Tipo, m.Cantidad, m.Saldo, m.Nota, m.CostoUnitario, m.CreadoEn })
            .ToListAsync(ct);
        var hayMas = filas.Count > limite;
        if (hayMas) filas.RemoveAt(filas.Count - 1);

        var items = filas.Select(f => new MovimientoDto(
            f.Id, f.VarianteId, Nulo(f.Color), Nulo(f.Talla), f.Tipo, f.Cantidad, f.Saldo, f.Nota, f.CostoUnitario, f.CreadoEn)).ToList();
        return new PaginaDeMovimientosDto(items, hayMas ? items[^1].Id : null);
    }

    // ───────────── Escritura ─────────────

    public async Task<ProductoStockDto> IngresarAsync(IngresoDto dto, CancellationToken ct)
    {
        TiendaIdDeLaSesion();
        var costo = dto.CostoUnitario is { } c && c != 0 ? Validacion.Monto(c, "El costo", permitirCero: false) : (decimal?)null;
        var nota = Validacion.TextoOpcional(dto.Nota, 200, "La nota");
        var lineas = (dto.Lineas ?? []).Where(l => l.Cantidad != 0).ToList();
        if (lineas.Count == 0) throw new ValidacionException("Escribí cuántas llegaron.");
        if (lineas.Any(l => l.Cantidad < 0)) throw new ValidacionException("Las cantidades que llegan no pueden ser negativas. Para restar, corregí el stock.");

        await ValidarVariantesDelProductoAsync(dto.ProductoId, lineas.Select(l => l.VarianteId), ct);

        await using var transaccion = await _db.Database.BeginTransactionAsync(ct);
        await _operaciones.AplicarAsync(TipoMovimiento.Ingreso, lineas.Select(l => new LineaMovimiento(l.VarianteId, l.Cantidad)).ToList(),
            nota, costo, ignorarSinControl: false, ct);
        await _db.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);

        return (await FilasAsync([dto.ProductoId], ct))[dto.ProductoId];
    }

    public async Task<ProductoStockDto> AjustarAsync(AjusteDto dto, CancellationToken ct)
    {
        TiendaIdDeLaSesion();
        var motivo = Validacion.TextoOpcional(dto.Motivo, 200, "El motivo") ?? MotivoPorDefecto;
        var lineas = (dto.Lineas ?? []).ToList();
        if (lineas.Count == 0) throw new ValidacionException("Escribí cuántas hay.");

        await ValidarVariantesDelProductoAsync(dto.ProductoId, lineas.Select(l => l.VarianteId), ct);

        await using var transaccion = await _db.Database.BeginTransactionAsync(ct);
        await _operaciones.FijarTotalesAsync(lineas.Select(l => (l.VarianteId, l.NuevoTotal)).ToList(), motivo, ct);
        await _db.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);

        return (await FilasAsync([dto.ProductoId], ct))[dto.ProductoId];
    }

    // ───────────── Armado de filas ─────────────

    // Todas las variantes tienen que ser ACTIVAS del producto indicado: así no se mueve (por error o a
    // propósito) el stock de una variante de otro producto con la id de uno propio.
    private async Task ValidarVariantesDelProductoAsync(Guid productoId, IEnumerable<Guid> varianteIds, CancellationToken ct)
    {
        if (!await _db.Productos.AnyAsync(p => p.Id == productoId, ct)) throw new ProductoNoEncontradoException(productoId);
        var pedidas = varianteIds.Distinct().ToList();
        var delProducto = await _db.Variantes.Where(v => v.ProductoId == productoId && v.Activa && pedidas.Contains(v.Id))
            .Select(v => v.Id).ToListAsync(ct);
        var ajena = pedidas.FirstOrDefault(id => !delProducto.Contains(id), Guid.Empty);
        if (ajena != Guid.Empty) throw new VarianteNoEncontradaException(ajena);
    }

    private async Task<Dictionary<Guid, ProductoStockDto>> FilasAsync(IReadOnlyCollection<Guid> productoIds, CancellationToken ct)
    {
        var tiendaId = TiendaIdDeLaSesion();
        var productos = await _db.Productos.AsNoTracking().Where(p => productoIds.Contains(p.Id))
            .Select(p => new
            {
                p.Id, p.Nombre, p.Precio, p.Costo, p.ControlaStock, p.StockMinimo,
                PortadaId = p.Fotos.OrderBy(f => f.Orden).Select(f => (Guid?)f.ImagenId).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var variantes = await _db.Variantes.AsNoTracking()
            .Where(v => v.Activa && productoIds.Contains(v.ProductoId))
            .OrderBy(v => v.Orden).ThenBy(v => v.Id)
            .Select(v => new { v.Id, v.ProductoId, v.Color, v.Talla })
            .ToListAsync(ct);
        var stockPorVariante = await _db.PorVarianteAsync(variantes.Select(v => v.Id).ToList(), ct);

        var portadaIds = productos.Where(p => p.PortadaId != null).Select(p => p.PortadaId!.Value).ToList();
        var portadas = await _db.Imagenes.AsNoTracking().Where(i => portadaIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);

        return productos.ToDictionary(p => p.Id, p =>
        {
            var suyas = variantes.Where(v => v.ProductoId == p.Id)
                .Select(v => new VarianteStockDto(v.Id, Nulo(v.Color), Nulo(v.Talla), stockPorVariante.GetValueOrDefault(v.Id)))
                .ToList();
            var total = suyas.Sum(v => v.Stock);
            return new ProductoStockDto(
                p.Id, p.Nombre, p.Precio, p.Costo,
                p.PortadaId is { } id && portadas.TryGetValue(id, out var imagen) ? ImagenMapeo.ADto(_almacen, tiendaId, imagen) : null,
                p.ControlaStock, p.StockMinimo, p.ControlaStock ? total : null,
                ReglaStock.Estado(p.ControlaStock, total, p.StockMinimo), suyas);
        });
    }

    private static string? Nulo(string texto) => texto.Length == 0 ? null : texto;

    // "0.0194…": prioridad y id del último producto de la página anterior.
    private static (int Prioridad, Guid Id)? LeerCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        var partes = cursor.Split('.', 2);
        if (partes.Length == 2 && int.TryParse(partes[0], out var prioridad) && prioridad is >= 0 and <= 3 && Guid.TryParse(partes[1], out var id))
            return (prioridad, id);
        throw new ValidacionException("El cursor de la página no es válido.");
    }
}
