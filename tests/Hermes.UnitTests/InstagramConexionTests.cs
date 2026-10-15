using System.Security.Cryptography;
using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Domain.Entities;
using Hermes.Infrastructure;
using Hermes.Infrastructure.Imagenes;
using Hermes.Infrastructure.Instagram;
using Hermes.Infrastructure.Mantenimiento;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Security;
using Hermes.Infrastructure.Services;
using Hermes.Infrastructure.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hermes.UnitTests;

// M12 con un Instagram FALSO (no hay app de Meta todavía): el flujo de conexión, el cifrado del token, la renovación, la
// sincronización a la bandeja con fotos, lo que se repite, los errores de Meta y el aislamiento entre tiendas.
public class InstagramConexionTests : IDisposable
{
    private readonly BaseDePrueba _bd = new();
    // Hora REAL: las fotos subidas llevan un Guid v7 con la hora real y la limpieza compara por eso.
    private readonly FakeTimeProvider _reloj = new(DateTimeOffset.UtcNow);
    private readonly InstagramOptions _o = new()
    {
        AppId = "1234567890", AppSecret = "secreto-de-la-app", RedirectUri = "https://panel.test/instagram/conectado",
        ClaveCifrado = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
    };

    public void Dispose() => _bd.Dispose();

    // ───────────── El Instagram falso ─────────────

    private sealed class InstagramFalso : IClienteInstagram
    {
        public const string TokenCorto = "corto-123";
        public string TokenLargo { get; set; } = "LARGO-SECRETO-abc";
        public int ExpiraEn { get; set; } = 60 * 24 * 3600;
        public List<MediaIg> Medias { get; } = [];
        public int Renovaciones { get; private set; }
        public InstagramRechazoException? FallarAlListar { get; set; }
        public List<string> Bajadas { get; } = [];

        public Task<TokenIg> CanjearCodigoAsync(string codigo, CancellationToken ct)
            => codigo == "codigo-bueno" ? Task.FromResult(new TokenIg(TokenCorto, null)) : throw new InstagramRechazoException("Código inválido");
        public Task<TokenIg> TokenLargoAsync(string tokenCorto, CancellationToken ct)
            => Task.FromResult(new TokenIg(tokenCorto == TokenCorto ? TokenLargo : throw new InvalidOperationException(), ExpiraEn));
        public Task<TokenIg> RenovarAsync(string tokenLargo, CancellationToken ct)
        {
            Renovaciones++;
            TokenLargo = $"RENOVADO-{Renovaciones}";
            return Task.FromResult(new TokenIg(TokenLargo, ExpiraEn));
        }
        public Task<PerfilIg> PerfilAsync(string token, CancellationToken ct) => Task.FromResult(new PerfilIg("17841400000000001", "accesorios.mia"));
        public Task<IReadOnlyList<MediaIg>> PublicacionesAsync(string token, int limite, CancellationToken ct)
        {
            if (FallarAlListar is { } e) throw e;
            if (token != TokenLargo) throw new InstagramRechazoException("token equivocado", tokenInvalido: true);
            return Task.FromResult<IReadOnlyList<MediaIg>>(Medias.OrderByDescending(m => m.Fecha).Take(limite).ToList());
        }
        public Task<byte[]> DescargarAsync(string url, CancellationToken ct)
        {
            Bajadas.Add(url);
            return url.Contains("rota") ? throw new InstagramRechazoException("No se pudo bajar") : Task.FromResult(FotoDePrueba.Jpeg(300 + Bajadas.Count, 300));
        }
    }

    private MediaIg Media(string id, string texto, int horasAtras = 1, string tipo = "IMAGE", params string[] hijos)
        => new(id, texto, tipo, tipo == "IMAGE" ? $"https://cdn.test/{id}.jpg" : null, tipo == "VIDEO" ? $"https://cdn.test/{id}-portada.jpg" : null,
            $"https://www.instagram.com/p/{id}/", _reloj.GetUtcNow().AddHours(-horasAtras),
            hijos.Select(h => new HijoIg(h.StartsWith("video") ? "VIDEO" : "IMAGE", h.StartsWith("video") ? null : $"https://cdn.test/{h}.jpg",
                h.StartsWith("video") ? $"https://cdn.test/{h}-portada.jpg" : null)).ToList());

