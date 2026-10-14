using Hermes.Application;
using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Instagram;
using Hermes.Application.Interfaces;
using Hermes.Domain.Entities;
using Hermes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hermes.Infrastructure.Services;

// Borrador es de la tienda (filtro global): nada acá filtra por tienda a mano. Leer un texto es lógica pura
// (ParserPublicacion); lo único que se guarda son borradores PENDIENTES: al catálogo solo llega lo que la dueña publica.
public class InstagramService : IInstagramService
{
    private const int LimitePorDefecto = 30;
    private const int LimiteMaximo = 100;

    private readonly HermesDbContext _db;
    private readonly IProductoService _productos;
    private readonly IStockService _stock;
    private readonly ITiendaActual _sesion;
    private readonly TimeProvider _reloj;

    public InstagramService(HermesDbContext db, IProductoService productos, IStockService stock, ITiendaActual sesion, TimeProvider reloj)
    {
        _db = db;
        _productos = productos;
        _stock = stock;
        _sesion = sesion;
        _reloj = reloj;
    }

    private void ExigirTienda() => _ = _sesion.TiendaId ?? throw new SinTiendaEnSesionException();

    // ───────────── Leer ─────────────

    public LecturaDto Leer(LeerTextoDto dto)
    {
        ExigirTienda();
        var lectura = ParserPublicacion.Leer(ValidarTexto(dto));
        return new LecturaDto(lectura.Productos.Select(ADto).ToList(), lectura.Hashtags);
    }

    private static string ValidarTexto(LeerTextoDto dto)
    {
        var texto = (dto?.Texto ?? string.Empty).Trim();
        if (texto.Length == 0) throw new ValidacionException("Pegá el texto de la publicación.");
        if (texto.Length > ParserPublicacion.MaximoTexto)
            throw new ValidacionException($"El texto es muy largo (máximo {ParserPublicacion.MaximoTexto:N0} caracteres).");
        return texto;
    }

    private static ProductoLeidoDto ADto(ProductoLeido p) => new(
        p.Nombre, p.Precio, p.PrecioAnterior, p.Colores, p.Tallas, p.Stock, p.Categoria, p.Descripcion, p.Avisos, Faltan(p.Nombre, p.Precio));

    // Para publicar hacen falta el nombre y el precio.
    private static List<string> Faltan(string? nombre, decimal? precio)
    {
        var faltan = new List<string>();
        if (string.IsNullOrWhiteSpace(nombre)) faltan.Add("nombre");
        if (precio is not > 0) faltan.Add("precio");
        return faltan;
    }

    // ───────────── Importar ─────────────

    public async Task<IReadOnlyList<BorradorDto>> ImportarAsync(LeerTextoDto dto, CancellationToken ct)
    {
        ExigirTienda();
        var texto = ValidarTexto(dto);
        var productos = ParserPublicacion.Leer(texto).Productos.Where(p => !string.IsNullOrWhiteSpace(p.Nombre)).ToList();
        if (productos.Count == 0)
            throw new ValidacionException("No pudimos leer ningún producto de ese texto. Probá en \"Probá tu formato\": el nombre va en la primera línea.");

        var pendientes = await _db.Borradores.CountAsync(b => b.Estado == EstadoBorrador.Pendiente, ct);
        if (pendientes + productos.Count > Borrador.MaximoPendientes)
            throw new ValidacionException($"Tenés {pendientes} productos esperando en la bandeja (máximo {Borrador.MaximoPendientes}). Revisalos o descartalos antes de agregar más.");

        var ahora = _reloj.GetUtcNow();
        // Los de una misma publicación nacen en el mismo milisegundo y sus Guid v7 no tendrían orden: se escalonan para que
        // en la bandeja (más nuevo primero) aparezcan en el orden de la publicación (el 1 arriba, después el 2…).
        var nuevos = productos.Select((p, i) => new Borrador
        {
            Id = Guid.CreateVersion7(ahora.AddMilliseconds(productos.Count - i)),
            Texto = texto, Nombre = p.Nombre!, Precio = p.Precio, PrecioAnterior = p.PrecioAnterior,
            Colores = string.Join(", ", p.Colores), Tallas = string.Join(", ", p.Tallas), Stock = p.Stock,
            Categoria = p.Categoria, Descripcion = p.Descripcion, Avisos = string.Join("\n", p.Avisos), CreadoEn = ahora,
        }).ToList();
        _db.Borradores.AddRange(nuevos);
        await _db.SaveChangesAsync(ct);
        return nuevos.Select(ADto).ToList();
    }

    // ───────────── Bandeja ─────────────

