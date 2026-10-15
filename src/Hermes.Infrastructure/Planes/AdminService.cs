using Hermes.Application;
using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Domain.Entities;
using Hermes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hermes.Infrastructure.Planes;

// El panel del administrador de la PLATAFORMA. Mira y cambia OTRAS tiendas, así que NO usa el filtro global de tienda: cada
// consulta acota por el Id de tienda que corresponde (Tienda, Suscripcion y PagoPlan no tienen filtro; Producto e Imagen sí,
// y por eso se leen con IgnoreQueryFilters + el Id explícito). Quién puede llamarlo lo decide la política "Admin" del controller
// (claim `rol` del token, que solo se emite si el usuario es administrador en la base).
public class AdminService : IAdminService
{
    private const int LimitePorDefecto = 30;
    private const int LimiteMaximo = 100;
    private const int MaximoMeses = 36;

    private readonly HermesDbContext _db;
    private readonly PlanesDeTienda _planes;
    private readonly IAlmacenArchivos _almacen;
    private readonly TimeProvider _reloj;

    public AdminService(HermesDbContext db, PlanesDeTienda planes, IAlmacenArchivos almacen, TimeProvider reloj)
    {
        _db = db;
        _planes = planes;
        _almacen = almacen;
        _reloj = reloj;
    }

    // ───────────── Pagos ─────────────

    public async Task<PaginaDePagosDto> PagosAsync(FiltroPagosDto filtro, CancellationToken ct)
    {
        using var todas = _db.AccesoATodasLasTiendas(); // comprobantes de cualquier tienda (RLS)
        if (!Enum.IsDefined(filtro.Estado)) throw new ValidacionException("El estado pedido no existe.");
        var limite = Math.Clamp(filtro.Limite ?? LimitePorDefecto, 1, LimiteMaximo);
        var estado = filtro.Estado;

        // Los pendientes, del más viejo al más nuevo (el primero que llegó se atiende primero); el resto, del más nuevo.
        var pendientes = estado == EstadoPago.Pendiente;
        var consulta = _db.PagosPlan.AsNoTracking().Include(p => p.Plan).Where(p => p.Estado == estado);
        if (filtro.Cursor is { } cursor) consulta = pendientes ? consulta.Where(p => p.Id.CompareTo(cursor) > 0) : consulta.Where(p => p.Id.CompareTo(cursor) < 0);
        var pagina = await (pendientes ? consulta.OrderBy(p => p.Id) : consulta.OrderByDescending(p => p.Id)).Take(limite + 1).ToListAsync(ct);
        var hayMas = pagina.Count > limite;
        if (hayMas) pagina.RemoveAt(pagina.Count - 1);

        return new PaginaDePagosDto(await ADtosAsync(pagina, ct), hayMas ? pagina[^1].Id : null);
    }

    public async Task<PagoParaRevisarDto> AprobarAsync(Guid pagoId, Guid adminId, CancellationToken ct)
    {
        using var todas = _db.AccesoATodasLasTiendas();
        try
        {
            await using var transaccion = await _db.Database.BeginTransactionAsync(ct);
            var ahora = _planes.Ahora;

            // Una sola fila gana: con dos aprobaciones a la vez, la segunda no extiende el plan de nuevo.
            var marcados = await _db.PagosPlan.Where(p => p.Id == pagoId && p.Estado == EstadoPago.Pendiente)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Estado, EstadoPago.Aprobado).SetProperty(p => p.ResueltoEn, ahora).SetProperty(p => p.ResueltoPorId, adminId), ct);
            if (marcados == 0) throw await ExplicarAsync(pagoId, ct);

