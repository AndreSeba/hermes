using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;

namespace Hermes.Infrastructure.Planes;

// Los límites del plan VIGENTE de la tienda de la sesión. Nunca borra ni oculta nada: solo impide CREAR más. Si un plan
// vence y la tienda queda por encima del límite del Gratis, conserva todo lo que ya tiene (y lo sigue vendiendo); lo único
// que no puede es agregar más hasta volver a un plan que lo permita.
public class LimitesDePlan : ILimitesDePlan
{
    private readonly PlanesDeTienda _planes;
    private readonly ITiendaActual _sesion;

    public LimitesDePlan(PlanesDeTienda planes, ITiendaActual sesion)
    {
        _planes = planes;
        _sesion = sesion;
    }

    public async Task ExigirPoderCrearProductoAsync(CancellationToken ct)
    {
        var tiendaId = _sesion.TiendaId ?? throw new SinTiendaEnSesionException();
        var (plan, _) = await _planes.VigenteAsync(tiendaId, ct);
        if (plan.MaxProductos is not { } maximo) return;

        var uso = (await _planes.UsoAsync([tiendaId], ct))[tiendaId];
        if (uso.Productos >= maximo)
            throw new LimiteDePlanException(
                $"Llegaste al límite de tu plan {plan.Nombre} ({maximo} productos). Mirá los planes en Mi plan para poder cargar más.");
    }

    public async Task ExigirAlmacenamientoAsync(long bytesNuevos, CancellationToken ct)
    {
        var tiendaId = _sesion.TiendaId ?? throw new SinTiendaEnSesionException();
        var (plan, _) = await _planes.VigenteAsync(tiendaId, ct);
        if (plan.MaxAlmacenamientoMb is not { } maximoMb) return;

        var uso = (await _planes.UsoAsync([tiendaId], ct))[tiendaId];
        if (uso.Bytes + bytesNuevos > maximoMb * 1_048_576L)
            throw new LimiteDePlanException(
                $"Se llenó el espacio de fotos de tu plan {plan.Nombre} ({maximoMb} MB). Borrá fotos que no uses o mirá los planes en Mi plan.");
    }
}
