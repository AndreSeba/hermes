using Hermes.Application;
using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Domain;
using Hermes.Domain.Entities;
using Hermes.Infrastructure.Imagenes;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Stock;
using Microsoft.EntityFrameworkCore;

namespace Hermes.Infrastructure.Services;

// Producto, Variante, ProductoFoto e Imagen son de la tienda (filtro global): ninguna consulta de acá
// filtra por tienda a mano. Los datos de ENTRADA que apuntan a otras filas (categoría, fotos) se
// validan con consultas que ya vienen filtradas, así una id de otra tienda "no existe".
public class ProductoService : IProductoService
{
    private const int LimitePorDefecto = 24;
    private const int LimiteMaximo = 60;

    private readonly HermesDbContext _db;
    private readonly IImagenService _imagenes;
    private readonly IAlmacenArchivos _almacen;
    private readonly ITiendaActual _sesion;
    private readonly TimeProvider _reloj;

    public ProductoService(HermesDbContext db, IImagenService imagenes, IAlmacenArchivos almacen, ITiendaActual sesion, TimeProvider reloj)
    {
        _db = db;
        _imagenes = imagenes;
        _almacen = almacen;
        _sesion = sesion;
        _reloj = reloj;
    }

    // Leer a una variable ANTES de armar consultas (ver TiendaService: si lanza dentro de LINQ sale como 500).
    private Guid TiendaIdDeLaSesion() => _sesion.TiendaId ?? throw new SinTiendaEnSesionException();

    // ───────────── Lectura ─────────────

    public async Task<PaginaDeProductosDto> ListarAsync(FiltroProductosDto filtro, CancellationToken ct)
    {
        var tiendaId = TiendaIdDeLaSesion();
        var limite = Math.Clamp(filtro.Limite ?? LimitePorDefecto, 1, LimiteMaximo);

        var consulta = _db.Productos.AsNoTracking().Where(p => p.Archivado == filtro.Archivados);
        if (filtro.CategoriaId is { } categoriaId) consulta = consulta.Where(p => p.CategoriaId == categoriaId);
        if (filtro.RevisarPrecio)
            // Misma regla que MargenProducto.Estado: con costo cargado, Pierde o Bajo.
            consulta = consulta.Where(p => p.Costo > 0 && p.Precio - p.Costo < p.Precio * MargenProducto.UmbralBajo);
        if (!string.IsNullOrWhiteSpace(filtro.Buscar))
        {
            var texto = filtro.Buscar.Trim().ToLower();
            consulta = consulta.Where(p => p.Nombre.ToLower().Contains(texto));
        }
        // Paginación por cursor: el Id es un Guid v7 (ordenado por fecha de creación), así que "más
        // nuevo primero" y "continuar después de este" son una sola condición. Sin OFFSET: no se saltea
        // ni se repite nada aunque se creen productos mientras se pagina.
        if (filtro.Cursor is { } cursor) consulta = consulta.Where(p => p.Id.CompareTo(cursor) < 0);

        var filas = await consulta.OrderByDescending(p => p.Id).Take(limite + 1)
            .Select(p => new
            {
                p.Id, p.Nombre, p.Precio, p.Costo, p.CategoriaId,
                Categoria = p.Categoria != null ? p.Categoria.Nombre : null,
                PortadaId = p.Fotos.OrderBy(f => f.Orden).Select(f => (Guid?)f.ImagenId).FirstOrDefault(),
                p.VisibleEnTienda, p.Archivado, p.ControlaStock, p.StockMinimo,
                Variantes = p.Variantes.Count(v => v.Activa),
            })
            .ToListAsync(ct);

        var hayMas = filas.Count > limite;
        if (hayMas) filas.RemoveAt(filas.Count - 1);

        var portadaIds = filas.Where(f => f.PortadaId != null).Select(f => f.PortadaId!.Value).ToList();
        var portadas = await _db.Imagenes.AsNoTracking().Where(i => portadaIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var stocks = await _db.PorProductoAsync(filas.Where(f => f.ControlaStock).Select(f => f.Id).ToList(), ct);

        var items = filas.Select(f =>
        {
            var total = stocks.GetValueOrDefault(f.Id);
            return new ProductoResumenDto(
                f.Id, f.Nombre, f.Precio, f.Costo,
                MargenProducto.Ganancia(f.Precio, f.Costo), MargenProducto.Estado(f.Precio, f.Costo),
                f.CategoriaId, f.Categoria,
                f.PortadaId is { } id && portadas.TryGetValue(id, out var imagen) ? ImagenMapeo.ADto(_almacen, tiendaId, imagen) : null,
                f.VisibleEnTienda, f.Archivado, f.Variantes,
                f.ControlaStock ? total : null, ReglaStock.Estado(f.ControlaStock, total, f.StockMinimo));
        }).ToList();

        return new PaginaDeProductosDto(items, hayMas ? items[^1].Id : null);
    }

    public async Task<ProductoDto> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var tiendaId = TiendaIdDeLaSesion();
        var producto = await _db.Productos.AsNoTracking()
            .Include(p => p.Variantes).Include(p => p.Fotos)
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new ProductoNoEncontradoException(id);
        return await ADtoAsync(tiendaId, producto, ct);
    }

