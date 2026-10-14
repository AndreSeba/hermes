using System.IdentityModel.Tokens.Jwt;
using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Infrastructure.Security;
using Hermes.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hermes.UnitTests;

public class AuthTests : IDisposable
{
    private readonly BaseDePrueba _bd = new();
    private readonly FakeTimeProvider _reloj = new(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
    private readonly BloqueoDeLogin _bloqueo;
    private readonly IOptions<JwtOptions> _opciones = Options.Create(new JwtOptions
    {
        Issuer = "hermes-api",
        Audience = "hermes-web",
        SecretKey = "clave-de-prueba-de-al-menos-32-caracteres!!",
        ExpiracionMinutos = 15,
        RefreshDias = 30,
    });

    private static readonly RegistroDto Registro = new("Luna", "  Luna@Ejemplo.com ", "secreta123", "Luna Accesorios", "luna");

    public AuthTests() => _bloqueo = new BloqueoDeLogin(new MemoryCache(new MemoryCacheOptions()), _reloj);

    public void Dispose() => _bd.Dispose();

    // Un contexto nuevo por llamada, como un request real: ExecuteUpdate no refresca lo ya rastreado.
    private async Task<T> ConServicio<T>(Func<AuthService, Task<T>> accion)
    {
        await using var db = _bd.Contexto(null);
        return await accion(new AuthService(db, new JwtTokenService(_opciones, _reloj), _bloqueo, _reloj, _opciones));
    }

    private Task Ejecutar(Func<AuthService, Task> accion) => ConServicio(async s => { await accion(s); return 0; });

    [Fact]
    public async Task Registro_crea_usuario_y_tienda_y_el_token_lleva_la_tienda()
    {
        var emitida = await ConServicio(s => s.RegistrarAsync(Registro, default));

        Assert.Equal("luna@ejemplo.com", emitida.Sesion.Usuario.Email);
        Assert.Equal("luna", emitida.Sesion.Tienda.Slug);
        Assert.Equal(_reloj.GetUtcNow().AddMinutes(15), emitida.Sesion.ExpiraEn);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(emitida.Sesion.AccessToken);
        Assert.Equal(emitida.Sesion.Tienda.Id.ToString(), jwt.Claims.Single(c => c.Type == JwtTokenService.ClaimTienda).Value);
        Assert.Equal(emitida.Sesion.Usuario.Id.ToString(), jwt.Subject);
    }

    [Fact]
    public async Task La_base_guarda_el_hash_del_refresh_token_y_de_la_contrasena_nunca_el_valor()
    {
        var emitida = await ConServicio(s => s.RegistrarAsync(Registro, default));

        await using var db = _bd.Contexto(null);
        var guardado = await db.RefreshTokens.SingleAsync();
        var usuario = await db.Usuarios.SingleAsync(u => u.Email == "luna@ejemplo.com");
        Assert.NotEqual(emitida.RefreshToken, guardado.TokenHash);
        Assert.Equal(JwtTokenService.Hash(emitida.RefreshToken), guardado.TokenHash);
        Assert.NotEqual("secreta123", usuario.PasswordHash);
    }

    [Fact]
    public async Task Registro_rechaza_email_repetido_slug_ocupado_reservado_y_contrasena_debil()
    {
        await ConServicio(s => s.RegistrarAsync(Registro, default));

        await Assert.ThrowsAsync<EmailYaRegistradoException>(() => ConServicio(s => s.RegistrarAsync(Registro with { Slug = "otra" }, default)));
        await Assert.ThrowsAsync<SlugNoDisponibleException>(() => ConServicio(s => s.RegistrarAsync(Registro with { Email = "otra@ejemplo.com" }, default)));
        await Assert.ThrowsAsync<ValidacionException>(() => ConServicio(s => s.RegistrarAsync(Registro with { Email = "b@ejemplo.com", Slug = "admin" }, default)));
        await Assert.ThrowsAsync<ValidacionException>(() => ConServicio(s => s.RegistrarAsync(Registro with { Email = "c@ejemplo.com", Slug = "Con Espacios" }, default)));
        await Assert.ThrowsAsync<ValidacionException>(() => ConServicio(s => s.RegistrarAsync(Registro with { Email = "d@ejemplo.com", Slug = "d", Password = "sololetras" }, default)));
    }

    [Fact]
    public async Task Login_con_email_en_mayusculas_entra_y_credenciales_malas_dan_el_mismo_error()
    {
        await ConServicio(s => s.RegistrarAsync(Registro, default));

        var sesion = await ConServicio(s => s.LoginAsync(new LoginDto("LUNA@ejemplo.com", "secreta123"), default));
        Assert.Equal("luna", sesion.Sesion.Tienda.Slug);

        await Assert.ThrowsAsync<CredencialesInvalidasException>(() => ConServicio(s => s.LoginAsync(new LoginDto("luna@ejemplo.com", "otra12345"), default)));
        await Assert.ThrowsAsync<CredencialesInvalidasException>(() => ConServicio(s => s.LoginAsync(new LoginDto("nadie@ejemplo.com", "secreta123"), default)));
    }

    [Fact]
    public async Task Cinco_fallos_bloquean_el_login_aunque_despues_la_contrasena_sea_correcta()
    {
        await ConServicio(s => s.RegistrarAsync(Registro, default));
        for (var i = 0; i < BloqueoDeLogin.MaximoFallos; i++)
            await Assert.ThrowsAsync<CredencialesInvalidasException>(() => ConServicio(s => s.LoginAsync(new LoginDto("luna@ejemplo.com", "mala12345"), default)));

        await Assert.ThrowsAsync<LoginBloqueadoException>(() => ConServicio(s => s.LoginAsync(new LoginDto("luna@ejemplo.com", "secreta123"), default)));

        _reloj.Advance(BloqueoDeLogin.Ventana + TimeSpan.FromSeconds(1));
        var sesion = await ConServicio(s => s.LoginAsync(new LoginDto("luna@ejemplo.com", "secreta123"), default));
        Assert.NotNull(sesion.Sesion.AccessToken);
    }

    [Fact]
    public async Task Renovar_rota_el_refresh_token_y_conserva_la_tienda()
    {
        var registro = await ConServicio(s => s.RegistrarAsync(Registro, default));

        var renovada = await ConServicio(s => s.RenovarAsync(registro.RefreshToken, default));

        Assert.NotEqual(registro.RefreshToken, renovada.RefreshToken);
        Assert.Equal(registro.Sesion.Tienda.Id, renovada.Sesion.Tienda.Id);
    }

    [Fact]
    public async Task Dos_pestanas_renovando_a_la_vez_no_cierran_la_sesion()
    {
        var registro = await ConServicio(s => s.RegistrarAsync(Registro, default));
        var pestanaA = await ConServicio(s => s.RenovarAsync(registro.RefreshToken, default));

        // La pestaña B llega 2 s después con la cookie vieja: se rechaza…
        _reloj.Advance(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<SesionInvalidaException>(() => ConServicio(s => s.RenovarAsync(registro.RefreshToken, default)));

        // …pero sin castigo: la cookie nueva (que B reintenta) sigue sirviendo.
        var reintentoB = await ConServicio(s => s.RenovarAsync(pestanaA.RefreshToken, default));
        Assert.NotNull(reintentoB.Sesion.AccessToken);
    }

    [Fact]
    public async Task Reusar_un_refresh_token_ya_usado_cierra_todas_las_sesiones()
    {
        var registro = await ConServicio(s => s.RegistrarAsync(Registro, default));
        var renovada = await ConServicio(s => s.RenovarAsync(registro.RefreshToken, default));
        _reloj.Advance(AuthService.MargenRenovacionSimultanea + TimeSpan.FromSeconds(1));

        // Alguien presenta el token viejo (robado): se rechaza…
        await Assert.ThrowsAsync<SesionInvalidaException>(() => ConServicio(s => s.RenovarAsync(registro.RefreshToken, default)));
        // …y el nuevo, que tiene el usuario legítimo, también deja de servir.
        await Assert.ThrowsAsync<SesionInvalidaException>(() => ConServicio(s => s.RenovarAsync(renovada.RefreshToken, default)));
    }

    [Fact]
    public async Task Un_refresh_token_vencido_o_cerrado_no_renueva()
    {
        var a = await ConServicio(s => s.RegistrarAsync(Registro, default));
        var b = await ConServicio(s => s.LoginAsync(new LoginDto("luna@ejemplo.com", "secreta123"), default));

        await Ejecutar(s => s.CerrarSesionAsync(a.RefreshToken, default));
        await Assert.ThrowsAsync<SesionInvalidaException>(() => ConServicio(s => s.RenovarAsync(a.RefreshToken, default)));

        _reloj.Advance(TimeSpan.FromDays(31));
        await Assert.ThrowsAsync<SesionInvalidaException>(() => ConServicio(s => s.RenovarAsync(b.RefreshToken, default)));
        await Assert.ThrowsAsync<SesionInvalidaException>(() => ConServicio(s => s.RenovarAsync("inventado", default)));
    }

    [Fact]
    public async Task Cerrar_sesion_con_un_token_desconocido_no_es_error()
        => await Ejecutar(s => s.CerrarSesionAsync("no-existe", default));
}
