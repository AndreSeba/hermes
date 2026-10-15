namespace Hermes.Application.Dtos;

// Alta en un solo paso: la persona y su tienda (Slug = hermes.bo/{slug}).
public record RegistroDto(string Nombre, string Email, string Password, string NombreTienda, string Slug);

public record LoginDto(string Email, string Password);

// EsAdmin: administrador de la plataforma (el front muestra su panel; la API lo exige igual con el token).
public record UsuarioSesionDto(Guid Id, string Nombre, string Email, bool EsAdmin = false);

public record TiendaSesionDto(Guid Id, string Nombre, string Slug);

// Lo que recibe el front. El refresh token NO viaja acá: va en una cookie HttpOnly.
public record SesionDto(string AccessToken, DateTimeOffset ExpiraEn, UsuarioSesionDto Usuario, TiendaSesionDto Tienda);

// GET /yo: quién soy y con qué tienda estoy operando.
public record PerfilDto(UsuarioSesionDto Usuario, TiendaSesionDto Tienda);

// Resultado interno del servicio: el controller pone RefreshToken en la cookie y devuelve Sesion.
public record SesionEmitida(SesionDto Sesion, string RefreshToken, DateTimeOffset RefreshExpiraEn);