    // ───────────── Escritura ─────────────

    public async Task<ProductoDto> CrearAsync(GuardarProductoDto dto, CancellationToken ct)
    {
        var tiendaId = TiendaIdDeLaSesion();
        var datos = await ValidarAsync(dto, tiendaId, productoId: null, ct);

        var ahora = _reloj.GetUtcNow();
        var producto = new Producto { CreadoEn = ahora, ActualizadoEn = ahora };
        Aplicar(producto, datos);
        for (var i = 0; i < datos.Variantes.Count; i++)
            producto.Variantes.Add(new Variante { Color = datos.Variantes[i].Color, Talla = datos.Variantes[i].Talla, Orden = i });
        for (var i = 0; i < datos.FotoIds.Count; i++)
            producto.Fotos.Add(new ProductoFoto { ImagenId = datos.FotoIds[i], Orden = i });

        _db.Productos.Add(producto);
        await _db.SaveChangesAsync(ct);
        return await ObtenerAsync(producto.Id, ct);
    }

    public async Task<ProductoDto> ActualizarAsync(Guid id, GuardarProductoDto dto, CancellationToken ct)
    {
        var tiendaId = TiendaIdDeLaSesion();
        var producto = await _db.Productos.Include(p => p.Variantes).Include(p => p.Fotos)
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new ProductoNoEncontradoException(id);
        var datos = await ValidarAsync(dto, tiendaId, productoId: id, ct);

        Aplicar(producto, datos);
        // Con costeo, el costo del producto es el total de sus componentes: el costo directo que llegue se ignora
        // (se cambia desde el costeo).
        if (await _db.Costeos.AsNoTracking().FirstOrDefaultAsync(c => c.ProductoId == id, ct) is { } costeo)
            producto.Costo = ReglaCosteo.Total(costeo.Material, costeo.Empaque, costeo.ManoDeObra, costeo.CostoFijo);
        producto.ActualizadoEn = _reloj.GetUtcNow();
        var activasAntes = producto.Variantes.Where(v => v.Activa).ToList();
        ReconciliarVariantes(producto, datos.Variantes);
        await ExigirSinStockEnLasQueSeSacanAsync(producto, activasAntes, ct);
        var fotosQuitadas = ReconciliarFotos(producto, datos.FotoIds);

        await _db.SaveChangesAsync(ct);

        // Las fotos que se sacaron se borran DESPUÉS de guardar el producto sin ellas: si algo falla
        // queda una foto de más, nunca un producto apuntando a una foto que ya no existe.
        foreach (var imagenId in fotosQuitadas)
            await _imagenes.BorrarAsync(imagenId, ct);

        return await ObtenerAsync(id, ct);
    }

    public async Task<ProductoDto> ArchivarAsync(Guid id, bool archivar, CancellationToken ct)
    {
        var producto = await _db.Productos.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new ProductoNoEncontradoException(id);
        if (producto.Archivado != archivar)
        {
            producto.Archivado = archivar;
            producto.ActualizadoEn = _reloj.GetUtcNow();
            await _db.SaveChangesAsync(ct);
        }
        return await ObtenerAsync(id, ct);
    }

    // ───────────── Reglas ─────────────

    private record DatosValidados(
        string Nombre, string? Descripcion, decimal Precio, decimal Costo, Guid? CategoriaId, bool Visible,
        List<(string Color, string Talla)> Variantes, List<Guid> FotoIds, bool ControlaStock, int StockMinimo);