    private record Lado(HermesDbContext Db, InstagramConexionService Conexion, InstagramService Bandeja);

    private Lado ComoDuena(Guid tienda, InstagramFalso ig, InstagramOptions? opciones = null, IImagenService? imagenes = null)
        => ComoDuena(_bd.Contexto(tienda), new BaseDePrueba.TiendaFija(tienda), ig, opciones, imagenes);

    private Lado ComoDuena(HermesDbContext db, ITiendaActual sesion, IClienteInstagram ig, InstagramOptions? opciones = null, IImagenService? imagenes = null)
    {
        var almacen = new AlmacenEnBaseDeDatos(db, _reloj);
        imagenes ??= new ImagenService(db, almacen, sesion, _reloj);
        var productos = new ProductoService(db, imagenes, almacen, sesion, _reloj);
        var bandeja = new InstagramService(db, productos, new StockService(db, new OperacionesStock(db, _reloj), almacen, sesion), sesion, _reloj, almacen);
        var o = Options.Create(opciones ?? _o);
        return new Lado(db, new InstagramConexionService(db, ig, new SecretosInstagram(o), imagenes, sesion, _reloj, o), bandeja);
    }

    private async Task<Lado> Conectada(Guid tienda, InstagramFalso ig)
    {
        var d = ComoDuena(tienda, ig);
        var estado = Uri.UnescapeDataString(d.Conexion.Url().Url.Split("state=")[1]);
        await d.Conexion.ConectarAsync(new ConectarInstagramDto("codigo-bueno#_", estado), default);
        return d;
    }

    // ───────────── Conectar ─────────────

    [Fact]
    public async Task Sin_app_de_Meta_configurada_no_se_puede_conectar_ni_sincronizar()
    {
        var d = ComoDuena(_bd.TiendaA, new InstagramFalso(), new InstagramOptions());

        Assert.Equal((false, false), ((await d.Conexion.EstadoAsync(default)).Configurada, (await d.Conexion.EstadoAsync(default)).Conectada));
        Assert.Throws<InstagramNoDisponibleException>(() => d.Conexion.Url());
        await Assert.ThrowsAsync<InstagramNoDisponibleException>(() => d.Conexion.SincronizarAsync(default));
    }

    [Fact]
    public async Task La_direccion_de_conexion_pide_solo_lo_basico_y_lleva_un_state_firmado()
    {
        var url = ComoDuena(_bd.TiendaA, new InstagramFalso()).Conexion.Url().Url;

        Assert.StartsWith("https://www.instagram.com/oauth/authorize?client_id=1234567890&redirect_uri=https%3A%2F%2Fpanel.test%2Finstagram%2Fconectado", url);
        Assert.Contains("&response_type=code&scope=instagram_business_basic&state=", url);
        Assert.Contains(_bd.TiendaA.ToString("N"), url);
    }

    [Fact]
    public async Task Conectar_guarda_el_token_CIFRADO_y_nunca_lo_muestra()
    {
        var ig = new InstagramFalso();
        var d = await Conectada(_bd.TiendaA, ig);

        var estado = await d.Conexion.EstadoAsync(default);
        Assert.Equal((true, true, "accesorios.mia", false), (estado.Configurada, estado.Conectada, estado.Usuario, estado.NecesitaReconectar));
        var guardada = await _bd.Contexto(_bd.TiendaA).InstagramConexiones.SingleAsync();
        Assert.StartsWith("v1:", guardada.TokenCifrado);
        Assert.DoesNotContain("LARGO-SECRETO", guardada.TokenCifrado);
        Assert.Equal(new SecretosInstagram(Options.Create(_o)).Descifrar(guardada.TokenCifrado), "LARGO-SECRETO-abc");
        Assert.Equal(_reloj.GetUtcNow().UtcDateTime.AddDays(60), guardada.TokenVence);
        Assert.DoesNotContain("LARGO", System.Text.Json.JsonSerializer.Serialize(estado));
    }

