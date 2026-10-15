using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Instagram;
using Hermes.Application.Interfaces;
using Hermes.Domain.Entities;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hermes.Infrastructure.Instagram;

// M12: la cuenta de Instagram de la tienda y la sincronización de sus publicaciones a la BANDEJA de revisión (M11). Nada
// llega al catálogo solo: cada publicación nueva se lee con el mismo lector (ParserPublicacion) y queda como borrador, ahora
// con sus fotos. InstagramConexion, InstagramPublicacion, Borrador y BorradorFoto son de la tienda (filtro global + RLS).
public class InstagramConexionService : IInstagramConexionService
{
    public const int MaximoFotos = 8; // las mismas que admite un producto
    private static readonly TimeSpan RenovarAntesDe = TimeSpan.FromDays(15);
    private static readonly TimeSpan EdadMinimaParaRenovar = TimeSpan.FromHours(24); // regla de Meta

    private readonly HermesDbContext _db;
    private readonly IClienteInstagram _cliente;
    private readonly SecretosInstagram _secretos;
    private readonly IImagenService _imagenes;
    private readonly ITiendaActual _sesion;
    private readonly TimeProvider _reloj;
    private readonly InstagramOptions _o;

    public InstagramConexionService(HermesDbContext db, IClienteInstagram cliente, SecretosInstagram secretos, IImagenService imagenes,
        ITiendaActual sesion, TimeProvider reloj, IOptions<InstagramOptions> opciones)
    {
        _db = db;
        _cliente = cliente;
        _secretos = secretos;
        _imagenes = imagenes;
        _sesion = sesion;
        _reloj = reloj;
        _o = opciones.Value;
    }

    private Guid TiendaIdDeLaSesion() => _sesion.TiendaId ?? throw new SinTiendaEnSesionException();
    private DateTime Ahora => _reloj.GetUtcNow().UtcDateTime;
    private static DateTimeOffset Utc(DateTime d) => new(DateTime.SpecifyKind(d, DateTimeKind.Utc));

    private void ExigirConfigurada()
    {
        if (!_o.Configurada)
            throw new InstagramNoDisponibleException("La conexión con Instagram todavía no está activada en la plataforma. Mientras tanto, pegá el texto de tu publicación.");
    }

    // ───────────── Estado, conectar, desconectar ─────────────

    public async Task<InstagramConexionDto> EstadoAsync(CancellationToken ct)
    {
        TiendaIdDeLaSesion();
        var c = await _db.InstagramConexiones.AsNoTracking().FirstOrDefaultAsync(x => x.Activa, ct);
        return c is null
            ? new InstagramConexionDto(_o.Configurada, false, null, null, null, null, false)
            : new InstagramConexionDto(_o.Configurada, true, c.Usuario, Utc(c.ConectadaEn), c.UltimaSincronizacion is { } u ? Utc(u) : null, c.UltimoError, c.NecesitaReconectar);
    }

    public UrlConexionDto Url()
    {
        var tienda = TiendaIdDeLaSesion();
        ExigirConfigurada();
        var estado = _secretos.FirmarEstado(tienda, _reloj.GetUtcNow().AddMinutes(15));
        return new UrlConexionDto(_o.UrlAutorizacion
            + $"?client_id={Uri.EscapeDataString(_o.AppId)}&redirect_uri={Uri.EscapeDataString(_o.RedirectUri)}"
            + $"&response_type=code&scope=instagram_business_basic&state={Uri.EscapeDataString(estado)}");
    }

    public async Task<InstagramConexionDto> ConectarAsync(ConectarInstagramDto dto, CancellationToken ct)
    {
        var tienda = TiendaIdDeLaSesion();
        ExigirConfigurada();
        // El `state` firmado prueba que el pedido salió de ESTA tienda hace menos de 15 minutos (si no, alguien podría atar
        // su cuenta de Instagram a la tienda de otro mandándole un enlace).
        if (!_secretos.EstadoValido(dto?.State, tienda, _reloj.GetUtcNow()))
            throw new ValidacionException("El enlace para conectar venció o no es de esta tienda. Probá conectar de nuevo.");
        // Instagram a veces agrega "#_" al final del código.
        var codigo = (dto!.Code ?? string.Empty).Trim().Split('#')[0];
        if (codigo.Length is 0 or > 2000) throw new ValidacionException("Instagram no mandó el código de autorización. Probá de nuevo.");

        var corto = await _cliente.CanjearCodigoAsync(codigo, ct);
        var largo = await _cliente.TokenLargoAsync(corto.Token, ct);
        var perfil = await _cliente.PerfilAsync(largo.Token, ct);

        var ahora = Ahora;
        var c = await _db.InstagramConexiones.FirstOrDefaultAsync(ct);
        if (c is null)
        {
            c = new InstagramConexion();
            _db.InstagramConexiones.Add(c);
        }
        c.IgUsuarioId = perfil.Id;
        c.Usuario = perfil.Usuario.Length > 64 ? perfil.Usuario[..64] : perfil.Usuario;
        c.TokenCifrado = _secretos.Cifrar(largo.Token);
        c.TokenVence = ahora.AddSeconds(largo.ExpiraEnSegundos ?? (int)TimeSpan.FromDays(60).TotalSeconds);
        c.TokenRenovado = ahora;
        c.ConectadaEn = ahora;
        c.UltimoError = null;
        c.NecesitaReconectar = false;
        c.Activa = true;
        await _db.SaveChangesAsync(ct);
        return await EstadoAsync(ct);
    }

