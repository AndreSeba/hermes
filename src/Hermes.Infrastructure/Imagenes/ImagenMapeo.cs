using Hermes.Application.Dtos;
using Hermes.Application.Interfaces;
using Hermes.Domain.Entities;
using Hermes.Domain.Enums;

namespace Hermes.Infrastructure.Imagenes;

// De una Imagen a su DTO con las tres urls. Lo usan todos los servicios que muestran fotos
// (Mi tienda, Catálogo…): así la url sale SIEMPRE de IAlmacenArchivos.Url y cuando las fotos pasen
// a Cloudflare R2 cambia en un solo lugar.
public static class ImagenMapeo
{
    public static ImagenDto ADto(IAlmacenArchivos almacen, Guid tiendaId, Imagen i) => new(
        i.Id, i.AnchoOriginal, i.AltoOriginal,
        almacen.Url(Imagen.ClaveDe(tiendaId, i.Id, TamanoImagen.Miniatura)),
        almacen.Url(Imagen.ClaveDe(tiendaId, i.Id, TamanoImagen.Catalogo)),
        almacen.Url(Imagen.ClaveDe(tiendaId, i.Id, TamanoImagen.Detalle)));
}