            var pago = await _db.PagosPlan.AsNoTracking().Include(p => p.Plan).FirstAsync(p => p.Id == pagoId, ct);
            await ActivarAsync(pago.TiendaId, pago.Plan!, pago.Meses, "Pago", pago.Id, adminId, ct);
            await _db.SaveChangesAsync(ct);
            await transaccion.CommitAsync(ct);
        }
        catch
        {
            _db.ChangeTracker.Clear();
            throw;
        }
        return await UnoAsync(pagoId, ct);
    }

    public async Task<PagoParaRevisarDto> RechazarAsync(Guid pagoId, Guid adminId, RechazarPagoDto dto, CancellationToken ct)
    {
        using var todas = _db.AccesoATodasLasTiendas();
        var motivo = Validacion.Texto(dto?.Motivo, 300, "El motivo");
        var ahora = _planes.Ahora;
        var marcados = await _db.PagosPlan.Where(p => p.Id == pagoId && p.Estado == EstadoPago.Pendiente)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Estado, EstadoPago.Rechazado).SetProperty(p => p.Motivo, motivo)
                .SetProperty(p => p.ResueltoEn, ahora).SetProperty(p => p.ResueltoPorId, adminId), ct);
        if (marcados == 0) throw await ExplicarAsync(pagoId, ct);
        return await UnoAsync(pagoId, ct);
    }

    private async Task<DominioException> ExplicarAsync(Guid pagoId, CancellationToken ct)
        => await _db.PagosPlan.AnyAsync(p => p.Id == pagoId, ct) ? new PagoYaResueltoException() : new PagoNoEncontradoException(pagoId);

    private async Task<PagoParaRevisarDto> UnoAsync(Guid pagoId, CancellationToken ct)
    {
        var pago = await _db.PagosPlan.AsNoTracking().Include(p => p.Plan).FirstOrDefaultAsync(p => p.Id == pagoId, ct) ?? throw new PagoNoEncontradoException(pagoId);
        return (await ADtosAsync([pago], ct))[0];
    }

    private async Task<List<PagoParaRevisarDto>> ADtosAsync(List<PagoPlan> pagos, CancellationToken ct)
    {
        var tiendaIds = pagos.Select(p => p.TiendaId).Distinct().ToList();
        var tiendas = await _db.Tiendas.AsNoTracking().Include(t => t.Dueno).Where(t => tiendaIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var imagenIds = pagos.Where(p => p.ComprobanteImagenId != null).Select(p => p.ComprobanteImagenId!.Value).ToList();
        // El comprobante es de OTRA tienda: el filtro global no lo dejaría ver.
        var imagenes = await _db.Imagenes.IgnoreQueryFilters().AsNoTracking().Where(i => imagenIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);

        return pagos.Select(p =>
        {
            var t = tiendas[p.TiendaId];
            var comprobante = p.ComprobanteImagenId is { } id ? imagenes.GetValueOrDefault(id) : null;
            return new PagoParaRevisarDto(PagoMapeo.ADto(_almacen, p, p.Plan!.Nombre, comprobante), t.Id, t.Nombre, t.Slug, t.Dueno!.Email);
        }).ToList();
    }

    // ───────────── Tiendas ─────────────

    public async Task<PaginaDeTiendasDto> TiendasAsync(FiltroTiendasDto filtro, CancellationToken ct)
    {
        using var todas = _db.AccesoATodasLasTiendas(); // uso (productos, fotos) de cada tienda (RLS)
        var limite = Math.Clamp(filtro.Limite ?? LimitePorDefecto, 1, LimiteMaximo);
        var consulta = _db.Tiendas.AsNoTracking().Include(t => t.Dueno).AsQueryable();
        if (!string.IsNullOrWhiteSpace(filtro.Buscar))
        {
            var texto = filtro.Buscar.Trim().ToLower();
            consulta = consulta.Where(t => t.Nombre.ToLower().Contains(texto) || t.Slug.Contains(texto) || t.Dueno!.Email.Contains(texto));
        }
        if (filtro.Cursor is { } cursor) consulta = consulta.Where(t => t.Id.CompareTo(cursor) < 0);

        var pagina = await consulta.OrderByDescending(t => t.Id).Take(limite + 1).ToListAsync(ct);
        var hayMas = pagina.Count > limite;
        if (hayMas) pagina.RemoveAt(pagina.Count - 1);

        return new PaginaDeTiendasDto(await TiendasDtoAsync(pagina, ct), hayMas ? pagina[^1].Id : null);
    }

    private async Task<List<AdminTiendaDto>> TiendasDtoAsync(List<Tienda> tiendas, CancellationToken ct)
    {
        var uso = await _planes.UsoAsync(tiendas.Select(t => t.Id).ToList(), ct);
        var resultado = new List<AdminTiendaDto>();
        foreach (var t in tiendas)
        {
            var (plan, suscripcion) = await _planes.VigenteAsync(t.Id, ct);
            resultado.Add(new AdminTiendaDto(t.Id, t.Nombre, t.Slug, t.Dueno!.Email, t.Activa, plan.Nombre,
                suscripcion?.Hasta is { } h ? PagoMapeo.Utc(h) : null, uso[t.Id].Productos, PlanesDeTienda.AMb(uso[t.Id].Bytes)));
        }
        return resultado;
    }

    public async Task<AdminTiendaDto> AsignarPlanAsync(Guid tiendaId, Guid adminId, AsignarPlanDto dto, CancellationToken ct)
    {
        using var todas = _db.AccesoATodasLasTiendas();
        if (dto.Meses is < 1 or > MaximoMeses) throw new ValidacionException($"Los meses tienen que estar entre 1 y {MaximoMeses} (o vacío, sin vencimiento).");
        var tienda = await _db.Tiendas.AsNoTracking().Include(t => t.Dueno).FirstOrDefaultAsync(t => t.Id == tiendaId, ct) ?? throw new TiendaNoEncontradaException(tiendaId);
        var plan = await _db.Planes.AsNoTracking().FirstOrDefaultAsync(p => p.Id == dto.PlanId, ct) ?? throw new PlanNoEncontradoException(dto.PlanId);

        try
        {
            await using var transaccion = await _db.Database.BeginTransactionAsync(ct);
            await ActivarAsync(tienda.Id, plan, dto.Meses, "Manual", null, adminId, ct);
            await _db.SaveChangesAsync(ct);
            await transaccion.CommitAsync(ct);
        }
        catch
        {
            _db.ChangeTracker.Clear();
            throw;
        }
        return (await TiendasDtoAsync([tienda], ct))[0];
    }

    // Activa `plan` en la tienda. Mismo plan vigente con vencimiento + meses → se EXTIENDE desde donde termina (el que paga por
    // adelantado no pierde días). Cualquier otro caso → el plan vigente se cierra ahora y el nuevo empieza ahora (no hay
    // prorrateo: es lo más simple y se entiende). Mismo plan sin vencimiento → ya lo tiene, no se acorta. NO guarda: lo hace quien llama.
    private async Task ActivarAsync(Guid tiendaId, Plan plan, int? meses, string origen, Guid? pagoId, Guid adminId, CancellationToken ct)
    {
        var ahora = _planes.Ahora;
        var (_, actual) = await _planes.VigenteAsync(tiendaId, ct);

        DateTime desde;
        if (actual is not null && actual.PlanId == plan.Id && meses is not null)
        {
            if (actual.Hasta is not { } fin) return;
            // Se alarga la misma suscripción (no una nueva que empiece en el futuro): así "vence el…" siempre es la fecha final.
            var nuevoFin = fin.AddMonths(meses.Value);
            await _db.Suscripciones.Where(s => s.Id == actual.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Hasta, nuevoFin), ct);
            return;
        }
        else
        {
            await _db.Suscripciones.Where(s => s.TiendaId == tiendaId && s.Desde <= ahora && (s.Hasta == null || s.Hasta > ahora))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Hasta, ahora), ct);
            desde = ahora;
        }

        _db.Suscripciones.Add(new Suscripcion
        {
            TiendaId = tiendaId, PlanId = plan.Id, Desde = desde, Hasta = meses is { } m ? desde.AddMonths(m) : null,
            Origen = origen, PagoId = pagoId, AsignadaPorId = adminId, CreadaEn = ahora,
        });
    }

    // ───────────── Planes ─────────────

    public async Task<IReadOnlyList<PlanDto>> PlanesAsync(CancellationToken ct)
        => (await _db.Planes.AsNoTracking().OrderBy(p => p.Orden).ToListAsync(ct)).Select(PagoMapeo.ADto).ToList();

    public async Task<PlanDto> ActualizarPlanAsync(Guid planId, ActualizarPlanDto dto, CancellationToken ct)
    {
        var plan = await _db.Planes.FirstOrDefaultAsync(p => p.Id == planId, ct) ?? throw new PlanNoEncontradoException(planId);
        var precio = Validacion.Monto(dto.PrecioMensual, "El precio", permitirCero: true);
        if (dto.MaxProductos is < 1 or > 1_000_000) throw new ValidacionException("El máximo de productos tiene que estar entre 1 y 1.000.000 (o vacío, sin límite).");
        if (dto.MaxAlmacenamientoMb is < 1 or > 1_000_000) throw new ValidacionException("El espacio de fotos tiene que estar entre 1 y 1.000.000 MB (o vacío, sin límite).");
        if (plan.Codigo == Plan.CodigoGratis && (precio != 0 || !dto.Activo)) throw new ValidacionException("El plan Gratis es gratis y siempre está activo.");
        if (plan.Codigo != Plan.CodigoGratis && precio <= 0) throw new ValidacionException("Un plan pago tiene que tener un precio mayor a 0.");

        plan.PrecioMensual = precio;
        plan.MaxProductos = dto.MaxProductos;
        plan.MaxAlmacenamientoMb = dto.MaxAlmacenamientoMb;
        plan.Activo = dto.Activo;
        await _db.SaveChangesAsync(ct);
        return PagoMapeo.ADto(plan);
    }
}
