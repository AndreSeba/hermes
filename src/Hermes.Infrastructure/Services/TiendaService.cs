using Hermes.Application;
using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Domain.Entities;
using Hermes.Infrastructure.Imagenes;
using Hermes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hermes.Infrastructure.Services;

// Tienda NO tiene filtro global (es el tenant): cada consulta se acota a mano por el Id de la
// tienda de la SESIÓN. Nunca se recibe un id de tienda por parámetro.
public class TiendaService : ITiendaService
{
    private readonly HermesDbContext _db;
    private readonly IImagenService _imagenes;
    private readonly IAlmacenArchivos _almacen;
    private readonly ITiendaActual _sesion;

    public TiendaService(HermesDbContext db, IImagenService imagenes, IAlmacenArchivos almacen, ITiendaActual sesion)
    {
        _db = db;
        _imagenes = imagenes;
        _almacen = almacen;
        _sesion = sesion;
    }

    // Siempre se lee a una variable ANTES de armar la consulta: si el getter lanza dentro de una
    // expresión LINQ, EF envuelve la excepción en InvalidOperationException y el 403 sale como 500.
    private Guid TiendaIdDeLaSesion() => _sesion.TiendaId ?? throw new SinTiendaEnSesionException();

    public async Task<TiendaDto> ObtenerAsync(CancellationToken ct)
    {
        var tiendaId = TiendaIdDeLaSesion();
        var tienda = await _db.Tiendas.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tiendaId, ct)
            ?? throw new SesionInvalidaException();
        return await ADtoAsync(tienda, ct);
    }

    public async Task<TiendaDto> ActualizarAsync(ActualizarTiendaDto dto, CancellationToken ct)
    {
        var tiendaId = TiendaIdDeLaSesion();
        var nombre = Validacion.Texto(dto.Nombre, 120, "El nombre de tu negocio");
        var descripcion = Validacion.TextoOpcional(dto.Descripcion, 500, "La descripción");
        var whatsapp = Validacion.Whatsapp(dto.Whatsapp);
        var color = Validacion.ColorHex(dto.Color);

        var tienda = await _db.Tiendas.FirstOrDefaultAsync(t => t.Id == tiendaId, ct)
            ?? throw new SesionInvalidaException();

        // El logo tiene que ser una imagen de ESTA tienda (el filtro global de Imagen lo garantiza).
        if (dto.LogoImagenId is { } logoId && !await _db.Imagenes.AnyAsync(i => i.Id == logoId, ct))
            throw new ImagenNoEncontradaException(logoId);

        var logoAnterior = tienda.LogoImagenId;
        tienda.Nombre = nombre;
        tienda.Descripcion = descripcion;
        tienda.Whatsapp = whatsapp;
        tienda.Color = color;
        tienda.LogoImagenId = dto.LogoImagenId;
        await _db.SaveChangesAsync(ct);

        // Primero se guarda la tienda ya apuntando al logo nuevo y recién después se borra el viejo:
        // si algo falla en el medio queda una foto de más, nunca una tienda sin logo.
        if (logoAnterior is { } viejo && viejo != dto.LogoImagenId)
            await _imagenes.BorrarAsync(viejo, ct);

        return await ADtoAsync(tienda, ct);
    }

    public async Task<TiendaDto> CambiarDireccionAsync(CambiarDireccionDto dto, CancellationToken ct)
    {
        var tiendaId = TiendaIdDeLaSesion();
        var slug = Validacion.Slug(dto.Slug);
        var tienda = await _db.Tiendas.FirstOrDefaultAsync(t => t.Id == tiendaId, ct)
            ?? throw new SesionInvalidaException();

        if (tienda.Slug != slug)
        {
            if (await _db.Tiendas.AnyAsync(t => t.Slug == slug && t.Id != tienda.Id, ct))
                throw new SlugNoDisponibleException(slug);

            // Los enlaces que ya compartió con la dirección vieja dejan de funcionar. Pendiente (M10):
            // guardar las direcciones anteriores para redirigirlas.
            tienda.Slug = slug;
            await _db.SaveChangesAsync(ct);
        }
        return await ADtoAsync(tienda, ct);
    }

    private async Task<TiendaDto> ADtoAsync(Tienda t, CancellationToken ct)
    {
        ImagenDto? logo = null;
        if (t.LogoImagenId is { } id)
        {
            var imagen = await _db.Imagenes.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
            if (imagen is not null) logo = ImagenMapeo.ADto(_almacen, t.Id, imagen);
        }
        return new TiendaDto(t.Id, t.Nombre, t.Slug, t.Descripcion, t.Whatsapp, t.Color, logo);
    }
}
