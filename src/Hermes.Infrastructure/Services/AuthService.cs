using Hermes.Application;
using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Domain.Entities;
using Hermes.Domain.Seguridad;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hermes.Infrastructure.Services;

// Usuario, Tienda y RefreshToken no implementan IPerteneceATienda: acá todavía no hay sesión,
// así que no les aplica el filtro global y cada consulta acota a mano por usuario/dueño.
// Lo que sí es de la tienda (miembros, roles y sus permisos) se lee dentro de ResolverAccesoAsync, a propósito, sin filtro.
public class AuthService : IAuthService
{
    private const int CostoBCrypt = 12;
    public static readonly TimeSpan MargenRenovacionSimultanea = TimeSpan.FromSeconds(30);

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
        return await EmitirSesionAsync(usuario, ComoDueno(tienda), ct);
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

        // Por ahora una persona opera una tienda: la primera que creó o, si no es dueña de ninguna, en la que trabaja.
        // Cambiar de tienda será un endpoint aparte que emita un token nuevo con otra.
        var acceso = await ResolverAccesoAsync(usuario, null, ct) ?? throw new SinTiendaEnSesionException();

        usuario.UltimoLoginEn = _reloj.GetUtcNow();
        return await EmitirSesionAsync(usuario, acceso, ct);
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
            // Rotado hace instantes: casi seguro dos pestañas renovando a la vez (comparten la cookie).
            // Se rechaza sin castigo; la pestaña que perdió reintenta con la cookie nueva.
            if (actual.ReemplazadoPorId is not null && ahora - actual.RevocadoEn.Value < MargenRenovacionSimultanea)
                throw new SesionInvalidaException();