    public async Task<PaginaDeBorradoresDto> ListarAsync(FiltroBorradoresDto filtro, CancellationToken ct)
    {
        ExigirTienda();
        if (!Enum.IsDefined(filtro.Estado)) throw new ValidacionException("El estado pedido no existe.");
        var limite = Math.Clamp(filtro.Limite ?? LimitePorDefecto, 1, LimiteMaximo);
        var estado = filtro.Estado;

        var consulta = _db.Borradores.AsNoTracking().Where(b => b.Estado == estado);
        if (filtro.Cursor is { } cursor) consulta = consulta.Where(b => b.Id.CompareTo(cursor) < 0);
        var pagina = await consulta.OrderByDescending(b => b.Id).Take(limite + 1).ToListAsync(ct);
        var hayMas = pagina.Count > limite;
        if (hayMas) pagina.RemoveAt(pagina.Count - 1);

        var pendientes = await _db.Borradores.CountAsync(b => b.Estado == EstadoBorrador.Pendiente, ct);
        return new PaginaDeBorradoresDto(pagina.Select(ADto).ToList(), hayMas ? pagina[^1].Id : null, pendientes);
    }

    public async Task<BorradorDto> ObtenerAsync(Guid id, CancellationToken ct)
    {
        ExigirTienda();
        var b = await _db.Borradores.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new BorradorNoEncontradoException(id);
        return ADto(b);
    }

    public async Task<BorradorDto> DescartarAsync(Guid id, CancellationToken ct)
    {
        ExigirTienda();
        var ahora = _reloj.GetUtcNow();
        var marcados = await _db.Borradores.Where(b => b.Id == id && b.Estado == EstadoBorrador.Pendiente)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Estado, EstadoBorrador.Descartado).SetProperty(b => b.ResueltoEn, ahora), ct);
        var borrador = await _db.Borradores.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new BorradorNoEncontradoException(id);
        if (marcados == 0) throw new BorradorYaResueltoException();
        return ADto(borrador);
    }

    // ───────────── Publicar ─────────────

    public async Task<PublicacionDto> PublicarAsync(Guid id, PublicarBorradorDto dto, CancellationToken ct)
    {
        ExigirTienda();
        if (dto?.Producto is null) throw new ValidacionException("Falta el producto a publicar.");
        if (!await _db.Borradores.AnyAsync(b => b.Id == id, ct)) throw new BorradorNoEncontradoException(id);

        // Primero se "reserva" el borrador con un UPDATE condicional: con dos pedidos simultáneos solo uno lo publica
        // (el otro recibe 409) y no se crean dos productos.
        var ahora = _reloj.GetUtcNow();
        var reservados = await _db.Borradores.Where(b => b.Id == id && b.Estado == EstadoBorrador.Pendiente)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Estado, EstadoBorrador.Publicado).SetProperty(b => b.ResueltoEn, ahora), ct);
        if (reservados == 0) throw new BorradorYaResueltoException();

        ProductoDto producto;
        try
        {
            producto = await _productos.CrearAsync(dto.Producto, ct);
        }
        catch
        {
            // Si el producto no se pudo crear (un precio mal, una foto que ya no está…), el borrador vuelve a la bandeja.
            _db.ChangeTracker.Clear();
            await _db.Borradores.Where(b => b.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.Estado, EstadoBorrador.Pendiente).SetProperty(b => b.ResueltoEn, (DateTimeOffset?)null), CancellationToken.None);
            throw;
        }
        await _db.Borradores.Where(b => b.Id == id).ExecuteUpdateAsync(s => s.SetProperty(b => b.ProductoId, producto.Id), ct);

        var (cargado, aviso) = await CargarStockAsync(producto, dto.Stock, ct);
        return new PublicacionDto(producto, cargado, aviso);
    }

    // "Quedan 5" de la publicación. Solo se carga si el producto lleva control y tiene UNA sola variante; con varias no se
    // sabe cuántas son de cada color o talla, y se le dice a la dueña dónde cargarlas.
    private async Task<(bool Cargado, string? Aviso)> CargarStockAsync(ProductoDto producto, int? stock, CancellationToken ct)
    {
        if (stock is not > 0) return (false, null);
        if (!producto.ControlaStock) return (false, "Este producto no lleva control de stock, así que no cargamos cuántos quedan.");
        if (producto.Variantes.Count != 1)
            return (false, $"La publicación decía que quedaban {stock}, pero hay varias opciones: cargá cuántas hay de cada una en Stock.");
        try
        {
            await _stock.IngresarAsync(new IngresoDto(producto.Id, [new LineaIngresoDto(producto.Variantes[0].Id, stock.Value)], null, "De la publicación de Instagram"), ct);
            return (true, null);
        }
        catch (DominioException ex)
        {
            return (false, $"El producto se publicó, pero no pudimos cargar el stock: {ex.Message}");
        }
    }

    // ───────────── DTOs ─────────────

    private static BorradorDto ADto(Borrador b) => new(
        b.Id, b.Estado, b.Origen, b.Texto, b.Nombre, b.Precio, b.PrecioAnterior, Partir(b.Colores), Partir(b.Tallas), b.Stock,
        b.Categoria, b.Descripcion, b.Avisos.Length == 0 ? [] : b.Avisos.Split('\n'), Faltan(b.Nombre, b.Precio), b.ProductoId, b.CreadoEn);

    private static List<string> Partir(string lista) => lista.Length == 0 ? [] : lista.Split(", ").ToList();
}