    // Se borra el token (no queda nada con qué leer la cuenta). Las publicaciones ya importadas quedan anotadas: si se vuelve a
    // conectar la misma cuenta, no se repiten. (La dueña puede además quitar el permiso desde Instagram.)
    public async Task DesconectarAsync(CancellationToken ct)
    {
        TiendaIdDeLaSesion();
        var c = await _db.InstagramConexiones.FirstOrDefaultAsync(ct);
        if (c is null) return;
        c.Activa = false;
        c.TokenCifrado = string.Empty;
        c.NecesitaReconectar = false;
        c.UltimoError = null;
        await _db.SaveChangesAsync(ct);
    }

    // ───────────── Sincronizar ─────────────

    public async Task<SincronizacionDto> SincronizarAsync(CancellationToken ct)
    {
        TiendaIdDeLaSesion();
        ExigirConfigurada();
        var c = await _db.InstagramConexiones.FirstOrDefaultAsync(x => x.Activa, ct)
            ?? throw new InstagramNoDisponibleException("Conectá tu cuenta de Instagram primero.");
        if (c.NecesitaReconectar) throw new InstagramNoDisponibleException("La conexión con Instagram se venció: volvé a conectar tu cuenta.");
        var ahora = Ahora;
        // Meta limita cuántos pedidos se le hacen por hora: "Sincronizar ahora" seguido no hace nada.
        if (c.UltimaSincronizacion is { } u && ahora - u < TimeSpan.FromMinutes(1))
            return new SincronizacionDto(0, 0, 0, ["Ya revisamos tu Instagram hace menos de un minuto."]);

        string token;
        try
        {
            token = _secretos.Descifrar(c.TokenCifrado);
            if (ahora >= c.TokenVence) throw new InstagramRechazoException("La conexión se venció.", tokenInvalido: true);
            // El token dura 60 días y Meta deja renovarlo cuando tiene más de 24 horas: se renueva cuando faltan menos de 15.
            if (ahora - c.TokenRenovado >= EdadMinimaParaRenovar && c.TokenVence - ahora <= RenovarAntesDe)
            {
                var nuevo = await _cliente.RenovarAsync(token, ct);
                token = nuevo.Token;
                c.TokenCifrado = _secretos.Cifrar(token);
                c.TokenVence = ahora.AddSeconds(nuevo.ExpiraEnSegundos ?? (int)TimeSpan.FromDays(60).TotalSeconds);
                c.TokenRenovado = ahora;
                await _db.SaveChangesAsync(ct);
            }
            var publicaciones = await _cliente.PublicacionesAsync(token, Math.Clamp(_o.MaximoPorSincronizacion, 1, 50), ct);
            var resultado = await ImportarAsync(publicaciones, ct);
            c.UltimaSincronizacion = ahora;
            c.UltimoError = resultado.Avisos.FirstOrDefault(a => a.StartsWith("La bandeja", StringComparison.Ordinal));
            await _db.SaveChangesAsync(ct);
            return resultado;
        }
        catch (InstagramRechazoException ex)
        {
            _db.ChangeTracker.Clear();
            var mensaje = ex.Message.Length > 300 ? ex.Message[..300] : ex.Message;
            await _db.InstagramConexiones.Where(x => x.Id == c.Id).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.UltimoError, mensaje)
                .SetProperty(x => x.NecesitaReconectar, x => x.NecesitaReconectar || ex.TokenInvalido), CancellationToken.None);
            throw;
        }
    }

    private async Task<SincronizacionDto> ImportarAsync(IReadOnlyList<MediaIg> publicaciones, CancellationToken ct)
    {
        var ids = publicaciones.Select(p => p.Id).ToList();
        var vistas = (await _db.InstagramPublicaciones.AsNoTracking().Where(p => ids.Contains(p.MediaId)).Select(p => p.MediaId).ToListAsync(ct)).ToHashSet();
        // La primera vez no se trae todo el historial a la bandeja: solo lo de los últimos días.
        var primera = !await _db.InstagramPublicaciones.AnyAsync(ct);
        var desde = primera ? _reloj.GetUtcNow().AddDays(-_o.DiasPrimeraSincronizacion) : DateTimeOffset.MinValue;
        var nuevas = publicaciones.Where(p => !vistas.Contains(p.Id) && p.Fecha >= desde).OrderBy(p => p.Fecha).ToList();
        // Las viejas que la primera vez se saltean quedan anotadas como vistas: si no, la segunda vez entrarían todas.
        var viejas = publicaciones.Where(p => !vistas.Contains(p.Id) && p.Fecha < desde).ToList();
        if (viejas.Count > 0)
        {
            var ahora = Ahora;
            _db.InstagramPublicaciones.AddRange(viejas.Select(p => new InstagramPublicacion
            {
                MediaId = p.Id, Enlace = Recortar(p.Enlace, 300), PublicadaEn = p.Fecha.UtcDateTime, ImportadaEn = ahora, Productos = 0,
            }));
            await GuardarAsync(0, ct);
        }

        int procesadas = 0, productos = 0, sinProductos = 0;
        var avisos = new List<string>();
        foreach (var p in nuevas)
        {
            var r = await ImportarUnaAsync(p, avisos, ct);
            if (r is null) break; // bandeja llena: lo que falta se trae la próxima vez
            procesadas++;
            productos += r.Value;
            if (r.Value == 0) sinProductos++;
        }
        return new SincronizacionDto(procesadas, productos, sinProductos, avisos);
    }

    // null = no entró (bandeja llena). Si no, cuántos borradores salieron (0 = el texto no tenía productos).
    private async Task<int?> ImportarUnaAsync(MediaIg p, List<string> avisos, CancellationToken ct)
    {
        var ahora = _reloj.GetUtcNow();
        var texto = (p.Texto ?? string.Empty).Trim();
        if (texto.Length > ParserPublicacion.MaximoTexto) texto = texto[..ParserPublicacion.MaximoTexto];
        // A diferencia de pegar el texto a mano (M11), acá entra TODO lo que se publica ("¡Gracias por los 1000 seguidores!",
        // sorteos, avisos): solo se toma como producto lo que tiene un PRECIO. Lo demás queda anotado como visto, sin borrador.
        var leidos = texto.Length == 0 ? [] : ParserPublicacion.Leer(texto).Productos.Where(x => !string.IsNullOrWhiteSpace(x.Nombre) && x.Precio is > 0).ToList();

        var registro = new InstagramPublicacion
        {
            MediaId = p.Id, Enlace = Recortar(p.Enlace, 300), PublicadaEn = p.Fecha.UtcDateTime, ImportadaEn = ahora.UtcDateTime, Productos = leidos.Count,
        };
        if (leidos.Count == 0)
        {
            _db.InstagramPublicaciones.Add(registro);
            return await GuardarAsync(0, ct);
        }

        var pendientes = await _db.Borradores.CountAsync(b => b.Estado == EstadoBorrador.Pendiente, ct);
        if (pendientes + leidos.Count > Borrador.MaximoPendientes)
        {
            avisos.Add($"La bandeja está llena ({pendientes} esperando): revisá o descartá algunos y lo que falta llega en la próxima sincronización.");
            return null;
        }

        // Las fotos: con un solo producto, todas (hasta 8); con varios (carrusel con lista), una por producto si son tantas
        // como productos; si no, ninguna (no hay cómo saber cuál es de cuál: se agregan al revisar).
        var urls = Fotos(p);
        var fotosPorProducto = leidos.Count == 1 ? [urls.Take(MaximoFotos).ToList()]
            : urls.Count == leidos.Count ? urls.Select(u => new List<string> { u }).ToList()
            : leidos.Select(_ => new List<string>()).ToList();
        var sinRepartir = leidos.Count > 1 && urls.Count > 0 && urls.Count != leidos.Count;

        var borradores = InstagramService.ArmarBorradores(texto, leidos, ahora);
        var sinEspacio = false;
        for (var i = 0; i < borradores.Count; i++)
        {
            var b = borradores[i];
            b.Origen = "Instagram";
            b.InstagramMediaId = p.Id;
            b.Enlace = registro.Enlace;
            var orden = 0;
            foreach (var url in fotosPorProducto[i])
            {
                if (sinEspacio) break;
                try
                {
                    var imagen = await _imagenes.SubirAsync(await _cliente.DescargarAsync(url, ct), ct);
                    b.Fotos.Add(new BorradorFoto { ImagenId = imagen.Id, Orden = orden++ });
                }
                catch (LimiteDePlanException)
                {
                    sinEspacio = true;
                }
                catch (Exception ex) when (ex is ValidacionException or InstagramRechazoException or HttpRequestException)
                {
                    // Una foto que no se pudo bajar o no es una imagen válida: el borrador sigue sin ella.
                }
            }
            if (sinRepartir) b.Avisos = Sumar(b.Avisos, "La publicación tiene varias fotos y varios productos: agregá las fotos de cada uno al revisar.");
            if (sinEspacio) b.Avisos = Sumar(b.Avisos, "No quedaba espacio de fotos en tu plan: algunas fotos no se trajeron.");
            if (p.Tipo == "VIDEO") b.Avisos = Sumar(b.Avisos, "Era un video: trajimos solo la imagen de portada.");
        }
        _db.Borradores.AddRange(borradores);
        _db.InstagramPublicaciones.Add(registro);
        return await GuardarAsync(leidos.Count, ct);
    }

    // Dos sincronizaciones a la vez (la automática y "Sincronizar ahora"): el índice único de la publicación deja pasar una.
    private async Task<int?> GuardarAsync(int productos, CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            return productos;
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return 0;
        }
    }

    // De qué fotos se trae cada tipo de publicación: imagen → ella; video → su portada; carrusel → las imágenes (y la
    // portada de los videos) de adentro.
    private static List<string> Fotos(MediaIg p)
    {
        var urls = p.Tipo switch
        {
            "CAROUSEL_ALBUM" => p.Hijos.Select(h => h.Tipo == "VIDEO" ? h.Miniatura : h.UrlMedia),
            "VIDEO" => [p.Miniatura],
            _ => [p.UrlMedia],
        };
        return urls.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u!).ToList();
    }

    private static string Sumar(string avisos, string aviso) => avisos.Length == 0 ? aviso : $"{avisos}\n{aviso}";
    private static string? Recortar(string? t, int max) => t is null ? null : t.Length > max ? t[..max] : t;
}