            // Un token ya usado vuelve a aparecer: lo tiene alguien más. Se cierran todas las sesiones.
            await _db.RefreshTokens.Where(r => r.UsuarioId == actual.UsuarioId && r.RevocadoEn == null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevocadoEn, ahora), ct);
            throw new SesionInvalidaException();
        }

        if (actual.ExpiraEn <= ahora || actual.Usuario is not { Activo: true })
            throw new SesionInvalidaException();

        // Los permisos se vuelven a leer en CADA renovación: un cambio de rol (o una baja) llega a la sesión en cuanto el token
        // de acceso se renueva. Si ya no tiene acceso a esa tienda, la sesión se cierra.
        var acceso = await ResolverAccesoAsync(actual.Usuario, actual.TiendaId, ct)
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

        var sesion = await EmitirSesionAsync(actual.Usuario, acceso, ct, nuevoId);
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
        var acceso = await ResolverAccesoAsync(usuario, tiendaId, ct) ?? throw new SesionInvalidaException();

        return new PerfilDto(UsuarioDto(usuario), TiendaDto(acceso.Tienda), acceso.ADto());
    }

    public async Task CambiarPasswordAsync(Guid usuarioId, string? refreshActual, CambiarPasswordDto dto, CancellationToken ct)
    {
        var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId && u.Activo, ct)
            ?? throw new SesionInvalidaException();

        var actual = dto.PasswordActual ?? string.Empty;
        if (!BCrypt.Net.BCrypt.Verify(actual, usuario.PasswordHash))
            throw new ValidacionException("La contraseña actual no es correcta.");
        var nueva = Validacion.Password(dto.PasswordNueva);
        if (nueva == actual)
            throw new ValidacionException("Elegí una contraseña distinta a la actual.");

        usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(nueva, CostoBCrypt);
        usuario.DebeCambiarPassword = false;
        await _db.SaveChangesAsync(ct);

        // Las demás sesiones se cierran: quien conocía la contraseña anterior (o la provisoria) no sigue adentro.
        var ahora = _reloj.GetUtcNow();
        var otras = _db.RefreshTokens.Where(r => r.UsuarioId == usuarioId && r.RevocadoEn == null);
        if (!string.IsNullOrWhiteSpace(refreshActual))
        {
            var hash = JwtTokenService.Hash(refreshActual);
            otras = otras.Where(r => r.TokenHash != hash);
        }
        await otras.ExecuteUpdateAsync(s => s.SetProperty(r => r.RevocadoEn, ahora), ct);
    }

    // ───────────── Qué puede hacer esta persona y en qué tienda ─────────────

    private sealed record AccesoResuelto(Tienda Tienda, bool EsDueno, string Rol, IReadOnlyList<string> Permisos)
    {
        public AccesoDto ADto() => new(EsDueno, Rol, Permisos);
    }

    private static AccesoResuelto ComoDueno(Tienda tienda) => new(tienda, true, "Dueño", Permisos.Todos);

    // La tienda en la que opera (la pedida, o la primera) y lo que puede hacer en ella: el dueño tiene TODO el catálogo de
    // permisos; alguien del equipo, los de su rol. null = no tiene acceso (no es de esa tienda, está desactivado o el rol
    // quedó desactivado).
    private async Task<AccesoResuelto?> ResolverAccesoAsync(Usuario usuario, Guid? tiendaId, CancellationToken ct)
    {
        // Acá todavía no hay tienda en la sesión: miembros, roles y permisos son de la tienda, así que se leen SIN el filtro
        // global y dentro de este bloque (para Row Level Security). Todo acotado a esta persona y a las tiendas donde figura.
        using var _ = _db.AccesoATodasLasTiendas();

        var propias = _db.Tiendas.Where(t => t.DuenoId == usuario.Id && t.Activa);
        if (tiendaId is { } propia) propias = propias.Where(t => t.Id == propia);
        var dueno = await propias.OrderBy(t => t.Id).FirstOrDefaultAsync(ct); // Guid v7: ordenar por Id es ordenar por creación.
        if (dueno is not null) return ComoDueno(dueno);

        var miembros = _db.Miembros.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.UsuarioId == usuario.Id && m.Activo && m.Rol!.Activo);
        if (tiendaId is { } pedida) miembros = miembros.Where(m => m.TiendaId == pedida);

        foreach (var m in await miembros.OrderBy(m => m.TiendaId).Select(m => new { m.TiendaId, m.RolId, Rol = m.Rol!.Nombre }).ToListAsync(ct))
        {
            var tienda = await _db.Tiendas.FirstOrDefaultAsync(t => t.Id == m.TiendaId && t.Activa, ct);
            if (tienda is null) continue;

            var codigos = await _db.RolPermisos.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.RolId == m.RolId).Select(p => p.Codigo).ToListAsync(ct);
            // Solo los que el catálogo conoce (si un permiso se quitó del código, deja de valer).
            return new AccesoResuelto(tienda, false, m.Rol, codigos.Where(Permisos.Existe).Distinct().Order().ToList());
        }
        return null;
    }

    // Emite el token de acceso + un refresh token nuevo y guarda TODO lo pendiente del contexto
    // (el alta del registro, el último login) en el mismo SaveChanges.
    private async Task<SesionEmitida> EmitirSesionAsync(Usuario usuario, AccesoResuelto acceso, CancellationToken ct, Guid? refreshId = null)
    {
        var tienda = acceso.Tienda;
        var ahora = _reloj.GetUtcNow();
        var (accessToken, expira) = _jwt.EmitirAccessToken(usuario, tienda, acceso.Permisos);

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

        return new SesionEmitida(new SesionDto(accessToken, expira, UsuarioDto(usuario), TiendaDto(tienda), acceso.ADto()), refresh, refreshExpira);
    }

    private static UsuarioSesionDto UsuarioDto(Usuario u) => new(u.Id, u.Nombre, u.Email, u.EsAdministrador, u.DebeCambiarPassword);

    private static TiendaSesionDto TiendaDto(Tienda t) => new(t.Id, t.Nombre, t.Slug);
}