    // Sacar una variante la desactiva, y el stock de una variante inactiva deja de contar: si todavía
    // quedan unidades, esas unidades "desaparecerían" del total sin dejar rastro. Se pide corregirlo a 0
    // antes (queda en el historial). Solo aplica si el producto lleva control de stock.
    private async Task ExigirSinStockEnLasQueSeSacanAsync(Producto producto, List<Variante> activasAntes, CancellationToken ct)
    {
        if (!producto.ControlaStock) return;
        var sacadas = activasAntes.Where(v => !v.Activa).ToList();
        if (sacadas.Count == 0) return;

        var stocks = await _db.PorVarianteAsync(sacadas.Select(v => v.Id).ToList(), ct);
        var conStock = sacadas.FirstOrDefault(v => stocks.GetValueOrDefault(v.Id) > 0);
        if (conStock is null) return;

        // El contexto ya tiene los cambios a medias: se descartan para que nada se guarde por error.
        var etiqueta = string.Join(" · ", new[] { conStock.Color, conStock.Talla }.Where(s => s.Length > 0));
        var cantidad = stocks[conStock.Id];
        _db.ChangeTracker.Clear();
        throw new ValidacionException(
            $"Todavía quedan {cantidad} de {(etiqueta.Length == 0 ? "Único" : etiqueta)}. Corregí el stock a 0 antes de sacarlo.");
    }

    private async Task<DatosValidados> ValidarAsync(GuardarProductoDto dto, Guid tiendaId, Guid? productoId, CancellationToken ct)
    {
        var nombre = Validacion.Texto(dto.Nombre, 120, "El nombre del producto");
        var descripcion = Validacion.TextoOpcional(dto.Descripcion, 1000, "La descripción");
        var precio = Validacion.Monto(dto.Precio, "El precio", permitirCero: false);
        var costo = Validacion.Monto(dto.Costo, "El costo", permitirCero: true);

        // Solo categorías activas de ESTA tienda (el filtro global descarta las de otra).
        if (dto.CategoriaId is { } categoriaId && !await _db.Categorias.AnyAsync(c => c.Id == categoriaId && c.Activa, ct))
            throw new CategoriaNoEncontradaException(categoriaId);

        var variantes = ParesDeVariantes(
            Validacion.ListaDeTextos(dto.Colores, 30, "color"),
            Validacion.ListaDeTextos(dto.Tallas, 30, "talla"));

        var fotos = (dto.FotoIds ?? []).ToList();
        if (fotos.Count > Producto.MaximoFotos)
            throw new ValidacionException($"Un producto puede tener hasta {Producto.MaximoFotos} fotos.");
        if (fotos.Distinct().Count() != fotos.Count)
            throw new ValidacionException("Hay una foto repetida.");
        await ValidarFotosAsync(fotos, tiendaId, productoId, ct);

        if (dto.StockMinimo is < 0 or > 100_000)
            throw new ValidacionException("El mínimo para avisar tiene que estar entre 0 y 100.000.");

        return new DatosValidados(nombre, descripcion, precio, costo, dto.CategoriaId, dto.VisibleEnTienda, variantes, fotos,
            dto.ControlaStock, dto.StockMinimo);
    }

    private async Task ValidarFotosAsync(List<Guid> fotos, Guid tiendaId, Guid? productoId, CancellationToken ct)
    {
        if (fotos.Count == 0) return;

        // Existen en ESTA tienda (la foto de otra tienda "no existe").
        var existentes = await _db.Imagenes.Where(i => fotos.Contains(i.Id)).Select(i => i.Id).ToListAsync(ct);
        var faltante = fotos.FirstOrDefault(f => !existentes.Contains(f), Guid.Empty);
        if (faltante != Guid.Empty) throw new ImagenNoEncontradaException(faltante);

        // No son de otro producto ni el logo de la tienda.
        var enOtroProducto = await _db.ProductoFotos.AnyAsync(f => fotos.Contains(f.ImagenId) && f.ProductoId != productoId, ct);
        var esElLogo = await _db.Tiendas.AnyAsync(
            t => t.Id == tiendaId && t.LogoImagenId != null && fotos.Contains(t.LogoImagenId.Value), ct);
        if (enOtroProducto || esElLogo) throw new ImagenEnUsoException();
    }

    // Colores × Tallas. Sin ninguno: la variante "Único". Solo colores o solo tallas: una por cada uno.
    private static List<(string Color, string Talla)> ParesDeVariantes(List<string> colores, List<string> tallas)
    {
        var pares = new List<(string, string)>();
        if (colores.Count == 0 && tallas.Count == 0) pares.Add((string.Empty, string.Empty));
        else if (tallas.Count == 0) pares.AddRange(colores.Select(c => (c, string.Empty)));
        else if (colores.Count == 0) pares.AddRange(tallas.Select(t => (string.Empty, t)));
        else
            foreach (var c in colores)
                foreach (var t in tallas)
                    pares.Add((c, t));

        if (pares.Count > Producto.MaximoVariantes)
            throw new ValidacionException(
                $"Son demasiadas combinaciones ({pares.Count}). El máximo es {Producto.MaximoVariantes}: probá con menos colores o tallas.");
        return pares;
    }

