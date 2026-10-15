using Hermes.Application.Interfaces;
using Hermes.Domain.Entities;
using Hermes.Domain.Enums;
using Hermes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hermes.Infrastructure.Mantenimiento;

public class LimpiezaOptions
{
    public const string SectionName = "Limpieza";

    public bool Habilitada { get; set; } = true;
    public int IntervaloMinutos { get; set; } = 60;
    // Una foto subida que nadie usa (ni producto, ni logo, ni comprobante) se borra pasado este tiempo. Da margen de sobra a
    // quien está armando un producto y todavía no guardó.
    public int HorasParaFotoSinUsar { get; set; } = 24;
    // Cuántas fotos se borran por vuelta como mucho (para no trabar la base con una sola transacción enorme).
    public int MaximoFotosPorVuelta { get; set; } = 200;
}

public record ResultadoLimpieza(int TokensBorrados, int FotosBorradas);

// Lo que se limpia solo, de TODAS las tiendas (por eso ignora el filtro global; no hay tienda en sesión):
//  - Refresh tokens ya vencidos (un token vencido se rechaza igual; guardarlo no sirve para nada).
//  - Fotos subidas que nadie usa: una subida que la dueña no llegó a guardar en un producto queda huérfana (pendiente de M2).
// Las fechas se comparan por el Id (Guid v7: lleva la fecha de creación adentro), no por las columnas DateTimeOffset: así la
// misma consulta corre en Postgres y en SQLite (las pruebas), que no compara DateTimeOffset en SQL.
public class Limpieza
{
    private readonly HermesDbContext _db;
    private readonly IAlmacenArchivos _almacen;
    private readonly TimeProvider _reloj;
    private readonly LimpiezaOptions _opciones;
    private readonly int _diasRefresh;

    public Limpieza(HermesDbContext db, IAlmacenArchivos almacen, TimeProvider reloj, IOptions<LimpiezaOptions> opciones, IOptions<Security.JwtOptions> jwt)
    {
        _db = db;
        _almacen = almacen;
        _reloj = reloj;
        _opciones = opciones.Value;
        _diasRefresh = jwt.Value.RefreshDias;
    }

    public async Task<ResultadoLimpieza> EjecutarAsync(CancellationToken ct)
    {
        using var todas = _db.AccesoATodasLasTiendas(); // recorre todas las tiendas (RLS)
        var ahora = _reloj.GetUtcNow();

        // Un refresh token dura RefreshDias desde que se creó: los creados antes de eso (+1 día de margen) ya vencieron.
        var corteTokens = Guid.CreateVersion7(ahora.AddDays(-(_diasRefresh + 1)));
        var tokens = await _db.RefreshTokens.Where(t => t.Id.CompareTo(corteTokens) < 0).ExecuteDeleteAsync(ct);

        var corteFotos = Guid.CreateVersion7(ahora.AddHours(-_opciones.HorasParaFotoSinUsar));
        var huerfanas = await _db.Imagenes.IgnoreQueryFilters()
            .Where(i => i.Id.CompareTo(corteFotos) < 0
                && !_db.ProductoFotos.IgnoreQueryFilters().Any(f => f.ImagenId == i.Id)
                // Las de un borrador de Instagram que todavía espera en la bandeja (M12); si se descartó, ya no sirven.
                && !_db.BorradorFotos.IgnoreQueryFilters().Any(f => f.ImagenId == i.Id && f.Borrador!.Estado == EstadoBorrador.Pendiente)
                && !_db.Tiendas.Any(t => t.LogoImagenId == i.Id)
                && !_db.PagosPlan.Any(p => p.ComprobanteImagenId == i.Id))
            .OrderBy(i => i.Id).Take(_opciones.MaximoFotosPorVuelta)
            .ToListAsync(ct);
        foreach (var imagen in huerfanas)
            await _almacen.BorrarAsync(Enum.GetValues<TamanoImagen>().Select(t => Imagen.ClaveDe(imagen.TiendaId, imagen.Id, t)), ct);
        _db.Imagenes.RemoveRange(huerfanas);
        await _db.SaveChangesAsync(ct);

        return new ResultadoLimpieza(tokens, huerfanas.Count);
    }
}

// Corre la limpieza cada IntervaloMinutos (la primera, un minuto después de arrancar). Un error se registra y se reintenta en la
// próxima vuelta: nunca tira abajo la API. Con varias instancias de la API correrían todas (inofensivo: borrar lo mismo dos veces
// no hace nada), pero conviene habilitarla en una sola (`Limpieza:Habilitada`).
public class LimpiezaPeriodica : BackgroundService
{
    private readonly IServiceScopeFactory _ambitos;
    private readonly LimpiezaOptions _opciones;
    private readonly ILogger<LimpiezaPeriodica> _log;

    public LimpiezaPeriodica(IServiceScopeFactory ambitos, IOptions<LimpiezaOptions> opciones, ILogger<LimpiezaPeriodica> log)
    {
        _ambitos = ambitos;
        _opciones = opciones.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_opciones.Habilitada) return;
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
            using var reloj = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, _opciones.IntervaloMinutos)));
            do
            {
                try
                {
                    await using var ambito = _ambitos.CreateAsyncScope();
                    var r = await ambito.ServiceProvider.GetRequiredService<Limpieza>().EjecutarAsync(ct);
                    if (r.TokensBorrados + r.FotosBorradas > 0)
                        _log.LogInformation("Limpieza: {Tokens} refresh tokens vencidos y {Fotos} fotos sin usar borradas", r.TokensBorrados, r.FotosBorradas);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogError(ex, "Falló la limpieza periódica; se reintenta en la próxima vuelta");
                }
            } while (await reloj.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException)
        {
            // La aplicación se está apagando.
        }
    }
}
