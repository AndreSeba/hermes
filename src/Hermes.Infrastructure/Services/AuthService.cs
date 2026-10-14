using Hermes.Application;
using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Domain.Entities;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hermes.Infrastructure.Services;

// Usuario, Tienda y RefreshToken no implementan IPerteneceATienda: acá todavía no hay sesión,
// así que no les aplica el filtro global y cada consulta acota a mano por usuario/dueño.
public class AuthService : IAuthService
{
    private const int CostoBCrypt = 12;

    // Hash de una contraseña que nadie conoce: si el email no existe se verifica contra este,
    // así la respuesta tarda lo mismo y no revela qué cuentas existen.
    private static readonly string HashFicticio = BCrypt.Net.BCrypt.HashPassword("hermes-sin-cuenta-0", CostoBCrypt);

    private readonly HermesDbContext _db;
    private readonly JwtTokenService _jwt;
    private readonly BloqueoDeLogin _bloqueo;
    private readonly TimeProvider _reloj;
    private readonly JwtOptions _opciones;

    public AuthService(HermesDbContext db, JwtTokenService jwt, BloqueoDeLogin bloqueo, TimeProvider reloj, IOptions<JwtOptions> opciones)
    {
        _db = db;
        _jwt = jwt;
        _bloqueo = bloqueo;
        _reloj = reloj;
        _opciones = opciones.Value;
    }

    public async Task<SesionEmitida> RegistrarAsync(RegistroDto dto, CancellationToken ct)
    {
        var nombre = Validacion.Texto(dto.Nombre, 120, "Tu nombre");
        var email = Validacion.Email(dto.Email);
        var password = Validacion.Password(dto.Password);
        var nombreTienda = Validacion.Texto(dto.NombreTienda, 120, "El nombre de la tienda");
        var slug = Validacion.Slug(dto.Slug);

        if (await _db.Usuarios.AnyAsync(u => u.Email == email, ct))
            throw new EmailYaRegistradoException();
        if (await _db.Tiendas.AnyAsync(t => t.Slug == slug, ct))
            throw new SlugNoDisponibleException(slug);

        var ahora = _reloj.GetUtcNow();
        var usuario = new Usuario
        {
            Nombre = nombre,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, CostoBCrypt),
            CreadoEn = ahora,
            UltimoLoginEn = ahora,
        };
        var tienda = new Tienda { DuenoId = usuario.Id, Nombre = nombreTienda, Slug = slug, CreadaEn = ahora };