    [Fact]
    public async Task Un_state_de_otra_tienda_vencido_o_tocado_no_conecta_nada()
    {
        var ig = new InstagramFalso();
        var secretos = new SecretosInstagram(Options.Create(_o));
        var deB = secretos.FirmarEstado(_bd.TiendaB, _reloj.GetUtcNow().AddMinutes(15));
        var vencido = secretos.FirmarEstado(_bd.TiendaA, _reloj.GetUtcNow().AddMinutes(-1));
        var bueno = secretos.FirmarEstado(_bd.TiendaA, _reloj.GetUtcNow().AddMinutes(15));
        var tocado = bueno[..^3] + "abc";
        var d = ComoDuena(_bd.TiendaA, ig);

        foreach (var estado in new[] { deB, vencido, tocado, "", "basura.123.xyz" })
            await Assert.ThrowsAsync<ValidacionException>(() => d.Conexion.ConectarAsync(new ConectarInstagramDto("codigo-bueno", estado), default));
        await Assert.ThrowsAsync<InstagramRechazoException>(() => d.Conexion.ConectarAsync(new ConectarInstagramDto("codigo-malo", bueno), default));

        Assert.Equal(0, await _bd.Contexto(null).InstagramConexiones.IgnoreQueryFilters().CountAsync());
    }

    // ───────────── Sincronizar ─────────────

    [Fact]
    public async Task Sincronizar_deja_cada_publicacion_en_la_bandeja_con_sus_fotos_y_su_enlace()
    {
        var ig = new InstagramFalso();
        ig.Medias.Add(Media("m1", "Cartera Aurora\nBs 150\nColores: café, negro", horasAtras: 5));
        ig.Medias.Add(Media("m2", "Novedades\n1. Aretes Luna Bs 90\n2. Collar Sol Bs 120", horasAtras: 4, "CAROUSEL_ALBUM", "a", "b"));
        ig.Medias.Add(Media("m3", "Combo\n1. Pulsera Bs 40\n2. Anillo Bs 30", horasAtras: 3, "CAROUSEL_ALBUM", "c", "d", "e"));
        ig.Medias.Add(Media("m4", "Mochila escolar\nBs 200", horasAtras: 2, "VIDEO"));
        ig.Medias.Add(Media("m5", "¡Gracias por los 1000 seguidores! 🎉", horasAtras: 1));
        var d = await Conectada(_bd.TiendaA, ig);

        var r = await d.Conexion.SincronizarAsync(default);

        Assert.Equal((5, 6, 1), (r.Publicaciones, r.Productos, r.SinProductos));
        var bandeja = (await d.Bandeja.ListarAsync(new(), default)).Items;
        Assert.All(bandeja, b => Assert.Equal("Instagram", b.Origen));
        var porNombre = bandeja.ToDictionary(b => b.Nombre);
        Assert.Equal((150m, 1, "https://www.instagram.com/p/m1/"), (porNombre["Cartera Aurora"].Precio!.Value, porNombre["Cartera Aurora"].Fotos!.Count, porNombre["Cartera Aurora"].Enlace));
        // Carrusel con tantas fotos como productos: una para cada uno, en orden.
        Assert.Equal((1, 1), (porNombre["Aretes Luna"].Fotos!.Count, porNombre["Collar Sol"].Fotos!.Count));
        Assert.NotEqual(porNombre["Aretes Luna"].Fotos![0].Id, porNombre["Collar Sol"].Fotos![0].Id);
        // Carrusel con más fotos que productos: no hay cómo repartirlas.
        Assert.Empty(porNombre["Pulsera"].Fotos!);
        Assert.Contains(porNombre["Pulsera"].Avisos, a => a.Contains("agregá las fotos de cada uno al revisar"));
        // Video: la portada.
        Assert.Single(porNombre["Mochila escolar"].Fotos!);
        Assert.Contains(porNombre["Mochila escolar"].Avisos, a => a.Contains("video"));
        Assert.Contains("https://cdn.test/m4-portada.jpg", ig.Bajadas);
        // Nada llegó al catálogo solo.
        Assert.Equal(0, await _bd.Contexto(_bd.TiendaA).Productos.CountAsync());
        Assert.NotNull((await d.Conexion.EstadoAsync(default)).UltimaSincronizacion);
    }

