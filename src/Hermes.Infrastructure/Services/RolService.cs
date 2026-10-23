using Hermes.Application;
using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Domain.Entities;
using Hermes.Domain.Seguridad;
using Hermes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hermes.Infrastructure.Services;

// Rol y RolPermiso son de la tienda (filtro global): nada acá filtra por tienda a mano.
//
// Regla de oro: nadie le puede dar a otro más de lo que tiene. Quien edita un rol solo puede AGREGARLE permisos que él mismo
// tiene (el dueño los tiene todos). Sin eso, alguien con "roles.gestionar" podría fabricarse un rol con todo.
public class RolService : IRolService
{
    // Nombres que se confundirían con el dueño (que no es un rol guardado).
    private static readonly string[] Reservados = ["dueño", "dueno", "propietario", "propietaria", "administrador de la plataforma"];

    private readonly HermesDbContext _db;
    private readonly IUsuarioActual? _usuario;
    private readonly ISesionesRevocadas? _sesiones;

    public RolService(HermesDbContext db, IUsuarioActual? usuario = null, ISesionesRevocadas? sesiones = null)
    {
        _db = db;
        _usuario = usuario;
        _sesiones = sesiones;
    }

    private static readonly IReadOnlyList<PermisoDto> Catalogo =
        Permisos.Catalogo.Select(p => new PermisoDto(p.Codigo, p.Modulo, p.Descripcion)).ToList();

    public async Task<RolesDto> ListarAsync(bool incluirInactivos, CancellationToken ct)
    {
        await AsegurarPorDefectoAsync(ct);

        var consulta = _db.Roles.AsNoTracking().Include(r => r.Permisos).AsQueryable();
        if (!incluirInactivos) consulta = consulta.Where(r => r.Activo);
        var roles = await consulta.OrderByDescending(r => r.Activo).ThenBy(r => r.Nombre).ToListAsync(ct);

        var personas = await _db.Miembros.AsNoTracking().Where(m => m.Activo)
            .GroupBy(m => m.RolId).Select(g => new { RolId = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.RolId, x => x.N, ct);
        return new RolesDto(roles.Select(r => ADto(r, personas.GetValueOrDefault(r.Id))).ToList(), Catalogo);
    }

    public async Task<RolDto> CrearAsync(GuardarRolDto dto, CancellationToken ct)
    {
        var nombre = ValidarNombre(dto.Nombre);
        var descripcion = Validacion.TextoOpcional(dto.Descripcion, 300, "La descripción");
        var codigos = ValidarPermisos(dto.Permisos);
        ExigirQuePuedaDarlos(codigos);
        await ValidarNombreLibreAsync(nombre, null, ct);

        var rol = new Rol { Nombre = nombre, Descripcion = descripcion };
        rol.Permisos = codigos.Select(c => new RolPermiso { RolId = rol.Id, Codigo = c }).ToList();
        _db.Roles.Add(rol);
        await _db.SaveChangesAsync(ct);
        return ADto(rol, 0);
    }

    public async Task<RolDto> ActualizarAsync(Guid id, GuardarRolDto dto, CancellationToken ct)
    {
        var rol = await _db.Roles.Include(r => r.Permisos).FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new RolNoEncontradoException(id);

        var nombre = ValidarNombre(dto.Nombre);
        var descripcion = Validacion.TextoOpcional(dto.Descripcion, 300, "La descripción");
        var codigos = ValidarPermisos(dto.Permisos);
        // Solo lo que se AGREGA tiene que poder darlo quien edita (lo que ya tenía el rol no se le exige).
        ExigirQuePuedaDarlos(codigos.Except(rol.Permisos.Select(p => p.Codigo)));

        var personas = await _db.Miembros.Where(m => m.RolId == id && m.Activo).Select(m => m.UsuarioId).ToListAsync(ct);
        if (!dto.Activo && rol.Activo && personas.Count > 0) throw new RolConPersonasException(rol.Nombre, personas.Count);
        if (dto.Activo) await ValidarNombreLibreAsync(nombre, id, ct);

        rol.Nombre = nombre;
        rol.Descripcion = descripcion;
        rol.Activo = dto.Activo;

        var actuales = rol.Permisos.ToDictionary(p => p.Codigo);
        foreach (var quitado in actuales.Where(p => !codigos.Contains(p.Key)).Select(p => p.Value)) _db.RolPermisos.Remove(quitado);
        foreach (var nuevo in codigos.Where(c => !actuales.ContainsKey(c))) _db.RolPermisos.Add(new RolPermiso { RolId = rol.Id, Codigo = nuevo });
        await _db.SaveChangesAsync(ct);

        // Los permisos de este rol cambiaron: las sesiones abiertas de quienes lo tienen se renuevan con los de ahora.
        foreach (var usuarioId in personas) _sesiones?.Revocar(usuarioId);

        return ADto(rol, personas.Count);
    }

    // La primera vez que alguien abre "Equipo" o "Roles", el negocio arranca con tres roles armados (Encargado, Vendedor y Solo
    // ver). Si después el dueño los desactiva o los cambia, no se vuelven a crear: solo se crean cuando no hay NINGÚN rol.
    private async Task AsegurarPorDefectoAsync(CancellationToken ct)
    {
        if (await _db.Roles.AnyAsync(ct)) return;

        foreach (var d in RolesPorDefecto.Todos)
        {
            var rol = new Rol { Nombre = d.Nombre, Descripcion = d.Descripcion };
            rol.Permisos = d.Permisos.Select(c => new RolPermiso { RolId = rol.Id, Codigo = c }).ToList();
            _db.Roles.Add(rol);
        }
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Otro pedido los creó en el mismo instante (el índice de nombre único lo frenó): lo que quedó es lo bueno.
            _db.ChangeTracker.Clear();
        }
    }

    private static string ValidarNombre(string? texto)
    {
        var nombre = Validacion.Texto(texto, 60, "El nombre del rol");
        if (Reservados.Contains(nombre.ToLowerInvariant()))
            throw new ValidacionException("Ese nombre se confunde con el del dueño. Elegí otro.");
        return nombre;
    }

    private async Task ValidarNombreLibreAsync(string nombre, Guid? excepto, CancellationToken ct)
    {
        var normalizado = nombre.ToLower();
        if (await _db.Roles.AnyAsync(r => r.Activo && r.Nombre.ToLower() == normalizado && r.Id != excepto, ct))
            throw new NombreRolDuplicadoException(nombre);
    }

    private static HashSet<string> ValidarPermisos(IReadOnlyList<string>? codigos)
    {
        var distintos = (codigos ?? []).Select(c => c?.Trim() ?? string.Empty).Where(c => c.Length > 0).ToHashSet(StringComparer.Ordinal);
        if (distintos.Count == 0) throw new ValidacionException("Elegí al menos un permiso para el rol.");
        foreach (var c in distintos)
            if (!Permisos.Existe(c)) throw new PermisoInvalidoException(c);
        return distintos;
    }

    private void ExigirQuePuedaDarlos(IEnumerable<string> codigos)
    {
        if (_usuario is null) return;
        var faltante = codigos.FirstOrDefault(c => !_usuario.Tiene(c));
        if (faltante is not null)
        {
            var detalle = Permisos.Catalogo.FirstOrDefault(p => p.Codigo == faltante)?.Descripcion ?? faltante;
            throw new SinPermisoException($"No podés dar el permiso «{detalle}» porque vos no lo tenés.");
        }
    }

    private static RolDto ADto(Rol r, int personas)
        => new(r.Id, r.Nombre, r.Descripcion, r.Activo, personas, r.Permisos.Select(p => p.Codigo).Order().ToList());
}