        // Un solo SaveChanges: o se crean los dos o ninguno. Si otro registro gana la carrera por
        // el mismo email/slug, el índice único lo frena y el manejador global responde 409.
        _db.Usuarios.Add(usuario);
        _db.Tiendas.Add(tienda);
        return await EmitirSesionAsync(usuario, tienda, ct);
    }

    public async Task<SesionEmitida> LoginAsync(LoginDto dto, CancellationToken ct)
    {
        var email = (dto.Email ?? string.Empty).Trim().ToLowerInvariant();
        var password = dto.Password ?? string.Empty;

        if (_bloqueo.EstaBloqueado(email))
            throw new LoginBloqueadoException((int)BloqueoDeLogin.Ventana.TotalMinutes);

        var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.Email == email && u.Activo, ct);
        var valida = BCrypt.Net.BCrypt.Verify(password, usuario?.PasswordHash ?? HashFicticio);
        if (usuario is null || !valida)
        {
            _bloqueo.RegistrarFallo(email);
            throw new CredencialesInvalidasException();
        }

        _bloqueo.Limpiar(email);

        // Por ahora una persona opera una tienda: la primera que creó. Cambiar de tienda será
        // un endpoint aparte que emita un token nuevo con otra.
        var tienda = await _db.Tiendas.Where(t => t.DuenoId == usuario.Id && t.Activa)
            .OrderBy(t => t.Id) // Guid v7: ordenar por Id es ordenar por creación.
            .FirstOrDefaultAsync(ct)
            ?? throw new SinTiendaEnSesionException();

        usuario.UltimoLoginEn = _reloj.GetUtcNow();
        return await EmitirSesionAsync(usuario, tienda, ct);
    }

    public async Task<SesionEmitida> RenovarAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) throw new SesionInvalidaException();

        var hash = JwtTokenService.Hash(refreshToken);
        var actual = await _db.RefreshTokens.Include(r => r.Usuario).FirstOrDefaultAsync(r => r.TokenHash == hash, ct)
            ?? throw new SesionInvalidaException();

        var ahora = _reloj.GetUtcNow();

        if (actual.RevocadoEn is not null)
        {
            // Un token ya usado vuelve a aparecer: lo tiene alguien más. Se cierran todas las sesiones.
            await _db.RefreshTokens.Where(r => r.UsuarioId == actual.UsuarioId && r.RevocadoEn == null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevocadoEn, ahora), ct);
            throw new SesionInvalidaException();
        }

        if (actual.ExpiraEn <= ahora || actual.Usuario is not { Activo: true })
            throw new SesionInvalidaException();

        var tienda = await _db.Tiendas.FirstOrDefaultAsync(t => t.Id == actual.TiendaId && t.DuenoId == actual.UsuarioId && t.Activa, ct)
            ?? throw new SesionInvalidaException();

        await using var transaccion = await _db.Database.BeginTransactionAsync(ct);

        // Revocar es un UPDATE condicional: si dos pedidos llegan con el mismo token a la vez,
        // solo uno lo rota; el otro recibe 401.
        var nuevoId = Guid.CreateVersion7();
        var revocados = await _db.RefreshTokens.Where(r => r.Id == actual.Id && r.RevocadoEn == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.RevocadoEn, ahora)
                .SetProperty(r => r.ReemplazadoPorId, nuevoId), ct);
        if (revocados == 0) throw new SesionInvalidaException();

        var sesion = await EmitirSesionAsync(actual.Usuario, tienda, ct, nuevoId);
        await transaccion.CommitAsync(ct);
        return sesion;
    }

    public async Task CerrarSesionAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;

        var hash = JwtTokenService.Hash(refreshToken);
        var ahora = _reloj.GetUtcNow();
        await _db.RefreshTokens.Where(r => r.TokenHash == hash && r.RevocadoEn == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevocadoEn, ahora), ct);
    }

    public async Task<PerfilDto> ObtenerPerfilAsync(Guid usuarioId, Guid tiendaId, CancellationToken ct)
    {
        var usuario = await _db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == usuarioId && u.Activo, ct)
            ?? throw new SesionInvalidaException();
        var tienda = await _db.Tiendas.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tiendaId && t.DuenoId == usuarioId, ct)
            ?? throw new SesionInvalidaException();

        return new PerfilDto(UsuarioDto(usuario), TiendaDto(tienda));
    }

    // Emite el token de acceso + un refresh token nuevo y guarda TODO lo pendiente del contexto
    // (el alta del registro, el último login) en el mismo SaveChanges.
    private async Task<SesionEmitida> EmitirSesionAsync(Usuario usuario, Tienda tienda, CancellationToken ct, Guid? refreshId = null)
    {
        var ahora = _reloj.GetUtcNow();
        var (accessToken, expira) = _jwt.EmitirAccessToken(usuario, tienda);

        var refresh = JwtTokenService.GenerarRefreshToken();
        var refreshExpira = ahora.AddDays(_opciones.RefreshDias);
        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = refreshId ?? Guid.CreateVersion7(),
            UsuarioId = usuario.Id,
            TiendaId = tienda.Id,
            TokenHash = JwtTokenService.Hash(refresh),
            CreadoEn = ahora,
            ExpiraEn = refreshExpira,
        });
        await _db.SaveChangesAsync(ct);

        return new SesionEmitida(new SesionDto(accessToken, expira, UsuarioDto(usuario), TiendaDto(tienda)), refresh, refreshExpira);
    }

    private static UsuarioSesionDto UsuarioDto(Usuario u) => new(u.Id, u.Nombre, u.Email);

    private static TiendaSesionDto TiendaDto(Tienda t) => new(t.Id, t.Nombre, t.Slug);
}