    [Fact]
    public async Task Lo_ya_traido_no_se_repite_y_lo_nuevo_entra_en_la_siguiente()
    {
        var ig = new InstagramFalso();
        ig.Medias.Add(Media("m1", "Cartera\nBs 150"));
        var d = await Conectada(_bd.TiendaA, ig);
        await d.Conexion.SincronizarAsync(default);

        var seguido = await ComoDuena(_bd.TiendaA, ig).Conexion.SincronizarAsync(default);
        Assert.Equal(0, seguido.Publicaciones); // menos de un minuto: ni se pregunta a Instagram
        _reloj.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(0, (await ComoDuena(_bd.TiendaA, ig).Conexion.SincronizarAsync(default)).Publicaciones);

        ig.Medias.Add(Media("m2", "Aretes\nBs 90", horasAtras: 0));
        _reloj.Advance(TimeSpan.FromMinutes(5));
        var r = await ComoDuena(_bd.TiendaA, ig).Conexion.SincronizarAsync(default);

        Assert.Equal((1, 1), (r.Publicaciones, r.Productos));
        Assert.Equal(2, await _bd.Contexto(_bd.TiendaA).Borradores.CountAsync());
    }

    [Fact]
    public async Task La_primera_vez_trae_solo_lo_reciente_y_lo_viejo_no_entra_nunca()
    {
        var ig = new InstagramFalso();
        ig.Medias.Add(Media("vieja", "Cartera vieja\nBs 100", horasAtras: 24 * 45));
        ig.Medias.Add(Media("nueva", "Cartera nueva\nBs 120", horasAtras: 24 * 2));
        var d = await Conectada(_bd.TiendaA, ig);

        await d.Conexion.SincronizarAsync(default);
        _reloj.Advance(TimeSpan.FromMinutes(10));
        await ComoDuena(_bd.TiendaA, ig).Conexion.SincronizarAsync(default);

        Assert.Equal(["Cartera nueva"], await _bd.Contexto(_bd.TiendaA).Borradores.Select(b => b.Nombre).ToListAsync());
    }

    [Fact]
    public async Task El_token_se_renueva_solo_cuando_falta_poco_y_tiene_mas_de_un_dia()
    {
        var ig = new InstagramFalso();
        ig.Medias.Add(Media("m1", "Cartera\nBs 150"));
        await Conectada(_bd.TiendaA, ig);

        _reloj.Advance(TimeSpan.FromDays(10)); // faltan 50 días: no
        await ComoDuena(_bd.TiendaA, ig).Conexion.SincronizarAsync(default);
        Assert.Equal(0, ig.Renovaciones);

        _reloj.Advance(TimeSpan.FromDays(40)); // faltan 10 días: sí
        await ComoDuena(_bd.TiendaA, ig).Conexion.SincronizarAsync(default);
        Assert.Equal(1, ig.Renovaciones);
        var c = await _bd.Contexto(_bd.TiendaA).InstagramConexiones.SingleAsync();
        Assert.Equal(_reloj.GetUtcNow().UtcDateTime.AddDays(60), c.TokenVence);
        Assert.Equal("RENOVADO-1", new SecretosInstagram(Options.Create(_o)).Descifrar(c.TokenCifrado));

        _reloj.Advance(TimeSpan.FromHours(2)); // recién renovado (menos de 24 h): no otra vez
        await ComoDuena(_bd.TiendaA, ig).Conexion.SincronizarAsync(default);
        Assert.Equal(1, ig.Renovaciones);
    }

