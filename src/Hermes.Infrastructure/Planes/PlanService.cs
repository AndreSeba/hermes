using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Domain.Entities;
using Hermes.Domain.Enums;
using Hermes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hermes.Infrastructure.Planes;

// El plan de la tienda de la SESIÓN. Suscripcion y PagoPlan no llevan filtro global (las toca también el administrador sobre
// otras tiendas): acá cada consulta se acota a mano por el Id de la tienda de la sesión, que se lee a una variable ANTES de
// armar la consulta (como TiendaService).
public class PlanService : IPlanService
{
    private readonly HermesDbContext _db;
    private readonly PlanesDeTienda _planes;
    private readonly IAlmacenArchivos _almacen;
    private readonly ITiendaActual _sesion;
    private readonly TimeProvider _reloj;
    private readonly PlanesOptions _opciones;

    public PlanService(HermesDbContext db, PlanesDeTienda planes, IAlmacenArchivos almacen, ITiendaActual sesion, TimeProvider reloj, IOptions<PlanesOptions> opciones)
    {
        _db = db;
        _planes = planes;
        _almacen = almacen;
        _sesion = sesion;
        _reloj = reloj;
        _opciones = opciones.Value;
    }

    private Guid TiendaIdDeLaSesion() => _sesion.TiendaId ?? throw new SinTiendaEnSesionException();

    public async Task<PlanesDto> ListarPlanesAsync(CancellationToken ct)
    {
        TiendaIdDeLaSesion();
        var planes = await _db.Planes.AsNoTracking().Where(p => p.Activo).OrderBy(p => p.Orden).ToListAsync(ct);
        return new PlanesDto(planes.Select(PagoMapeo.ADto).ToList(), _opciones.Meses, _opciones.InstruccionesDePago);
    }

    public async Task<MiPlanDto> MiPlanAsync(CancellationToken ct)
    {
        var tiendaId = TiendaIdDeLaSesion();
        var (plan, suscripcion) = await _planes.VigenteAsync(tiendaId, ct);
        var uso = (await _planes.UsoAsync([tiendaId], ct))[tiendaId];

        var pendiente = await _db.PagosPlan.AsNoTracking().Include(p => p.Plan)
            .Where(p => p.TiendaId == tiendaId && p.Estado == EstadoPago.Pendiente).FirstOrDefaultAsync(ct);
        PagoPlanDto? pendienteDto = pendiente is null ? null : (await ADtosAsync([pendiente], ct))[0];

        return new MiPlanDto(PagoMapeo.ADto(plan), suscripcion?.Hasta is { } h ? PagoMapeo.Utc(h) : null, uso.Productos, PlanesDeTienda.AMb(uso.Bytes), pendienteDto);
    }

    public async Task<PagoPlanDto> SolicitarAsync(SolicitarPlanDto dto, CancellationToken ct)
    {
        var tiendaId = TiendaIdDeLaSesion();
        if (!_opciones.Meses.Contains(dto.Meses))
            throw new ValidacionException($"Elegí por cuántos meses: {string.Join(", ", _opciones.Meses)}.");
        if (dto.Metodo is not ("Qr" or "Transferencia")) throw new ValidacionException("Elegí cómo pagaste: QR o transferencia.");

        var plan = await _db.Planes.AsNoTracking().FirstOrDefaultAsync(p => p.Id == dto.PlanId, ct) ?? throw new PlanNoEncontradoException(dto.PlanId);
        if (!plan.Activo || plan.PrecioMensual <= 0) throw new ValidacionException("Ese plan no se paga: elegí uno de los que tienen precio.");

        // El comprobante tiene que ser una foto de ESTA tienda (el filtro global de Imagen lo garantiza).
        if (!await _db.Imagenes.AnyAsync(i => i.Id == dto.ComprobanteImagenId, ct)) throw new ValidacionException("Subí la foto del comprobante de tu pago.");

        if (await _db.PagosPlan.AnyAsync(p => p.TiendaId == tiendaId && p.Estado == EstadoPago.Pendiente, ct)) throw new PagoPendienteExistenteException();

        var pago = new PagoPlan
        {
            TiendaId = tiendaId, PlanId = plan.Id, Meses = dto.Meses, Monto = plan.PrecioMensual * dto.Meses, Metodo = dto.Metodo,
            ComprobanteImagenId = dto.ComprobanteImagenId, CreadoEn = _reloj.GetUtcNow().UtcDateTime,
        };
        try
        {
            _db.PagosPlan.Add(pago);
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Dos pedidos a la vez: el índice único de "una pendiente por tienda" corta al segundo.
            _db.ChangeTracker.Clear();
            if (await _db.PagosPlan.AnyAsync(p => p.TiendaId == tiendaId && p.Estado == EstadoPago.Pendiente, ct)) throw new PagoPendienteExistenteException();
            throw;
        }
        pago.Plan = plan;
        return (await ADtosAsync([pago], ct))[0];
    }

    public async Task<IReadOnlyList<PagoPlanDto>> MisPagosAsync(CancellationToken ct)
    {
        var tiendaId = TiendaIdDeLaSesion();
        var pagos = await _db.PagosPlan.AsNoTracking().Include(p => p.Plan).Where(p => p.TiendaId == tiendaId)
            .OrderByDescending(p => p.Id).Take(20).ToListAsync(ct);
        return await ADtosAsync(pagos, ct);
    }

    private async Task<List<PagoPlanDto>> ADtosAsync(List<PagoPlan> pagos, CancellationToken ct)
    {
        var ids = pagos.Where(p => p.ComprobanteImagenId != null).Select(p => p.ComprobanteImagenId!.Value).ToList();
        var imagenes = await _db.Imagenes.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        return pagos.Select(p => PagoMapeo.ADto(_almacen, p, p.Plan!.Nombre, p.ComprobanteImagenId is { } id ? imagenes.GetValueOrDefault(id) : null)).ToList();
    }
}

