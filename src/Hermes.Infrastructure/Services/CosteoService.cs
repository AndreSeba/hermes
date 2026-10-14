using Hermes.Application;
using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Domain;
using Hermes.Domain.Entities;
using Hermes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hermes.Infrastructure.Services;

// Costeo y Producto son de la tienda (filtro global): nada acá filtra por tienda a mano.
public class CosteoService : ICosteoService
{
    private readonly HermesDbContext _db;
    private readonly ITiendaActual _sesion;
    private readonly TimeProvider _reloj;

    public CosteoService(HermesDbContext db, ITiendaActual sesion, TimeProvider reloj)
    {
        _db = db;
        _sesion = sesion;
        _reloj = reloj;
    }

    private void ExigirTienda() => _ = _sesion.TiendaId ?? throw new SinTiendaEnSesionException();

    public async Task<CosteoDto> ObtenerAsync(Guid productoId, CancellationToken ct)
    {
        ExigirTienda();
        var producto = await _db.Productos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productoId, ct)
            ?? throw new ProductoNoEncontradoException(productoId);
        var costeo = await _db.Costeos.AsNoTracking().FirstOrDefaultAsync(c => c.ProductoId == productoId, ct);
        return costeo is null
            ? ADto(false, 0, 0, 0, 0, ReglaCosteo.MargenPorDefecto, producto.Costo, producto.Precio)
            : ADto(true, costeo.Material, costeo.Empaque, costeo.ManoDeObra, costeo.CostoFijo, costeo.MargenObjetivo,
                ReglaCosteo.Total(costeo.Material, costeo.Empaque, costeo.ManoDeObra, costeo.CostoFijo), producto.Precio);
    }

    public async Task<CosteoDto> CalcularAsync(Guid productoId, GuardarCosteoDto dto, CancellationToken ct)
    {
        ExigirTienda();
        var producto = await _db.Productos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productoId, ct)
            ?? throw new ProductoNoEncontradoException(productoId);
        var datos = Validar(dto);
        return ADto(true, datos.Material, datos.Empaque, datos.ManoDeObra, datos.CostoFijo, datos.Margen, datos.Total, producto.Precio);
    }

    public async Task<CosteoDto> GuardarAsync(Guid productoId, GuardarCosteoDto dto, CancellationToken ct)
    {
        ExigirTienda();
        var datos = Validar(dto);
        var producto = await _db.Productos.FirstOrDefaultAsync(p => p.Id == productoId, ct)
            ?? throw new ProductoNoEncontradoException(productoId);

        try
        {
            var costeo = await _db.Costeos.FirstOrDefaultAsync(c => c.ProductoId == productoId, ct);
            if (costeo is null)
            {
                costeo = new Costeo { ProductoId = productoId };
                _db.Costeos.Add(costeo);
            }
            costeo.Material = datos.Material;
            costeo.Empaque = datos.Empaque;
            costeo.ManoDeObra = datos.ManoDeObra;
            costeo.CostoFijo = datos.CostoFijo;
            costeo.MargenObjetivo = datos.Margen;

            // El costo del producto es el total del costeo. El PRECIO no se toca: lo cambia la dueña a mano.
            producto.Costo = datos.Total;
            producto.ActualizadoEn = _reloj.GetUtcNow();
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Dos pedidos creando el primer costeo a la vez: el índice único corta al segundo.
            _db.ChangeTracker.Clear();
            throw new ValidacionException("El costeo se estaba guardando desde otro lado. Probá de nuevo.");
        }
        return ADto(true, datos.Material, datos.Empaque, datos.ManoDeObra, datos.CostoFijo, datos.Margen, datos.Total, producto.Precio);
    }

    public async Task QuitarAsync(Guid productoId, CancellationToken ct)
    {
        ExigirTienda();
        if (!await _db.Productos.AnyAsync(p => p.Id == productoId, ct)) throw new ProductoNoEncontradoException(productoId);
        // Idempotente: sin costeo no hay nada que quitar. El producto se queda con el último costo total.
        await _db.Costeos.Where(c => c.ProductoId == productoId).ExecuteDeleteAsync(ct);
    }

    private record Datos(decimal Material, decimal Empaque, decimal ManoDeObra, decimal CostoFijo, int Margen, decimal Total);

    private static Datos Validar(GuardarCosteoDto dto)
    {
        var material = Validacion.Monto(dto.Material, "El material", permitirCero: true);
        var empaque = Validacion.Monto(dto.Empaque, "El empaque", permitirCero: true);
        var manoDeObra = Validacion.Monto(dto.ManoDeObra, "La mano de obra", permitirCero: true);
        var costoFijo = Validacion.Monto(dto.CostoFijo, "El costo fijo", permitirCero: true);
        if (dto.MargenObjetivo < ReglaCosteo.MargenMinimo || dto.MargenObjetivo > ReglaCosteo.MargenMaximo)
            throw new ValidacionException($"El margen que querés ganar tiene que estar entre {ReglaCosteo.MargenMinimo} y {ReglaCosteo.MargenMaximo} %.");
        var total = ReglaCosteo.Total(material, empaque, manoDeObra, costoFijo);
        if (total <= 0) throw new ValidacionException("Cargá al menos un costo (material, empaque, mano de obra o costo fijo).");
        if (total > 1_000_000m) throw new ValidacionException("El costo total es demasiado alto.");
        return new Datos(material, empaque, manoDeObra, costoFijo, dto.MargenObjetivo, total);
    }

    private static CosteoDto ADto(bool tiene, decimal material, decimal empaque, decimal manoDeObra, decimal costoFijo, int margen, decimal total, decimal precio)
    {
        var sugerido = ReglaCosteo.PrecioSugerido(total, margen);
        return new CosteoDto(
            tiene, material, empaque, manoDeObra, costoFijo, margen, total, sugerido, sugerido - total,
            precio, MargenProducto.Ganancia(precio, total),
            total > 0 ? Math.Round((precio - total) / precio * 100, 1) : null,
            MargenProducto.Estado(precio, total));
    }
}