    private static void Aplicar(Producto p, DatosValidados d)
    {
        p.Nombre = d.Nombre;
        p.Descripcion = d.Descripcion;
        p.Precio = d.Precio;
        p.Costo = d.Costo;
        p.CategoriaId = d.CategoriaId;
        p.VisibleEnTienda = d.Visible;
        p.ControlaStock = d.ControlaStock;
        p.StockMinimo = d.StockMinimo;
    }

    // Las variantes NUNCA se borran (en M5 tendrán stock): las que ya no están se desactivan y, si la
    // dueña las vuelve a poner, se reactiva la MISMA fila. Mayúsculas/minúsculas no cuentan como otra.
    //
    // TRAMPA: los hijos NUEVOS de una entidad ya rastreada se agregan con _db.Variantes.Add(...), no
    // solo a la lista del padre. Como su Id ya viene asignado (Guid v7 en el inicializador), EF los
    // detecta por navegación como "modificados" (UPDATE de una fila que no existe) y no como nuevos.
    private void ReconciliarVariantes(Producto producto, List<(string Color, string Talla)> deseadas)
    {
        static string Clave(string c, string t) => $"{c.ToLowerInvariant()}\u001f{t.ToLowerInvariant()}";
        var porClave = producto.Variantes.ToDictionary(v => Clave(v.Color, v.Talla));
        var clavesDeseadas = new HashSet<string>();

        for (var i = 0; i < deseadas.Count; i++)
        {
            var (color, talla) = deseadas[i];
            var clave = Clave(color, talla);
            clavesDeseadas.Add(clave);
            if (porClave.TryGetValue(clave, out var existente))
            {
                existente.Activa = true;
                existente.Color = color; // por si cambió la forma de escribirlo ("rojo" → "Rojo")
                existente.Talla = talla;
                existente.Orden = i;
            }
            else
            {
                _db.Variantes.Add(new Variante { ProductoId = producto.Id, Color = color, Talla = talla, Orden = i });
            }
        }
        foreach (var v in producto.Variantes.Where(v => v.Activa && !clavesDeseadas.Contains(Clave(v.Color, v.Talla))))
            v.Activa = false;
    }

    // Deja las fotos en el orden pedido (la primera es la portada) y devuelve las imágenes que se sacaron.
    private List<Guid> ReconciliarFotos(Producto producto, List<Guid> deseadas)
    {
        var quitadas = producto.Fotos.Where(f => !deseadas.Contains(f.ImagenId)).ToList();
        _db.ProductoFotos.RemoveRange(quitadas);

        for (var i = 0; i < deseadas.Count; i++)
        {
            var existente = producto.Fotos.FirstOrDefault(f => f.ImagenId == deseadas[i]);
            if (existente is null) _db.ProductoFotos.Add(new ProductoFoto { ProductoId = producto.Id, ImagenId = deseadas[i], Orden = i }); // ver TRAMPA arriba
            else existente.Orden = i;
        }
        return quitadas.Select(f => f.ImagenId).ToList();
    }

    private async Task<ProductoDto> ADtoAsync(Guid tiendaId, Producto p, CancellationToken ct)
    {
        var ordenadas = p.Fotos.OrderBy(f => f.Orden).ToList();
        var ids = ordenadas.Select(f => f.ImagenId).ToList();
        var imagenes = await _db.Imagenes.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var fotos = ordenadas.Where(f => imagenes.ContainsKey(f.ImagenId))
            .Select(f => ImagenMapeo.ADto(_almacen, tiendaId, imagenes[f.ImagenId])).ToList();

        var variantes = p.Variantes.Where(v => v.Activa)
            .OrderBy(v => v.Orden).ThenBy(v => v.Id)
            .Select(v => new VarianteDto(v.Id, v.Color.Length == 0 ? null : v.Color, v.Talla.Length == 0 ? null : v.Talla))
            .ToList();

        return new ProductoDto(
            p.Id, p.Nombre, p.Descripcion, p.Precio, p.Costo,
            MargenProducto.Ganancia(p.Precio, p.Costo), MargenProducto.Estado(p.Precio, p.Costo),
            p.CategoriaId, p.VisibleEnTienda, p.Archivado, variantes, fotos, p.ControlaStock, p.StockMinimo);
    }
}
