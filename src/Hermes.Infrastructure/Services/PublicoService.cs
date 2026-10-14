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

    public PublicoService(HermesDbContext db, IAlmacenArchivos almacen, TiendaPublicaContexto contexto)
    {
        _db = db;
        _almacen = almacen;
        _contexto = contexto;
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
        if (cursor is { } c)
            conPrioridad = conPrioridad.Where(x => x.Prioridad > c.Prioridad || (x.Prioridad == c.Prioridad && x.Id.CompareTo(c.Id) < 0));

        var pagina = await conPrioridad.OrderBy(x => x.Prioridad).ThenByDescending(x => x.Id).Take(limite + 1).ToListAsync(ct);
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

        return new PaginaPublicaDto(items, hayMas ? $"{pagina[^1].Prioridad}.{pagina[^1].Id}" : null);
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
