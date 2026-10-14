using Hermes.Application;
using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Domain.Entities;
using Hermes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hermes.Infrastructure.Services;

// Gasto es de la tienda (filtro global): nada acá filtra por tienda a mano. Los totales se suman EN MEMORIA
// (SQLite no suma decimales).
public class GastoService : IGastoService
{
    private const int LimitePorDefecto = 30;
    private const int LimiteMaximo = 100;
    private static readonly DateTime FechaMinima = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly HermesDbContext _db;
    private readonly ITiendaActual _sesion;
    private readonly TimeProvider _reloj;

    public GastoService(HermesDbContext db, ITiendaActual sesion, TimeProvider reloj)
    {
        _db = db;
        _sesion = sesion;
        _reloj = reloj;
    }

    private void ExigirTienda() => _ = _sesion.TiendaId ?? throw new SinTiendaEnSesionException();

    public async Task<ResultadoGasto> RegistrarAsync(RegistrarGastoDto dto, CancellationToken ct)
    {
        ExigirTienda();
        if (dto.ClaveIdempotencia == Guid.Empty) throw new ValidacionException("Falta la clave del gasto.");
        var concepto = Validacion.Texto(dto.Concepto, 120, "El concepto");
        var monto = Validacion.Monto(dto.Monto, "El monto", permitirCero: false);

        var ahora = _reloj.GetUtcNow().UtcDateTime;
        var fecha = dto.Fecha?.UtcDateTime ?? ahora;
        // Un día de margen por la zona horaria de quien lo carga; el pasado puede ser cualquiera (desde el 2000).
        if (fecha > ahora.AddDays(1)) throw new ValidacionException("La fecha del gasto no puede ser futura.");
        if (fecha < FechaMinima) throw new ValidacionException("La fecha del gasto no es válida.");

        if (await BuscarPorClaveAsync(dto.ClaveIdempotencia, ct) is { } yaRegistrado) return new ResultadoGasto(yaRegistrado, false);

        var gasto = new Gasto { Concepto = concepto, Monto = monto, Fecha = fecha, ClaveIdempotencia = dto.ClaveIdempotencia };
        try
        {
            _db.Gastos.Add(gasto);
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // ¿Fue la carrera por la misma clave? Entonces el otro pedido ya lo registró: se devuelve ese.
            _db.ChangeTracker.Clear();
            if (await BuscarPorClaveAsync(dto.ClaveIdempotencia, ct) is { } ganador) return new ResultadoGasto(ganador, false);
            throw;
        }
        return new ResultadoGasto(ADto(gasto), true);
    }

    private async Task<GastoDto?> BuscarPorClaveAsync(Guid clave, CancellationToken ct)
    {
        var g = await _db.Gastos.AsNoTracking().FirstOrDefaultAsync(x => x.ClaveIdempotencia == clave, ct);
        return g is null ? null : ADto(g);
    }

    public async Task<PaginaDeGastosDto> ListarAsync(FiltroGastosDto filtro, CancellationToken ct)
    {
        ExigirTienda();
        var limite = Math.Clamp(filtro.Limite ?? LimitePorDefecto, 1, LimiteMaximo);

        var enPeriodo = _db.Gastos.AsNoTracking().AsQueryable();
        if (filtro.Desde is { } desde) { var d = desde.UtcDateTime; enPeriodo = enPeriodo.Where(g => g.Fecha >= d); }
        if (filtro.Hasta is { } hasta) { var h = hasta.UtcDateTime; enPeriodo = enPeriodo.Where(g => g.Fecha < h); }

        // El total es de TODO el período, no de la página: se suma en memoria solo los montos vigentes.
        var montos = await enPeriodo.Where(g => !g.Anulado).Select(g => g.Monto).ToListAsync(ct);

        var consulta = filtro.IncluirAnulados ? enPeriodo : enPeriodo.Where(g => !g.Anulado);
        if (filtro.Cursor is { } cursor) consulta = consulta.Where(g => g.Id.CompareTo(cursor) < 0);
        var pagina = await consulta.OrderByDescending(g => g.Id).Take(limite + 1).ToListAsync(ct);
        var hayMas = pagina.Count > limite;
        if (hayMas) pagina.RemoveAt(pagina.Count - 1);

        return new PaginaDeGastosDto(pagina.Select(ADto).ToList(), hayMas ? pagina[^1].Id : null, montos.Sum());
    }

    public async Task<decimal> TotalAsync(DateTimeOffset? desde, DateTimeOffset? hasta, CancellationToken ct)
    {
        ExigirTienda();
        var consulta = _db.Gastos.AsNoTracking().Where(g => !g.Anulado);
        if (desde is { } d0) { var d = d0.UtcDateTime; consulta = consulta.Where(g => g.Fecha >= d); }
        if (hasta is { } h0) { var h = h0.UtcDateTime; consulta = consulta.Where(g => g.Fecha < h); }
        return (await consulta.Select(g => g.Monto).ToListAsync(ct)).Sum();
    }

    public async Task<GastoDto> AnularAsync(Guid id, CancellationToken ct)
    {
        ExigirTienda();
        var ahora = _reloj.GetUtcNow().UtcDateTime;
        // Un solo UPDATE condicional: con dos pedidos simultáneos solo uno anula.
        var marcados = await _db.Gastos.Where(g => g.Id == id && !g.Anulado)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.Anulado, true).SetProperty(g => g.AnuladoEn, ahora), ct);
        var gasto = await _db.Gastos.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct) ?? throw new GastoNoEncontradoException(id);
        if (marcados == 0) throw new GastoYaAnuladoException();
        return ADto(gasto);
    }

    private static DateTimeOffset Utc(DateTime d) => new(DateTime.SpecifyKind(d, DateTimeKind.Utc));

    private static GastoDto ADto(Gasto g) => new(g.Id, Utc(g.Fecha), g.Concepto, g.Monto, g.Anulado, g.AnuladoEn is { } a ? Utc(a) : null);
}