    [Fact]
    public async Task Si_Meta_dice_que_el_token_no_vale_queda_para_reconectar()
    {
        var ig = new InstagramFalso();
        await Conectada(_bd.TiendaA, ig);
        ig.FallarAlListar = new InstagramRechazoException("Volvé a conectar tu cuenta.", tokenInvalido: true);

        await Assert.ThrowsAsync<InstagramRechazoException>(() => ComoDuena(_bd.TiendaA, ig).Conexion.SincronizarAsync(default));

        var estado = await ComoDuena(_bd.TiendaA, ig).Conexion.EstadoAsync(default);
        Assert.Equal((true, "Volvé a conectar tu cuenta."), (estado.NecesitaReconectar, estado.UltimoError));
        await Assert.ThrowsAsync<InstagramNoDisponibleException>(() => ComoDuena(_bd.TiendaA, ig).Conexion.SincronizarAsync(default));

        // Volver a conectar lo arregla.
        ig.FallarAlListar = null;
        await Conectada(_bd.TiendaA, ig);
        Assert.False((await ComoDuena(_bd.TiendaA, ig).Conexion.EstadoAsync(default)).NecesitaReconectar);
        Assert.Equal(1, await _bd.Contexto(_bd.TiendaA).InstagramConexiones.CountAsync()); // la misma fila, no otra
    }

    [Fact]
    public async Task Un_token_vencido_no_se_usa()
    {
        var ig = new InstagramFalso();
        await Conectada(_bd.TiendaA, ig);
        _reloj.Advance(TimeSpan.FromDays(61));

        await Assert.ThrowsAsync<InstagramRechazoException>(() => ComoDuena(_bd.TiendaA, ig).Conexion.SincronizarAsync(default));

        Assert.True((await ComoDuena(_bd.TiendaA, ig).Conexion.EstadoAsync(default)).NecesitaReconectar);
        Assert.Equal(0, ig.Renovaciones);
    }

    [Fact]
    public async Task Desconectar_borra_el_token()
    {
        var ig = new InstagramFalso();
        var d = await Conectada(_bd.TiendaA, ig);

        await d.Conexion.DesconectarAsync(default);

        Assert.False((await d.Conexion.EstadoAsync(default)).Conectada);
        Assert.Equal("", (await _bd.Contexto(_bd.TiendaA).InstagramConexiones.SingleAsync()).TokenCifrado);
        await Assert.ThrowsAsync<InstagramNoDisponibleException>(() => ComoDuena(_bd.TiendaA, ig).Conexion.SincronizarAsync(default));
    }

    [Fact]
    public async Task Sin_espacio_de_fotos_igual_llega_el_borrador_avisando()
    {
        var ig = new InstagramFalso();
        ig.Medias.Add(Media("m1", "Cartera\nBs 150"));
        await Conectada(_bd.TiendaA, ig);
        var d = ComoDuena(_bd.TiendaA, ig, imagenes: new ImagenesSinEspacio());

        var r = await d.Conexion.SincronizarAsync(default);

        var b = (await d.Bandeja.ListarAsync(new(), default)).Items.Single();
        Assert.Equal(1, r.Productos);
        Assert.Empty(b.Fotos!);
        Assert.Contains(b.Avisos, a => a.Contains("espacio de fotos"));
    }

