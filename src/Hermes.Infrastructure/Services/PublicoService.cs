using Hermes.Application;
using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Domain.Entities;
using Hermes.Infrastructure.Imagenes;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Stock;
using Microsoft.EntityFrameworkCore;

namespace Hermes.Infrastructure.Services;

// La tienda pública. Primero se resuelve el slug → tienda (Tienda no tiene filtro global: es el tenant) y se FIJA como
// tienda actual del pedido; desde ahí, el filtro global de Producto/Variante/Imagen/Movimiento acota todo a esa tienda.
// Solo se muestran productos no archivados y visibles; las variantes desactivadas no existen para el público.
// NUNCA sale costo, margen ni stock exacto: estos DTOs no tienen dónde ponerlo (ver PublicoDto.cs).
public class PublicoService : IPublicoService
{
    private const int LimitePorDefecto = 24;
    private const int LimiteMaximo = 48;

    private readonly HermesDbContext _db;
    private readonly IAlmacenArchivos _almacen;
    private readonly TiendaPublicaContexto _contexto;
    private readonly TimeProvider? _reloj;

    public PublicoService(HermesDbContext db, IAlmacenArchivos almacen, TiendaPublicaContexto contexto, TimeProvider? reloj = null)
    {
        _db = db;
        _almacen = almacen;
        _contexto = contexto;
        _reloj = reloj;
    }