// Cada IntervaloMinutos, para cada tienda con la cuenta conectada (y que no necesita reconectar), trae lo nuevo. Cada tienda
// en su propio ámbito, con la tienda FIJADA (TiendaPublicaContexto: el ITiendaActual de la API la usa cuando no hay sesión),
// así el filtro global y la Row Level Security valen igual que con la dueña. Un error de una tienda no frena a las demás.
public class SincronizacionInstagramPeriodica : BackgroundService
{
    private readonly IServiceScopeFactory _ambitos;
    private readonly InstagramOptions _o;
    private readonly ILogger<SincronizacionInstagramPeriodica> _log;

    public SincronizacionInstagramPeriodica(IServiceScopeFactory ambitos, IOptions<InstagramOptions> opciones, ILogger<SincronizacionInstagramPeriodica> log)
    {
        _ambitos = ambitos;
        _o = opciones.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_o.Configurada || !_o.SincronizacionAutomatica) return;
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), ct);
            using var reloj = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(5, _o.IntervaloMinutos)));
            do
            {
                await SincronizarTodasAsync(_ambitos, _log, ct);
            } while (await reloj.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException)
        {
            // La aplicación se está apagando.
        }
    }

    public static async Task<int> SincronizarTodasAsync(IServiceScopeFactory ambitos, ILogger? log, CancellationToken ct)
    {
        List<Guid> tiendas;
        await using (var ambito = ambitos.CreateAsyncScope())
        {
            var db = ambito.ServiceProvider.GetRequiredService<HermesDbContext>();
            using (db.AccesoATodasLasTiendas())
                tiendas = await db.InstagramConexiones.IgnoreQueryFilters().AsNoTracking()
                    .Where(c => c.Activa && !c.NecesitaReconectar).Select(c => c.TiendaId).ToListAsync(ct);
        }
        var bien = 0;
        foreach (var tienda in tiendas)
        {
            try
            {
                await using var ambito = ambitos.CreateAsyncScope();
                ambito.ServiceProvider.GetRequiredService<TiendaPublicaContexto>().TiendaId = tienda;
                var r = await ambito.ServiceProvider.GetRequiredService<IInstagramConexionService>().SincronizarAsync(ct);
                if (r.Productos > 0) log?.LogInformation("Instagram: {Productos} productos nuevos a la bandeja de la tienda {Tienda}", r.Productos, tienda);
                bien++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log?.LogWarning(ex, "Instagram: no se pudo sincronizar la tienda {Tienda}", tienda);
            }
        }
        return bien;
    }
}