    private sealed class ImagenesSinEspacio : IImagenService
    {
        public Task<ImagenDto> SubirAsync(byte[] contenido, CancellationToken ct, bool esComprobante = false) => throw new LimiteDePlanException("Sin espacio");
        public Task BorrarAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task Publicar_un_borrador_de_Instagram_usa_sus_fotos_y_la_limpieza_las_respeta_mientras_espera()
    {
        var ig = new InstagramFalso();
        ig.Medias.Add(Media("m1", "Cartera\nBs 150"));
        ig.Medias.Add(Media("m2", "Aretes\nBs 90"));
        var d = await Conectada(_bd.TiendaA, ig);
        await d.Conexion.SincronizarAsync(default);
        var bandeja = (await d.Bandeja.ListarAsync(new(), default)).Items;
        var cartera = bandeja.Single(b => b.Nombre == "Cartera");
        var aretes = bandeja.Single(b => b.Nombre == "Aretes");

        // Dos días después, las fotos de los borradores que esperan NO se limpian.
        var limpieza = new Limpieza(_bd.Contexto(null), new AlmacenEnBaseDeDatos(_bd.Contexto(null), _reloj), new FakeTimeProvider(DateTimeOffset.UtcNow.AddDays(2)),
            Options.Create(new LimpiezaOptions()), Options.Create(new JwtOptions()));
        Assert.Equal(0, (await limpieza.EjecutarAsync(default)).FotosBorradas);

        var publicado = await ComoDuena(_bd.TiendaA, ig).Bandeja.PublicarAsync(cartera.Id,
            new(new GuardarProductoDto("Cartera", null, 150, 0, null, true, null, null, cartera.Fotos!.Select(f => f.Id).ToList())), default);
        Assert.Equal(cartera.Fotos![0].Id, publicado.Producto.Fotos.Single().Id);

        // Descartar los aretes: su foto ya no sirve y la limpieza se la lleva.
        await ComoDuena(_bd.TiendaA, ig).Bandeja.DescartarAsync(aretes.Id, default);
        var otraLimpieza = new Limpieza(_bd.Contexto(null), new AlmacenEnBaseDeDatos(_bd.Contexto(null), _reloj), new FakeTimeProvider(DateTimeOffset.UtcNow.AddDays(2)),
            Options.Create(new LimpiezaOptions()), Options.Create(new JwtOptions()));
        Assert.Equal(1, (await otraLimpieza.EjecutarAsync(default)).FotosBorradas);
        Assert.Equal([cartera.Fotos[0].Id], await _bd.Contexto(null).Imagenes.IgnoreQueryFilters().Select(i => i.Id).ToListAsync());
    }

    [Fact]
    public async Task Cada_tienda_tiene_su_conexion_y_sus_publicaciones()
    {
        var igA = new InstagramFalso();
        igA.Medias.Add(Media("compartida", "Cartera\nBs 150"));
        var igB = new InstagramFalso();
        igB.Medias.Add(Media("compartida", "Mochila\nBs 200"));
        await (await Conectada(_bd.TiendaA, igA)).Conexion.SincronizarAsync(default);

        var b = ComoDuena(_bd.TiendaB, igB);
        Assert.False((await b.Conexion.EstadoAsync(default)).Conectada);
        await Assert.ThrowsAsync<InstagramNoDisponibleException>(() => b.Conexion.SincronizarAsync(default));
        await (await Conectada(_bd.TiendaB, igB)).Conexion.SincronizarAsync(default); // el mismo MediaId en otra tienda sí entra

        Assert.Equal(["Cartera"], await _bd.Contexto(_bd.TiendaA).Borradores.Select(x => x.Nombre).ToListAsync());
        Assert.Equal(["Mochila"], await _bd.Contexto(_bd.TiendaB).Borradores.Select(x => x.Nombre).ToListAsync());
    }

    [Fact]
    public async Task La_tarea_periodica_sincroniza_cada_tienda_con_su_propia_tienda_fijada()
    {
        var ig = new InstagramFalso();
        await Conectada(_bd.TiendaA, ig);
        await Conectada(_bd.TiendaB, ig);
        ig.Medias.Add(Media("m1", "Cartera\nBs 150"));

        var servicios = new ServiceCollection();
        servicios.AddScoped<TiendaPublicaContexto>();
        servicios.AddScoped<ITiendaActual>(sp => sp.GetRequiredService<TiendaPublicaContexto>());
        servicios.AddScoped(sp => _bd.ContextoPublico(sp.GetRequiredService<ITiendaActual>()));
        servicios.AddScoped<IInstagramConexionService>(sp =>
            ComoDuena(sp.GetRequiredService<HermesDbContext>(), sp.GetRequiredService<ITiendaActual>(), ig).Conexion);
        await using var proveedor = servicios.BuildServiceProvider();

        var bien = await SincronizacionInstagramPeriodica.SincronizarTodasAsync(proveedor.GetRequiredService<IServiceScopeFactory>(), null, default);

        Assert.Equal(2, bien);
        Assert.Equal(1, await _bd.Contexto(_bd.TiendaA).Borradores.CountAsync());
        Assert.Equal(1, await _bd.Contexto(_bd.TiendaB).Borradores.CountAsync());
    }
}
