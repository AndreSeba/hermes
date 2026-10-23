using Hermes.Application;
using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Domain.Entities;
using Hermes.Domain.Seguridad;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Planes;
using Microsoft.EntityFrameworkCore;

namespace Hermes.Infrastructure.Services;

// Las personas del negocio. MiembroTienda y Rol son de la tienda (filtro global); Usuario no (es una cuenta): acá solo se
// tocan las cuentas que ESTE servicio acaba de crear o que figuran como miembros de la tienda de la sesión.
//
// Reglas: el dueño no figura como miembro (no se edita ni se desactiva); nadie se desactiva ni se cambia el rol a sí mismo; y
// nadie le da a otro (ni administra a alguien) con más permisos que los que tiene él.
public class EquipoService : IEquipoService
{
    private const int CostoBCrypt = 12;

    private readonly HermesDbContext _db;
    private readonly ITiendaActual _sesion;
    private readonly PlanesDeTienda _planes;
    private readonly TimeProvider _reloj;
    private readonly ILimitesDePlan? _limites;
    private readonly IUsuarioActual? _usuario;
    private readonly ISesionesRevocadas? _sesiones;

    public EquipoService(HermesDbContext db, ITiendaActual sesion, PlanesDeTienda planes, TimeProvider reloj,
        ILimitesDePlan? limites = null, IUsuarioActual? usuario = null, ISesionesRevocadas? sesiones = null)
    {
        _db = db;
        _sesion = sesion;
        _planes = planes;
        _reloj = reloj;
        _limites = limites;
        _usuario = usuario;
        _sesiones = sesiones;
    }

    private Guid TiendaIdDeLaSesion() => _sesion.TiendaId ?? throw new SinTiendaEnSesionException();

    public async Task<EquipoDto> ListarAsync(CancellationToken ct)
    {
        var tiendaId = TiendaIdDeLaSesion();
        var dueno = await _db.Tiendas.AsNoTracking().Where(t => t.Id == tiendaId).Select(t => t.Dueno!).FirstAsync(ct);
        var miembros = await _db.Miembros.AsNoTracking().Include(m => m.Usuario).Include(m => m.Rol)
            .OrderByDescending(m => m.Activo).ThenBy(m => m.Usuario!.Nombre).ToListAsync(ct);
        var (plan, _) = await _planes.VigenteAsync(tiendaId, ct);

        var lista = new List<MiembroDto>
        {
            new(dueno.Id, dueno.Id, dueno.Nombre, dueno.Email, true, null, "Dueño", true, dueno.CreadoEn, dueno.UltimoLoginEn),
        };
        lista.AddRange(miembros.Select(ADto));
        return new EquipoDto(lista, 1 + miembros.Count(m => m.Activo), plan.MaxUsuarios, plan.Nombre);
    }

    public async Task<MiembroDto> CrearAsync(CrearMiembroDto dto, CancellationToken ct)
    {
        var tiendaId = TiendaIdDeLaSesion();
        var nombre = Validacion.Texto(dto.Nombre, 120, "El nombre");
        var email = Validacion.Email(dto.Email);
        var password = Validacion.Password(dto.Password);

        var rol = await _db.Roles.Include(r => r.Permisos).FirstOrDefaultAsync(r => r.Id == dto.RolId && r.Activo, ct)
            ?? throw new RolNoEncontradoException(dto.RolId);
        ExigirQuePuedaDar(rol);

        if (await _db.Usuarios.AnyAsync(u => u.Email == email, ct)) throw new CorreoDeEquipoEnUsoException(email);
        // Después de las validaciones baratas: el límite del plan cuenta al dueño y a las personas activas.
        if (_limites is not null) await _limites.ExigirPoderSumarPersonaAsync(ct);

        var ahora = _reloj.GetUtcNow();
        var usuario = new Usuario
        {
            Nombre = nombre,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, CostoBCrypt),
            // La contraseña la eligió el dueño: la persona tiene que poner la suya la primera vez que entra.
            DebeCambiarPassword = true,
            CreadoEn = ahora,
        };
        var miembro = new MiembroTienda { UsuarioId = usuario.Id, RolId = rol.Id, CreadoEn = ahora };

        try
        {
            _db.Usuarios.Add(usuario);
            _db.Miembros.Add(miembro);
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Otro alta con el mismo correo en el mismo instante: el índice único de email la frenó.
            _db.ChangeTracker.Clear();
            throw new CorreoDeEquipoEnUsoException(email);
        }

