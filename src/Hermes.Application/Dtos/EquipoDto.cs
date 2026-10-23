namespace Hermes.Application.Dtos;

// ───────────── Roles ─────────────

// Un permiso que se puede dar (el catálogo es el mismo para todas las tiendas). Codigo = "ventas.registrar".
public record PermisoDto(string Codigo, string Modulo, string Descripcion);

// Personas = cuántas personas ACTIVAS tienen hoy este rol.
public record RolDto(Guid Id, string Nombre, string? Descripcion, bool Activo, int Personas, IReadOnlyList<string> Permisos);

public record GuardarRolDto(string Nombre, string? Descripcion, IReadOnlyList<string> Permisos, bool Activo = true);

public record RolesDto(IReadOnlyList<RolDto> Roles, IReadOnlyList<PermisoDto> Permisos);

// ───────────── Equipo ─────────────

// El dueño figura primero (EsDueno = true, RolId = null, no se puede editar). Id: el del miembro; el del dueño es su UsuarioId.
public record MiembroDto(
    Guid Id, Guid UsuarioId, string Nombre, string Email, bool EsDueno, Guid? RolId, string Rol, bool Activo,
    DateTimeOffset CreadoEn, DateTimeOffset? UltimoLoginEn);

// Usuarios = dueño + equipo activo. MaxUsuarios = el límite del plan (null = sin límite).
public record EquipoDto(IReadOnlyList<MiembroDto> Miembros, int Usuarios, int? MaxUsuarios, string Plan);

// La contraseña es provisoria: la persona elige la suya la primera vez que entra.
public record CrearMiembroDto(string Nombre, string Email, string Password, Guid RolId);

public record ActualizarMiembroDto(Guid RolId, bool Activo);

// El dueño le pone una contraseña provisoria nueva a alguien del equipo (se le vuelve a pedir que elija la suya).
public record RestablecerPasswordDto(string Password);

// ───────────── Venta por vendedor ─────────────

// UsuarioId = null: ventas anteriores a los roles (no se sabe quién las registró).
public record VentaVendedorDto(Guid? UsuarioId, string Vendedor, int Ventas, int Unidades, decimal Total);
