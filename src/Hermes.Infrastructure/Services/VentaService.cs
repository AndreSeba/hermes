using Hermes.Application;
using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Domain;
using Hermes.Domain.Entities;
using Hermes.Domain.Enums;
using Hermes.Domain.Seguridad;
using Hermes.Infrastructure.Imagenes;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Stock;
using Microsoft.EntityFrameworkCore;

namespace Hermes.Infrastructure.Services;

// Venta, VentaLinea, Producto, Variante y MovimientoStock son de la tienda (filtro global): nada acá filtra por
// tienda a mano. Los totales NO se suman en SQL (EF con SQLite no suma decimales): se calculan en memoria sobre las
// líneas de la página o del período pedido.
public class VentaService : IVentaService
{
    private const int LimitePorDefecto = 30;
    private const int LimiteMaximo = 100;
    private const int MaximoPorLinea = 100_000;

    private readonly HermesDbContext _db;
    private readonly IOperacionesStock _stock;
    private readonly IAlmacenArchivos _almacen;
    private readonly ITiendaActual _sesion;
    private readonly TimeProvider _reloj;
    private readonly IUsuarioActual? _usuario;

    public VentaService(HermesDbContext db, IOperacionesStock stock, IAlmacenArchivos almacen, ITiendaActual sesion, TimeProvider reloj,
        IUsuarioActual? usuario = null)
    {
        _db = db;
        _stock = stock;
        _almacen = almacen;
        _sesion = sesion;
        _reloj = reloj;
        _usuario = usuario;
    }

    private Guid TiendaIdDeLaSesion() => _sesion.TiendaId ?? throw new SinTiendaEnSesionException();

    // Quien no puede ver todas las ventas (ni el Inicio con los números del negocio) ve SOLO las que registró él.
    private Guid? SoloLasDe =>
        _usuario is { UsuarioId: { } id } u && !u.Tiene(Permisos.VentasVer) && !u.Tiene(Permisos.InicioVer) ? id : null;

    private IQueryable<Venta> VentasVisibles()
    {
        var consulta = _db.Ventas.AsNoTracking().AsQueryable();
        return SoloLasDe is { } id ? consulta.Where(v => v.UsuarioId == id) : consulta;
    }

    private IQueryable<VentaLinea> LineasVisibles()
    {
        var consulta = _db.VentaLineas.AsNoTracking().Where(l => !l.Venta!.Anulada);
        return SoloLasDe is { } id ? consulta.Where(l => l.Venta!.UsuarioId == id) : consulta;
    }

