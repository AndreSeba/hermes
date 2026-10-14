using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Domain.Entities;
using Hermes.Domain.Enums;
using Hermes.Infrastructure.Imagenes;
using Hermes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hermes.Infrastructure.Services;

public class ImagenService : IImagenService
{
    private readonly HermesDbContext _db;
    private readonly IAlmacenArchivos _almacen;
    private readonly ITiendaActual _tienda;
    private readonly TimeProvider _reloj;

    public ImagenService(HermesDbContext db, IAlmacenArchivos almacen, ITiendaActual tienda, TimeProvider reloj)
    {
        _db = db;
        _almacen = almacen;
        _tienda = tienda;
        _reloj = reloj;
    }

    public async Task<ImagenDto> SubirAsync(byte[] contenido, CancellationToken ct)
    {
        var tiendaId = _tienda.TiendaId ?? throw new SinTiendaEnSesionException();

        // Decodificar y achicar cuesta CPU: se hace antes de tocar la base.
        var procesada = await Task.Run(() => ProcesadorImagenes.Procesar(contenido), ct);

        var imagen = new Imagen
        {
            AnchoOriginal = procesada.Ancho,
            AltoOriginal = procesada.Alto,
            BytesGuardados = procesada.Tamanos.Values.Sum(b => b.Length),
            CreadaEn = _reloj.GetUtcNow(),
        };
        _db.Imagenes.Add(imagen);

        foreach (var (tamano, bytes) in procesada.Tamanos)
            await _almacen.GuardarAsync(Imagen.ClaveDe(tiendaId, imagen.Id, tamano), bytes, "image/webp", ct);

        // Imagen y archivos se guardan juntos o no se guarda nada.
        await _db.SaveChangesAsync(ct);
        return ImagenMapeo.ADto(_almacen, tiendaId, imagen);
    }

    public async Task BorrarAsync(Guid id, CancellationToken ct)
    {
        var tiendaId = _tienda.TiendaId ?? throw new SinTiendaEnSesionException();
        var imagen = await _db.Imagenes.FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new ImagenNoEncontradaException(id);

        await _almacen.BorrarAsync(Enum.GetValues<TamanoImagen>().Select(t => Imagen.ClaveDe(tiendaId, imagen.Id, t)), ct);
        _db.Imagenes.Remove(imagen);
        await _db.SaveChangesAsync(ct);
    }
}
