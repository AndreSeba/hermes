using Hermes.Application.Dtos;
using Hermes.Application.Interfaces;
using Hermes.Domain.Entities;
using Hermes.Infrastructure.Imagenes;

namespace Hermes.Infrastructure.Planes;

internal static class PagoMapeo
{
    public static DateTimeOffset Utc(DateTime d) => new(DateTime.SpecifyKind(d, DateTimeKind.Utc));

    public static PlanDto ADto(Plan p) => new(p.Id, p.Codigo, p.Nombre, p.PrecioMensual, p.MaxProductos, p.MaxAlmacenamientoMb, p.Activo);

    // `comprobante`: la imagen ya cargada (puede ser null si se borró). La dirección de la foto usa la tienda DEL PAGO.
    public static PagoPlanDto ADto(IAlmacenArchivos almacen, PagoPlan p, string plan, Imagen? comprobante) => new(
        p.Id, p.PlanId, plan, p.Meses, p.Monto, p.Metodo, p.Estado, p.Motivo,
        comprobante is null ? null : ImagenMapeo.ADto(almacen, p.TiendaId, comprobante), Utc(p.CreadoEn), p.ResueltoEn is { } r ? Utc(r) : null);
}
