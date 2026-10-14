using Hermes.Application;
using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Domain.Entities;
using Hermes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hermes.Infrastructure.Services;

// Proveedor, Compra y CompraLinea son de la tienda (filtro global): nada acá filtra por tienda a mano. Lo que se
// debe y lo que está en camino se suman EN MEMORIA (SQLite no suma decimales; son pocas filas por proveedor).
public class ProveedorService : IProveedorService
{
    private const int MaximoListado = 500;

    private readonly HermesDbContext _db;
    private readonly ITiendaActual _sesion;

    public ProveedorService(HermesDbContext db, ITiendaActual sesion)
    {
        _db = db;
        _sesion = sesion;
    }

    private void ExigirTienda() => _ = _sesion.TiendaId ?? throw new SinTiendaEnSesionException();

    public async Task<IReadOnlyList<ProveedorDto>> ListarAsync(bool incluirInactivos, CancellationToken ct)
    {
        ExigirTienda();
        var consulta = _db.Proveedores.AsNoTracking().AsQueryable();
        if (!incluirInactivos) consulta = consulta.Where(p => p.Activo);
        var proveedores = await consulta.OrderBy(p => p.Nombre).Take(MaximoListado).ToListAsync(ct);
        var cuentas = await CuentasAsync(proveedores.Select(p => p.Id).ToList(), ct);
        return proveedores.Select(p => ADto(p, cuentas.GetValueOrDefault(p.Id))).ToList();
    }

    public async Task<ProveedorDetalleDto> ObtenerAsync(Guid id, CancellationToken ct)
    {
        ExigirTienda();
        var proveedor = await _db.Proveedores.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new ProveedorNoEncontradoException(id);
        var cuentas = await CuentasAsync([id], ct);

        // Lo último por producto: la línea de la compra más nueva (las anuladas no cuentan).
        var lineas = await _db.CompraLineas.AsNoTracking()
            .Where(l => l.Compra!.ProveedorId == id && !l.Compra.Anulada)
            .Select(l => new { l.ProductoId, l.CostoUnitario, l.CompraId, l.Compra!.Fecha })
            .ToListAsync(ct);
        var ultimas = lineas.GroupBy(l => l.ProductoId)
            .Select(g => g.OrderByDescending(l => l.CompraId).First()).ToList();
        var ids = ultimas.Select(u => u.ProductoId).ToList();
        var nombres = await _db.Productos.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Nombre, ct);
        var precios = ultimas
            .Select(u => new UltimoPrecioDto(u.ProductoId, nombres.GetValueOrDefault(u.ProductoId, "(producto)"), u.CostoUnitario, Utc(u.Fecha)))
            .OrderBy(u => u.Producto).ToList();

        return new ProveedorDetalleDto(ADto(proveedor, cuentas.GetValueOrDefault(id)), precios);
    }

    public async Task<ProveedorDto> CrearAsync(GuardarProveedorDto dto, CancellationToken ct)
    {
        ExigirTienda();
        var (nombre, telefono, notas) = Validar(dto);
        await ExigirNombreLibreAsync(nombre, null, ct);

        var proveedor = new Proveedor { Nombre = nombre, Telefono = telefono, Notas = notas };
        _db.Proveedores.Add(proveedor);
        await GuardarAsync(proveedor.Nombre, ct);
        return ADto(proveedor, null);
    }

    public async Task<ProveedorDto> ActualizarAsync(Guid id, GuardarProveedorDto dto, CancellationToken ct)
    {
        ExigirTienda();
        var (nombre, telefono, notas) = Validar(dto);
        var proveedor = await _db.Proveedores.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new ProveedorNoEncontradoException(id);
        await ExigirNombreLibreAsync(nombre, id, ct);

        proveedor.Nombre = nombre;
        proveedor.Telefono = telefono;
        proveedor.Notas = notas;
        await GuardarAsync(nombre, ct);
        return ADto(proveedor, (await CuentasAsync([id], ct)).GetValueOrDefault(id));
    }

    public async Task<ProveedorDto> CambiarActivoAsync(Guid id, bool activo, CancellationToken ct)
    {
        ExigirTienda();
        var proveedor = await _db.Proveedores.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new ProveedorNoEncontradoException(id);
        proveedor.Activo = activo;
        await _db.SaveChangesAsync(ct);
        return ADto(proveedor, (await CuentasAsync([id], ct)).GetValueOrDefault(id));
    }

    // ───────────── Interno ─────────────

    private static (string Nombre, string? Telefono, string? Notas) Validar(GuardarProveedorDto dto)
        => (Validacion.Texto(dto.Nombre, 120, "El nombre"), Validacion.Whatsapp(dto.Telefono), Validacion.TextoOpcional(dto.Notas, 300, "Las notas"));

    private async Task ExigirNombreLibreAsync(string nombre, Guid? salvo, CancellationToken ct)
    {
        var minuscula = nombre.ToLower();
        if (await _db.Proveedores.AnyAsync(p => p.Nombre.ToLower() == minuscula && p.Id != salvo, ct))
            throw new ProveedorDuplicadoException(nombre);
    }

    // Si dos pedidos crean el mismo nombre a la vez, el índice único corta al segundo: mismo 409 que el chequeo.
    private async Task GuardarAsync(string nombre, CancellationToken ct)
    {
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            if (await _db.Proveedores.AnyAsync(p => p.Nombre.ToLower() == nombre.ToLower(), ct)) throw new ProveedorDuplicadoException(nombre);
            throw;
        }
    }

    private record Cuenta(decimal PorPagar, int EnCamino, int Compras);

    private async Task<Dictionary<Guid, Cuenta>> CuentasAsync(List<Guid> proveedorIds, CancellationToken ct)
    {
        var filas = await _db.CompraLineas.AsNoTracking()
            .Where(l => !l.Compra!.Anulada && proveedorIds.Contains(l.Compra.ProveedorId))
            .Select(l => new { l.Compra!.ProveedorId, l.CompraId, l.Compra.Recibida, l.Compra.Pagada, l.Cantidad, l.CostoUnitario })
            .ToListAsync(ct);
        return filas.GroupBy(f => f.ProveedorId).ToDictionary(g => g.Key, g =>
        {
            var compras = g.GroupBy(f => f.CompraId).ToList();
            return new Cuenta(
                g.Where(f => !f.Pagada).Sum(f => f.Cantidad * f.CostoUnitario),
                compras.Count(c => !c.First().Recibida),
                compras.Count);
        });
    }

    private static ProveedorDto ADto(Proveedor p, Cuenta? c)
        => new(p.Id, p.Nombre, p.Telefono, p.Notas, p.Activo, c?.PorPagar ?? 0, c?.EnCamino ?? 0, c?.Compras ?? 0);

    private static DateTimeOffset Utc(DateTime d) => new(DateTime.SpecifyKind(d, DateTimeKind.Utc));
}