    // Los nombres de quienes registraron las ventas (Usuario no es de la tienda: se pide solo por los Id que aparecen).
    private async Task<Dictionary<Guid, string>> NombresAsync(IEnumerable<Guid?> usuarioIds, CancellationToken ct)
    {
        var ids = usuarioIds.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        return ids.Count == 0 ? [] : await _db.Usuarios.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nombre, ct);
    }

    // ───────────── Registrar ─────────────

    public async Task<ResultadoVenta> RegistrarAsync(RegistrarVentaDto dto, CancellationToken ct)
    {
        TiendaIdDeLaSesion();
        var (pedidas, cliente) = Validar(dto);

        // ¿Ya se registró con esta clave? (doble toque, reintento por mala conexión): se devuelve la misma.
        if (await BuscarPorClaveAsync(dto.ClaveIdempotencia, ct) is { } yaRegistrada)
            return new ResultadoVenta(yaRegistrada, false);

        var ids = pedidas.Select(l => l.VarianteId).ToList();
        var variantes = await _db.Variantes.Include(v => v.Producto).Where(v => ids.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        foreach (var l in pedidas)
        {
            if (!variantes.TryGetValue(l.VarianteId, out var v)) throw new VarianteNoEncontradaException(l.VarianteId); // inexistente o de otra tienda
            if (v.Producto!.Archivado) throw new ValidacionException($"{v.Producto.Nombre} está archivado: no se puede vender.");
            if (!v.Activa) throw new ValidacionException($"{OperacionesStock.NombreCompleto(v)} ya no se vende.");
        }

        var venta = new Venta
        {
            Fecha = _reloj.GetUtcNow().UtcDateTime,
            MetodoPago = dto.MetodoPago,
            Cliente = cliente,
            ClaveIdempotencia = dto.ClaveIdempotencia,
            UsuarioId = _usuario?.UsuarioId,
        };
        foreach (var l in pedidas.OrderBy(l => variantes[l.VarianteId].Orden))
        {
            var v = variantes[l.VarianteId];
            var descripcion = OperacionesStock.NombreCompleto(v);
            venta.Lineas.Add(new VentaLinea
            {
                ProductoId = v.ProductoId, VarianteId = v.Id,
                Descripcion = descripcion.Length > 200 ? descripcion[..200] : descripcion,
                Cantidad = l.Cantidad,
                // La foto del momento: precio y costo de HOY. Cambiarlos después no toca lo ya vendido.
                PrecioUnitario = v.Producto!.Precio,
                CostoUnitario = v.Producto.Costo,
                DescontoStock = v.Producto.ControlaStock,
            });
        }

        try
        {
            // Todo o nada: la venta y el descuento de stock se guardan juntos o no se guarda NADA.
            await using var transaccion = await _db.Database.BeginTransactionAsync(ct);

            // Primero se inserta la venta (con su clave única): si OTRA petición con la misma clave está en vuelo, la
            // segunda se queda esperando acá hasta que la primera termine y después choca contra el índice único.
            _db.Ventas.Add(venta);
            await _db.SaveChangesAsync(ct);

            await _stock.AplicarAsync(TipoMovimiento.Venta, pedidas.Select(l => new LineaMovimiento(l.VarianteId, -l.Cantidad)).ToList(),
                "Venta", null, ignorarSinControl: true, ct, venta.Id);
            await _db.SaveChangesAsync(ct);
            await transaccion.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            // ¿Fue la carrera por la misma clave? Entonces la otra petición ya la registró: se devuelve esa.
            if (await BuscarPorClaveAsync(dto.ClaveIdempotencia, ct) is { } ganadora) return new ResultadoVenta(ganadora, false);
            throw;
        }
        catch
        {
            _db.ChangeTracker.Clear(); // lo que quedó a medias no se guarda por error si se reusa el contexto
            throw;
        }

        return new ResultadoVenta(ADto(venta, await NombresAsync([venta.UsuarioId], ct)), true);
    }

    private (List<LineaVentaDto> Lineas, string? Cliente) Validar(RegistrarVentaDto dto)
    {
        if (dto.ClaveIdempotencia == Guid.Empty) throw new ValidacionException("Falta la clave de la venta.");
        if (!Enum.IsDefined(dto.MetodoPago)) throw new ValidacionException("Elegí cómo te pagó.");
        var cliente = Validacion.TextoOpcional(dto.Cliente, 120, "El cliente");

        // Una variante repetida se junta en una sola línea.
        var lineas = (dto.Lineas ?? []).GroupBy(l => l.VarianteId)
            .Select(g => new LineaVentaDto(g.Key, (int)Math.Min(g.Sum(l => (long)l.Cantidad), int.MaxValue))).ToList();
        if (lineas.Count == 0) throw new ValidacionException("Agregá al menos un producto.");
        if (lineas.Count > Venta.MaximoLineas) throw new ValidacionException($"Una venta puede tener hasta {Venta.MaximoLineas} productos distintos.");
        if (lineas.Any(l => l.Cantidad < 1 || l.Cantidad > MaximoPorLinea))
            throw new ValidacionException($"La cantidad de cada producto tiene que estar entre 1 y {MaximoPorLinea:N0}.");
        return (lineas, cliente);
    }

    private async Task<VentaDto?> BuscarPorClaveAsync(Guid clave, CancellationToken ct)
    {
        var existente = await _db.Ventas.AsNoTracking().Include(v => v.Lineas).FirstOrDefaultAsync(v => v.ClaveIdempotencia == clave, ct);
        return existente is null ? null : ADto(existente, await NombresAsync([existente.UsuarioId], ct));
    }

    // ───────────── Lectura ─────────────

    public async Task<VentaDto> ObtenerAsync(Guid id, CancellationToken ct)
    {
        TiendaIdDeLaSesion();
        var venta = await VentasVisibles().Include(v => v.Lineas).FirstOrDefaultAsync(v => v.Id == id, ct)
            ?? throw new VentaNoEncontradaException(id);
        return ADto(venta, await NombresAsync([venta.UsuarioId], ct));
    }

    public async Task<PaginaDeVentasDto> ListarAsync(FiltroVentasDto filtro, CancellationToken ct)
    {
        TiendaIdDeLaSesion();
        var limite = Math.Clamp(filtro.Limite ?? LimitePorDefecto, 1, LimiteMaximo);

        var consulta = VentasVisibles();
        if (!filtro.IncluirAnuladas) consulta = consulta.Where(v => !v.Anulada);
        if (filtro.Desde is { } desde) { var d = desde.UtcDateTime; consulta = consulta.Where(v => v.Fecha >= d); }
        if (filtro.Hasta is { } hasta) { var h = hasta.UtcDateTime; consulta = consulta.Where(v => v.Fecha < h); }
        // Más nuevas primero; el Id es un Guid v7 (ordenado por creación): cursor sin OFFSET.
        if (filtro.Cursor is { } cursor) consulta = consulta.Where(v => v.Id.CompareTo(cursor) < 0);

        var pagina = await consulta.OrderByDescending(v => v.Id).Take(limite + 1).Include(v => v.Lineas).ToListAsync(ct);
        var hayMas = pagina.Count > limite;
        if (hayMas) pagina.RemoveAt(pagina.Count - 1);

        var nombres = await NombresAsync(pagina.Select(v => v.UsuarioId), ct);
        var items = pagina.Select(v => ADtoResumen(v, nombres)).ToList();
        return new PaginaDeVentasDto(items, hayMas ? items[^1].Id : null);
    }

    public async Task<IReadOnlyList<VentaDiaDto>> PorDiaAsync(FiltroPorDiaDto filtro, CancellationToken ct)
    {
        TiendaIdDeLaSesion();
        if (filtro.Dias is < 1 or > 60) throw new ValidacionException("Pedí entre 1 y 60 días.");
        if (filtro.DesfaseMinutos is < -840 or > 840) throw new ValidacionException("El desfase horario no es válido.");

        var desde = filtro.Desde.UtcDateTime;
        var hasta = desde.AddDays(filtro.Dias);
        var filas = await LineasVisibles()
            .Where(l => l.Venta!.Fecha >= desde && l.Venta.Fecha < hasta)
            .Select(l => new { l.VentaId, l.Venta!.Fecha, l.Cantidad, l.PrecioUnitario }).ToListAsync(ct);

        // El día de cada venta es el de quien mira: hora local = UTC − desfase (getTimezoneOffset es UTC − local).
        var primero = DateOnly.FromDateTime(desde.AddMinutes(-filtro.DesfaseMinutos));
        var porDia = filas.GroupBy(f => DateOnly.FromDateTime(f.Fecha.AddMinutes(-filtro.DesfaseMinutos)))
            .ToDictionary(g => g.Key, g => (Total: g.Sum(f => f.PrecioUnitario * f.Cantidad), Ventas: g.Select(f => f.VentaId).Distinct().Count()));
        return Enumerable.Range(0, filtro.Dias).Select(i =>
        {
            var dia = primero.AddDays(i);
            return porDia.TryGetValue(dia, out var d) ? new VentaDiaDto(dia, d.Total, d.Ventas) : new VentaDiaDto(dia, 0, 0);
        }).ToList();
    }

    public async Task<IReadOnlyList<MasVendidoDto>> MasVendidosAsync(FiltroMasVendidosDto filtro, CancellationToken ct)
    {
        TiendaIdDeLaSesion();
        var limite = Math.Clamp(filtro.Limite ?? 5, 1, 20);
        var lineas = LineasVisibles();
        if (filtro.Desde is { } desde) { var d = desde.UtcDateTime; lineas = lineas.Where(l => l.Venta!.Fecha >= d); }
        if (filtro.Hasta is { } hasta) { var h = hasta.UtcDateTime; lineas = lineas.Where(l => l.Venta!.Fecha < h); }

        var filas = await lineas.Select(l => new { l.ProductoId, l.Descripcion, l.Cantidad, l.PrecioUnitario, l.CostoUnitario }).ToListAsync(ct);
        var top = filas.GroupBy(f => f.ProductoId)
            .Select(g => new
            {
                ProductoId = g.Key,
                Unidades = g.Sum(f => f.Cantidad),
                // Solo las unidades con costo cargado cuentan para la ganancia (como en el resumen: no se inventa).
                Ganancia = g.Any(f => f.CostoUnitario > 0) ? g.Where(f => f.CostoUnitario > 0).Sum(f => (f.PrecioUnitario - f.CostoUnitario) * f.Cantidad) : (decimal?)null,
                Descripcion = g.First().Descripcion,
            })
            .OrderByDescending(x => x.Unidades).ThenByDescending(x => x.Ganancia ?? 0).Take(limite).ToList();

        // El nombre de hoy del producto (la descripción de la venta trae también la variante: "Cartera (café · M)").
        var ids = top.Select(x => x.ProductoId).ToList();
        var nombres = await _db.Productos.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Nombre, ct);
        return top.Select(x => new MasVendidoDto(x.ProductoId, nombres.GetValueOrDefault(x.ProductoId, x.Descripcion), x.Unidades, x.Ganancia)).ToList();
    }

    public async Task<ResumenVentasDto> ResumenAsync(FiltroPeriodoDto filtro, CancellationToken ct)
    {
        TiendaIdDeLaSesion();
        var lineas = LineasVisibles();
        var anuladas = VentasVisibles().Where(v => v.Anulada);
        if (filtro.Desde is { } desde)
        {
            var d = desde.UtcDateTime;
            lineas = lineas.Where(l => l.Venta!.Fecha >= d);
            anuladas = anuladas.Where(v => v.Fecha >= d);
        }
        if (filtro.Hasta is { } hasta)
        {
            var h = hasta.UtcDateTime;
            lineas = lineas.Where(l => l.Venta!.Fecha < h);
            anuladas = anuladas.Where(v => v.Fecha < h);
        }

        var filas = await lineas.Select(l => new { l.VentaId, l.Cantidad, l.PrecioUnitario, l.CostoUnitario }).ToListAsync(ct);
        var cantidadAnuladas = await anuladas.CountAsync(ct);

        decimal total = 0, ganancia = 0;
        var unidades = 0;
        var sinCosto = 0;
        foreach (var f in filas)
        {
            total += f.Cantidad * f.PrecioUnitario;
            unidades += f.Cantidad;
            if (f.CostoUnitario > 0) ganancia += f.Cantidad * (f.PrecioUnitario - f.CostoUnitario);
            else sinCosto += f.Cantidad;
        }
        return new ResumenVentasDto(filas.Select(f => f.VentaId).Distinct().Count(), unidades, total, ganancia, sinCosto, cantidadAnuladas);
    }

    // Lo vendido por cada persona en el período. Sin el permiso de ver todo, solo aparece la propia.
    public async Task<IReadOnlyList<VentaVendedorDto>> PorVendedorAsync(FiltroPeriodoDto filtro, CancellationToken ct)
    {
        TiendaIdDeLaSesion();
        var lineas = LineasVisibles();
        if (filtro.Desde is { } desde) { var d = desde.UtcDateTime; lineas = lineas.Where(l => l.Venta!.Fecha >= d); }
        if (filtro.Hasta is { } hasta) { var h = hasta.UtcDateTime; lineas = lineas.Where(l => l.Venta!.Fecha < h); }

        var filas = await lineas.Select(l => new { l.VentaId, l.Venta!.UsuarioId, l.Cantidad, l.PrecioUnitario }).ToListAsync(ct);
        var nombres = await NombresAsync(filas.Select(f => f.UsuarioId), ct);
        return filas.GroupBy(f => f.UsuarioId)
            .Select(g => new VentaVendedorDto(
                g.Key, g.Key is { } id ? nombres.GetValueOrDefault(id, "Sin nombre") : "Sin registrar",
                g.Select(f => f.VentaId).Distinct().Count(), g.Sum(f => f.Cantidad), g.Sum(f => f.Cantidad * f.PrecioUnitario)))
            .OrderByDescending(v => v.Total).ThenBy(v => v.Vendedor).ToList();
    }

    // ───────────── Anular ─────────────

    public async Task<VentaDto> AnularAsync(Guid id, CancellationToken ct)
    {
        TiendaIdDeLaSesion();
        var venta = await VentasVisibles().Include(v => v.Lineas).FirstOrDefaultAsync(v => v.Id == id, ct)
            ?? throw new VentaNoEncontradaException(id);
        if (venta.Anulada) throw new VentaYaAnuladaException();

        try
        {
            await using var transaccion = await _db.Database.BeginTransactionAsync(ct);

            // Un solo UPDATE condicional: si dos pedidos anulan la misma venta a la vez, solo uno la marca y el
            // otro no devuelve el stock dos veces.
            var ahora = _reloj.GetUtcNow().UtcDateTime;
            var marcadas = await _db.Ventas.Where(v => v.Id == id && !v.Anulada)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.Anulada, true).SetProperty(v => v.AnuladaEn, ahora), ct);
            if (marcadas == 0) throw new VentaYaAnuladaException();

            // Solo vuelve lo que se había descontado (DescontoStock): no se inventan unidades de un producto que
            // pasó a llevar control después de la venta.
            var devolver = venta.Lineas.Where(l => l.DescontoStock)
                .GroupBy(l => l.VarianteId).Select(g => new LineaMovimiento(g.Key, g.Sum(l => l.Cantidad))).ToList();
            if (devolver.Count > 0)
            {
                await _stock.AplicarAsync(TipoMovimiento.Devolucion, devolver, "Venta anulada", null, ignorarSinControl: true, ct, id);
                await _db.SaveChangesAsync(ct);
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

    // ───────────── Elegir qué vender ─────────────

    public async Task<PaginaDeVendiblesDto> ProductosParaVenderAsync(FiltroVendiblesDto filtro, CancellationToken ct)
    {
        var tiendaId = TiendaIdDeLaSesion();
        var limite = Math.Clamp(filtro.Limite ?? LimitePorDefecto, 1, LimiteMaximo);
        var cursor = LeerCursor(filtro.Cursor);

        var productos = _db.Productos.AsNoTracking().Where(p => !p.Archivado);
        if (!string.IsNullOrWhiteSpace(filtro.Buscar))
        {
            var texto = filtro.Buscar.Trim().ToLower();
            productos = productos.Where(p => p.Nombre.ToLower().Contains(texto));
        }

        // Prioridad 0 = se puede vender, 1 = agotado (con control y sin unidades): los agotados van al final.
        var conPrioridad = productos
            .Select(p => new
            {
                p.Id,
                Total = p.Variantes.Where(v => v.Activa).Sum(v => v.Movimientos.Sum(m => m.Cantidad)),
                p.ControlaStock,
            })
            .Select(x => new { x.Id, Prioridad = x.ControlaStock && x.Total <= 0 ? 1 : 0 });
        if (cursor is { } c)
            conPrioridad = conPrioridad.Where(x => x.Prioridad > c.Prioridad || (x.Prioridad == c.Prioridad && x.Id.CompareTo(c.Id) < 0));

        var pagina = await conPrioridad.OrderBy(x => x.Prioridad).ThenByDescending(x => x.Id).Take(limite + 1).ToListAsync(ct);
        var hayMas = pagina.Count > limite;
        if (hayMas) pagina.RemoveAt(pagina.Count - 1);
        var ids = pagina.Select(x => x.Id).ToList();

        var datos = await _db.Productos.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new
            {
                p.Id, p.Nombre, p.Precio, p.Costo, p.ControlaStock,
                PortadaId = p.Fotos.OrderBy(f => f.Orden).Select(f => (Guid?)f.ImagenId).FirstOrDefault(),
            })
            .ToDictionaryAsync(p => p.Id, ct);
        var variantes = await _db.Variantes.AsNoTracking().Where(v => v.Activa && ids.Contains(v.ProductoId))
            .OrderBy(v => v.Orden).ThenBy(v => v.Id).Select(v => new { v.Id, v.ProductoId, v.Color, v.Talla }).ToListAsync(ct);
        var stocks = await _db.PorVarianteAsync(variantes.Select(v => v.Id).ToList(), ct);
        var portadaIds = datos.Values.Where(p => p.PortadaId != null).Select(p => p.PortadaId!.Value).ToList();
        var portadas = await _db.Imagenes.AsNoTracking().Where(i => portadaIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);

        var items = pagina.Select(x =>
        {
            var p = datos[x.Id];
            var suyas = variantes.Where(v => v.ProductoId == p.Id)
                .Select(v => new VarianteVendibleDto(v.Id, Nulo(v.Color), Nulo(v.Talla), p.ControlaStock ? stocks.GetValueOrDefault(v.Id) : null)).ToList();
            return new ProductoVendibleDto(
                p.Id, p.Nombre, p.Precio, p.Costo,
                p.PortadaId is { } img && portadas.TryGetValue(img, out var imagen) ? ImagenMapeo.ADto(_almacen, tiendaId, imagen) : null,
                p.ControlaStock, p.ControlaStock ? suyas.Sum(v => v.Stock ?? 0) : null, suyas);
        }).ToList();

        return new PaginaDeVendiblesDto(items, hayMas ? $"{pagina[^1].Prioridad}.{pagina[^1].Id}" : null);
    }

    // ───────────── DTOs ─────────────

    private static string? Nulo(string texto) => texto.Length == 0 ? null : texto;

    private static DateTimeOffset Utc(DateTime d) => new(DateTime.SpecifyKind(d, DateTimeKind.Utc));

    private static (decimal Total, int Unidades, decimal? Ganancia) Totales(IEnumerable<VentaLinea> lineas)
    {
        decimal total = 0, ganancia = 0;
        var unidades = 0;
        var completa = true;
        foreach (var l in lineas)
        {
            total += l.Cantidad * l.PrecioUnitario;
            unidades += l.Cantidad;
            if (l.CostoUnitario > 0) ganancia += l.Cantidad * (l.PrecioUnitario - l.CostoUnitario);
            else completa = false; // sin costo no se puede saber cuánto se ganó
        }
        return (total, unidades, completa ? ganancia : null);
    }

    private static VentaDto ADto(Venta v, IReadOnlyDictionary<Guid, string> nombres)
    {
        var (total, unidades, ganancia) = Totales(v.Lineas);
        var lineas = v.Lineas.OrderBy(l => l.Id).Select(l => new VentaLineaDto(
            l.Id, l.ProductoId, l.VarianteId, l.Descripcion, l.Cantidad, l.PrecioUnitario,
            l.CostoUnitario > 0 ? l.CostoUnitario : null, l.Cantidad * l.PrecioUnitario,
            l.CostoUnitario > 0 ? l.Cantidad * (l.PrecioUnitario - l.CostoUnitario) : null)).ToList();
        return new VentaDto(v.Id, Utc(v.Fecha), v.MetodoPago, v.Cliente, v.Anulada, v.AnuladaEn is { } a ? Utc(a) : null,
            unidades, total, ganancia, lineas, Vendedor(v, nombres));
    }

    private static string? Vendedor(Venta v, IReadOnlyDictionary<Guid, string> nombres)
        => v.UsuarioId is { } id ? nombres.GetValueOrDefault(id) : null;

    private static VentaResumenDto ADtoResumen(Venta v, IReadOnlyDictionary<Guid, string> nombres)
    {
        var (total, unidades, ganancia) = Totales(v.Lineas);
        var primeras = v.Lineas.OrderBy(l => l.Id).Take(2).Select(l => $"{l.Cantidad} {l.Descripcion}").ToList();
        var detalle = string.Join(" · ", primeras) + (v.Lineas.Count > 2 ? $" · y {v.Lineas.Count - 2} más" : "");
        return new VentaResumenDto(v.Id, Utc(v.Fecha), v.MetodoPago, v.Cliente, v.Anulada, unidades, total, ganancia, detalle, Vendedor(v, nombres));
    }

    // "0.0194…": prioridad y id del último producto de la página anterior.
    private static (int Prioridad, Guid Id)? LeerCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        var partes = cursor.Split('.', 2);
        if (partes.Length == 2 && int.TryParse(partes[0], out var prioridad) && prioridad is 0 or 1 && Guid.TryParse(partes[1], out var id))
            return (prioridad, id);
        throw new ValidacionException("El cursor de la página no es válido.");
    }
}