        miembro.Usuario = usuario;
        miembro.Rol = rol;
        return ADto(miembro);
    }

    public async Task<MiembroDto> ActualizarAsync(Guid id, ActualizarMiembroDto dto, CancellationToken ct)
    {
        var tiendaId = TiendaIdDeLaSesion();
        var miembro = await BuscarAsync(id, ct);
        var rolNuevo = await _db.Roles.Include(r => r.Permisos).FirstOrDefaultAsync(r => r.Id == dto.RolId && r.Activo, ct)
            ?? throw new RolNoEncontradoException(dto.RolId);

        var esUno = _usuario?.UsuarioId == miembro.UsuarioId;
        if (esUno && !dto.Activo) throw new OperacionNoPermitidaException("No podés desactivarte a vos mismo: te quedarías sin acceso.");
        if (esUno && dto.RolId != miembro.RolId) throw new OperacionNoPermitidaException("No podés cambiarte el rol a vos mismo.");

        // No se administra a alguien con más permisos que los propios, ni se le da un rol con más de lo que uno tiene.
        await ExigirQuePuedaAdministrarAsync(miembro, ct);
        if (dto.RolId != miembro.RolId) ExigirQuePuedaDar(rolNuevo);
        if (dto.Activo && !miembro.Activo && _limites is not null) await _limites.ExigirPoderSumarPersonaAsync(ct);

        var cambio = dto.RolId != miembro.RolId || dto.Activo != miembro.Activo;
        var seDesactiva = miembro.Activo && !dto.Activo;
        miembro.RolId = rolNuevo.Id;
        miembro.Rol = rolNuevo;
        miembro.Activo = dto.Activo;
        await _db.SaveChangesAsync(ct);

        // Sus sesiones abiertas se renuevan con lo de ahora; si lo desactivaron, se cierran del todo.
        if (cambio) _sesiones?.Revocar(miembro.UsuarioId);
        if (seDesactiva) await CerrarSesionesAsync(miembro.UsuarioId, tiendaId, ct);
        return ADto(miembro);
    }

    public async Task<MiembroDto> RestablecerPasswordAsync(Guid id, RestablecerPasswordDto dto, CancellationToken ct)
    {
        TiendaIdDeLaSesion();
        var miembro = await BuscarAsync(id, ct);
        var password = Validacion.Password(dto.Password);
        if (_usuario?.UsuarioId == miembro.UsuarioId)
            throw new OperacionNoPermitidaException("Tu propia contraseña la cambiás desde tu perfil.");
        await ExigirQuePuedaAdministrarAsync(miembro, ct);

        miembro.Usuario!.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, CostoBCrypt);
        miembro.Usuario.DebeCambiarPassword = true;
        await _db.SaveChangesAsync(ct);

        // La contraseña anterior ya no vale: se cierran sus sesiones abiertas.
        _sesiones?.Revocar(miembro.UsuarioId);
        await CerrarSesionesAsync(miembro.UsuarioId, null, ct);
        return ADto(miembro);
    }

    // ───────────── Ayudas ─────────────

    private async Task<MiembroTienda> BuscarAsync(Guid id, CancellationToken ct)
        => await _db.Miembros.Include(m => m.Usuario).Include(m => m.Rol).FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new MiembroNoEncontradoException(id);

    // Quien da el rol tiene que tener TODOS sus permisos.
    private void ExigirQuePuedaDar(Rol rol)
    {
        if (_usuario is null) return;
        var faltante = rol.Permisos.Select(p => p.Codigo).FirstOrDefault(c => !_usuario.Tiene(c));
        if (faltante is not null)
            throw new SinPermisoException($"No podés dar el rol «{rol.Nombre}»: incluye permisos que vos no tenés.");
    }

    // Para tocar a una persona (su rol, su estado o su contraseña) hay que tener todo lo que ella tiene.
    private async Task ExigirQuePuedaAdministrarAsync(MiembroTienda miembro, CancellationToken ct)
    {
        if (_usuario is null) return;
        var codigos = await _db.RolPermisos.Where(p => p.RolId == miembro.RolId).Select(p => p.Codigo).ToListAsync(ct);
        if (codigos.Any(c => !_usuario.Tiene(c)))
            throw new SinPermisoException($"{miembro.Usuario!.Nombre} tiene permisos que vos no tenés: no podés administrarla.");
    }

    // Cierra sus sesiones largas (refresh tokens). tiendaId = null: todas las de la cuenta.
    private async Task CerrarSesionesAsync(Guid usuarioId, Guid? tiendaId, CancellationToken ct)
    {
        var ahora = _reloj.GetUtcNow();
        var abiertas = _db.RefreshTokens.Where(r => r.UsuarioId == usuarioId && r.RevocadoEn == null);
        if (tiendaId is { } t) abiertas = abiertas.Where(r => r.TiendaId == t);
        await abiertas.ExecuteUpdateAsync(s => s.SetProperty(r => r.RevocadoEn, ahora), ct);
    }

    private static MiembroDto ADto(MiembroTienda m)
        => new(m.Id, m.UsuarioId, m.Usuario!.Nombre, m.Usuario.Email, false, m.RolId, m.Rol!.Nombre, m.Activo, m.CreadoEn, m.Usuario.UltimoLoginEn);
}
