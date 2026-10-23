namespace Hermes.Application.Dtos;

// Alta en un solo paso: la persona y su tienda (Slug = hermes.bo/{slug}).
public record RegistroDto(string Nombre, string Email, string Password, string NombreTienda, string Slug);

public record LoginDto(string Email, string Password);

// EsAdmin: administrador de la plataforma (el front muestra su panel; la API lo exige igual con el token).
// DebeCambiarPassword: el dueño le puso una contraseña provisoria; el front pide elegir la propia antes de seguir.
public record UsuarioSesionDto(Guid Id, string Nombre, string Email, bool EsAdmin = false, bool DebeCambiarPassword = false);

public record TiendaSesionDto(Guid Id, string Nombre, string Slug);

// Qué puede hacer esta persona en esta tienda. El dueño tiene TODOS los permisos del catálogo. Rol = "Dueño" o el nombre del rol.
// El front arma el menú con esto; la API igual lo exige en cada pedido (el token lleva los mismos permisos).
public record AccesoDto(bool EsDueno, string Rol, IReadOnlyList<string> Permisos);

// Lo que recibe el front. El refresh token NO viaja acá: va en una cookie HttpOnly.
public record SesionDto(string AccessToken, DateTimeOffset ExpiraEn, UsuarioSesionDto Usuario, TiendaSesionDto Tienda, AccesoDto Acceso);

// GET /yo: quién soy y con qué tienda estoy operando.
public record PerfilDto(UsuarioSesionDto Usuario, TiendaSesionDto Tienda, AccesoDto Acceso);

public record CambiarPasswordDto(string PasswordActual, string PasswordNueva);

// Resultado interno del servicio: el controller pone RefreshToken en la cookie y devuelve Sesion.
public record SesionEmitida(SesionDto Sesion, string RefreshToken, DateTimeOffset RefreshExpiraEn);