    private async Task<Tienda> FijarTiendaAsync(string slug, CancellationToken ct)
    {
        var limpio = (slug ?? string.Empty).Trim().ToLowerInvariant();
        var tienda = await _db.Tiendas.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == limpio && t.Activa, ct)
            ?? throw new TiendaPublicaNoEncontradaException();
        _contexto.TiendaId = tienda.Id;
        return tienda;
    }

    // ───────────── Tienda ─────────────

    public async Task<TiendaPublicaDto> ObtenerTiendaAsync(string slug, CancellationToken ct)
    {
        var t = await FijarTiendaAsync(slug, ct);

        ImagenDto? logo = null;
        if (t.LogoImagenId is { } logoId)
        {
            var imagen = await _db.Imagenes.AsNoTracking().FirstOrDefaultAsync(i => i.Id == logoId, ct);
            if (imagen is not null) logo = ImagenMapeo.ADto(_almacen, t.Id, imagen);
        }

        // Solo las categorías que tienen algo para mostrar.
        var categorias = (await _db.Productos.AsNoTracking()
            .Where(p => !p.Archivado && p.VisibleEnTienda && p.CategoriaId != null && p.Categoria!.Activa)
            .Select(p => new { Id = p.CategoriaId!.Value, p.Categoria!.Nombre })
            .Distinct().OrderBy(c => c.Nombre).ToListAsync(ct))
            .Select(c => new CategoriaPublicaDto(c.Id, c.Nombre)).ToList();

        return new TiendaPublicaDto(
            t.Nombre, t.Slug, t.Descripcion, t.Color, t.Fondo, logo, t.MostrarPrecios, t.MostrarDisponibilidad,
            t.PedidosPorWhatsapp ? t.Whatsapp : null, categorias);
    }

    // ───────────── Productos ─────────────

    public async Task<PaginaPublicaDto> ProductosAsync(string slug, FiltroPublicoDto filtro, CancellationToken ct)
    {
        var t = await FijarTiendaAsync(slug, ct);
        var limite = Math.Clamp(filtro.Limite ?? LimitePorDefecto, 1, LimiteMaximo);
        var cursor = LeerCursor(filtro.Cursor);
        var mostrarDisponibilidad = t.MostrarDisponibilidad;

        var productos = _db.Productos.AsNoTracking().Where(p => !p.Archivado && p.VisibleEnTienda);
        if (filtro.CategoriaId is { } categoriaId) productos = productos.Where(p => p.CategoriaId == categoriaId);
        if (!string.IsNullOrWhiteSpace(filtro.Buscar))
        {
            var texto = filtro.Buscar.Trim().ToLower();
            productos = productos.Where(p => p.Nombre.ToLower().Contains(texto));
        }

        // Prioridad 0 = disponible, 1 = agotado (con control y sin unidades): los agotados van al final. Si la tienda
        // NO muestra disponibilidad, todos van en 0: ordenar por agotado revelaría lo que se decidió ocultar.
        var conPrioridad = productos
            .Select(p => new
            {
                p.Id,
                Total = p.Variantes.Where(v => v.Activa).Sum(v => v.Movimientos.Sum(m => m.Cantidad)),
                p.ControlaStock,
            })
            .Select(x => new { x.Id, Prioridad = mostrarDisponibilidad && x.ControlaStock && x.Total <= 0 ? 1 : 0 });
        // Página numerada: Skip + cantidad de páginas (solo se cuenta, nunca se muestra cuántos productos hay en total).
        var numeroPagina = 1;
        var totalPaginas = 1;
        List<(int Prioridad, Guid Id)> pagina;
        if (filtro.Pagina is { } pedida)
        {
            var cuantos = await conPrioridad.CountAsync(ct);
            totalPaginas = Math.Max(1, (int)Math.Ceiling(cuantos / (double)limite));
            numeroPagina = Math.Clamp(pedida, 1, totalPaginas);
            pagina = (await conPrioridad.OrderBy(x => x.Prioridad).ThenByDescending(x => x.Id)
                .Skip((numeroPagina - 1) * limite).Take(limite + 1).ToListAsync(ct)).Select(x => (x.Prioridad, x.Id)).ToList();
        }
        else
        {
            if (cursor is { } c)
                conPrioridad = conPrioridad.Where(x => x.Prioridad > c.Prioridad || (x.Prioridad == c.Prioridad && x.Id.CompareTo(c.Id) < 0));
            pagina = (await conPrioridad.OrderBy(x => x.Prioridad).ThenByDescending(x => x.Id).Take(limite + 1).ToListAsync(ct))
                .Select(x => (x.Prioridad, x.Id)).ToList();
        }
        var hayMas = pagina.Count > limite;
        if (hayMas) pagina.RemoveAt(pagina.Count - 1);
        var ids = pagina.Select(x => x.Id).ToList();

        var datos = await _db.Productos.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new
            {
                p.Id, p.Nombre, p.Precio, p.ControlaStock,
                Categoria = p.Categoria != null && p.Categoria.Activa ? p.Categoria.Nombre : null,
                PortadaId = p.Fotos.OrderBy(f => f.Orden).Select(f => (Guid?)f.ImagenId).FirstOrDefault(),
            })
            .ToDictionaryAsync(p => p.Id, ct);
        var portadaIds = datos.Values.Where(p => p.PortadaId != null).Select(p => p.PortadaId!.Value).ToList();
        var portadas = await _db.Imagenes.AsNoTracking().Where(i => portadaIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var disponibles = mostrarDisponibilidad ? await DisponiblesAsync(ids, ct) : [];

        var items = pagina.Select(x =>
        {
            var p = datos[x.Id];
            return new ProductoPublicoResumenDto(
                p.Id, p.Nombre, t.MostrarPrecios ? p.Precio : null,
                p.PortadaId is { } img && portadas.TryGetValue(img, out var imagen) ? ImagenMapeo.ADto(_almacen, t.Id, imagen) : null,
                p.Categoria, mostrarDisponibilidad ? !p.ControlaStock || disponibles.Contains(p.Id) : null);
        }).ToList();

        return new PaginaPublicaDto(items, hayMas ? $"{pagina[^1].Prioridad}.{pagina[^1].Id}" : null, numeroPagina, totalPaginas);
    }

    public async Task<ProductoPublicoDto> ProductoAsync(string slug, Guid productoId, CancellationToken ct)
    {
        var t = await FijarTiendaAsync(slug, ct);

        // El filtro global ya descarta los de otra tienda; acá se descartan los archivados y los ocultos.
        var p = await _db.Productos.AsNoTracking().Include(x => x.Categoria).Include(x => x.Variantes)
            .Include(x => x.Fotos).FirstOrDefaultAsync(x => x.Id == productoId && !x.Archivado && x.VisibleEnTienda, ct)
            ?? throw new TiendaPublicaNoEncontradaException();

        var fotoIds = p.Fotos.OrderBy(f => f.Orden).Select(f => f.ImagenId).ToList();
        var imagenes = await _db.Imagenes.AsNoTracking().Where(i => fotoIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var fotos = fotoIds.Where(imagenes.ContainsKey).Select(id => ImagenMapeo.ADto(_almacen, t.Id, imagenes[id])).ToList();

        var activas = p.Variantes.Where(v => v.Activa).OrderBy(v => v.Orden).ThenBy(v => v.Id).ToList();
        var stocks = t.MostrarDisponibilidad && p.ControlaStock ? await _db.PorVarianteAsync(activas.Select(v => v.Id).ToList(), ct) : [];
        bool? Disponible(Variante v) => !t.MostrarDisponibilidad ? null : !p.ControlaStock || stocks.GetValueOrDefault(v.Id) > 0;

        var variantes = activas.Select(v => new VariantePublicaDto(v.Id, Nulo(v.Color), Nulo(v.Talla), Disponible(v))).ToList();
        bool? disponible = !t.MostrarDisponibilidad ? null : !p.ControlaStock || variantes.Any(v => v.Disponible == true);

        return new ProductoPublicoDto(
            p.Id, p.Nombre, p.Descripcion, t.MostrarPrecios ? p.Precio : null,
            p.Categoria is { Activa: true } c ? c.Nombre : null, disponible, fotos, variantes);
    }

    // ───────────── Carrito y pedido ─────────────

    public async Task<CarritoPublicoDto> RevisarCarritoAsync(string slug, RevisarCarritoDto dto, CancellationToken ct)
    {
        var t = await FijarTiendaAsync(slug, ct);
        var ids = (dto.Variantes ?? []).Distinct().Take(Pedido.MaximoLineas).ToList();
        var pedibles = await PediblesAsync(ids, ct);

        var productoIds = pedibles.Values.Select(v => v.ProductoId).Distinct().ToList();
        var portadas = await _db.ProductoFotos.AsNoTracking().Where(f => productoIds.Contains(f.ProductoId))
            .GroupBy(f => f.ProductoId).Select(g => new { ProductoId = g.Key, ImagenId = g.OrderBy(f => f.Orden).Select(f => f.ImagenId).First() })
            .ToDictionaryAsync(x => x.ProductoId, x => x.ImagenId, ct);
        var imagenIds = portadas.Values.ToList();
        var imagenes = await _db.Imagenes.AsNoTracking().Where(i => imagenIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var conStock = t.MostrarDisponibilidad
            ? await _db.PorVarianteAsync(pedibles.Values.Where(v => v.Producto!.ControlaStock).Select(v => v.Id).ToList(), ct)
            : [];

        var articulos = ids.Where(pedibles.ContainsKey).Select(id =>
        {
            var v = pedibles[id];
            var p = v.Producto!;
            var variante = string.Join(" · ", new[] { v.Color, v.Talla }.Where(s => s.Length > 0));
            return new ArticuloCarritoDto(
                v.Id, p.Id, p.Nombre, variante.Length > 0 ? variante : null, t.MostrarPrecios ? p.Precio : null,
                t.MostrarDisponibilidad ? !p.ControlaStock || conStock.GetValueOrDefault(v.Id) > 0 : null,
                portadas.TryGetValue(p.Id, out var img) && imagenes.TryGetValue(img, out var imagen) ? ImagenMapeo.ADto(_almacen, t.Id, imagen) : null);
        }).ToList();

        return new CarritoPublicoDto(articulos, RecibePedidos(t));
    }

    public async Task<PedidoEnviadoDto> PedirAsync(string slug, NuevoPedidoDto dto, CancellationToken ct)
    {
        var t = await FijarTiendaAsync(slug, ct);
        if (!RecibePedidos(t)) throw new TiendaSinPedidosException();
        if (dto.ClaveIdempotencia == Guid.Empty) throw new ValidacionException("Falta la clave del pedido.");

        // Un reintento (doble toque, se cortó la conexión) devuelve el pedido que ya estaba, aunque llegue distinto.
        if (await PorClaveAsync(dto.ClaveIdempotencia, ct) is { } previo) return Enviado(t, previo, nuevo: false);

        var nombre = Validacion.Texto(dto.Nombre, 80, "Tu nombre");
        var celular = Validacion.Whatsapp(dto.Celular)
            ?? throw new ValidacionException("Escribí tu celular para que la tienda te pueda responder.");
        if (!Enum.IsDefined(dto.Entrega)) throw new ValidacionException("Elegí si lo retirás o te lo envían.");
        string? direccion = null, referencia = null;
        if (dto.Entrega == EntregaPedido.Envio)
        {
            direccion = Validacion.Texto(dto.Direccion, 200, "La dirección de entrega");
            referencia = Validacion.TextoOpcional(dto.Referencia, 200, "La referencia");
        }
        var nota = Validacion.TextoOpcional(dto.Nota, 300, "La nota");

        // La misma variante dos veces se junta en una línea, en el orden en que apareció primero.
        var articulos = (dto.Articulos ?? []).GroupBy(a => a.VarianteId)
            .Select(g => (VarianteId: g.Key, Unidades: g.Sum(a => (long)a.Unidades), Primera: g.First())).ToList();
        if (articulos.Count == 0) throw new ValidacionException("Tu carrito está vacío.");
        if (articulos.Count > Pedido.MaximoLineas) throw new ValidacionException($"Podés pedir hasta {Pedido.MaximoLineas} productos distintos por pedido.");
        if (articulos.Any(a => a.Unidades < 1 || a.Unidades > Pedido.MaximoCantidad))
            throw new ValidacionException($"Podés pedir de 1 a {Pedido.MaximoCantidad} unidades de cada producto.");

        var pedibles = await PediblesAsync(articulos.Select(a => a.VarianteId).ToList(), ct);
        if (articulos.Any(a => !pedibles.ContainsKey(a.VarianteId)))
            throw new ProductoNoPedibleException("Algo de tu carrito ya no está a la venta. Volvé a abrir el carrito para ver qué cambió.");

        // Solo se rechaza lo AGOTADO y solo si la tienda muestra la disponibilidad (si la oculta, no se revela así). No se
        // compara con las unidades pedidas: eso dejaría adivinar el stock exacto. Lo resuelve la dueña al confirmar.
        if (t.MostrarDisponibilidad)
        {
            var controladas = pedibles.Values.Where(v => v.Producto!.ControlaStock).Select(v => v.Id).ToList();
            var stock = await _db.PorVarianteAsync(controladas, ct);
            var agotada = controladas.FirstOrDefault(id => stock.GetValueOrDefault(id) <= 0);
            if (agotada != Guid.Empty)
                throw new ProductoNoPedibleException($"«{Descripcion(pedibles[agotada])}» se agotó. Sacalo del carrito y probá de nuevo.");
        }

        var ahora = (_reloj ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            // Correlativo por tienda: se toma el candado de la fila de la tienda (un UPDATE que no cambia nada, como el
            // candado por variante del stock) y RECIÉN DESPUÉS se lee el último número. Dos pedidos a la vez esperan en fila:
            // sin el candado, 8 simultáneos chocaban en el índice único (lo encontró la prueba con Postgres).
            var tiendaId = t.Id;
            await _db.Tiendas.Where(x => x.Id == tiendaId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Slug, x => x.Slug), ct);
            var numero = (await _db.Pedidos.MaxAsync(p => (int?)p.Numero, ct) ?? 0) + 1;
            var pedido = new Pedido
            {
                Numero = numero, ClaveIdempotencia = dto.ClaveIdempotencia, Fecha = ahora,
                ClienteNombre = nombre, ClienteCelular = celular, Entrega = dto.Entrega, Direccion = direccion, Referencia = referencia, Nota = nota,
                Lineas = articulos.Select((a, i) => new PedidoLinea
                {
                    ProductoId = pedibles[a.VarianteId].ProductoId, VarianteId = a.VarianteId, Descripcion = Descripcion(pedibles[a.VarianteId]),
                    Cantidad = (int)a.Unidades, PrecioUnitario = pedibles[a.VarianteId].Producto!.Precio, Orden = i,
                }).ToList(),
            };
            _db.Pedidos.Add(pedido);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Enviado(t, pedido, nuevo: true);
        }
        catch (DbUpdateException)
        {
            // La misma clave llegó dos veces a la vez: el índice único (TiendaId, ClaveIdempotencia) dejó pasar a una.
            _db.ChangeTracker.Clear();
            if (await PorClaveAsync(dto.ClaveIdempotencia, ct) is { } ganador) return Enviado(t, ganador, nuevo: false);
            throw;
        }
    }

    private static bool RecibePedidos(Tienda t) => t.PedidosPorWhatsapp && t.Whatsapp is not null;

    // Variantes ACTIVAS de productos no archivados y visibles (el filtro global ya deja solo las de esta tienda).
    private async Task<Dictionary<Guid, Variante>> PediblesAsync(List<Guid> ids, CancellationToken ct)
        => await _db.Variantes.AsNoTracking().Include(v => v.Producto)
            .Where(v => ids.Contains(v.Id) && v.Activa && !v.Producto!.Archivado && v.Producto.VisibleEnTienda)
            .ToDictionaryAsync(v => v.Id, ct);

    private async Task<Pedido?> PorClaveAsync(Guid clave, CancellationToken ct)
        => await _db.Pedidos.AsNoTracking().Include(p => p.Lineas).FirstOrDefaultAsync(p => p.ClaveIdempotencia == clave, ct);

    // "Cartera (café · M)", igual que en las ventas.
    private static string Descripcion(Variante v)
    {
        var variante = string.Join(" · ", new[] { v.Color, v.Talla }.Where(s => s.Length > 0));
        return variante.Length > 0 ? $"{v.Producto!.Nombre} ({variante})" : v.Producto!.Nombre;
    }

    private static PedidoEnviadoDto Enviado(Tienda t, Pedido p, bool nuevo)
    {
        var lineas = p.Lineas.OrderBy(l => l.Orden)
            .Select(l => new LineaPedidoPublicaDto(l.Descripcion, l.Cantidad, t.MostrarPrecios ? l.PrecioUnitario : null)).ToList();
        decimal? total = t.MostrarPrecios ? p.Lineas.Sum(l => l.PrecioUnitario * l.Cantidad) : null;
        return new PedidoEnviadoDto(t.Nombre, p.Numero, lineas, total, p.ClienteNombre, p.Entrega, p.Direccion, t.Whatsapp!, nuevo);
    }

    // ───────────── Interno ─────────────

    // Los productos (de estos ids) que tienen al menos una unidad en alguna variante activa, o no llevan control.
    private async Task<HashSet<Guid>> DisponiblesAsync(List<Guid> productoIds, CancellationToken ct)
    {
        var variantes = await _db.Variantes.AsNoTracking().Where(v => v.Activa && productoIds.Contains(v.ProductoId))
            .Select(v => new { v.Id, v.ProductoId }).ToListAsync(ct);
        var stocks = await _db.PorVarianteAsync(variantes.Select(v => v.Id).ToList(), ct);
        return variantes.Where(v => stocks.GetValueOrDefault(v.Id) > 0).Select(v => v.ProductoId).ToHashSet();
    }

    private static string? Nulo(string texto) => texto.Length == 0 ? null : texto;

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
