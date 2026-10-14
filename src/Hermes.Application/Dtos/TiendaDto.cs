namespace Hermes.Application.Dtos;

// Whatsapp sale normalizado ("+59171234567"); Logo es null si no subió ninguno.
public record TiendaDto(Guid Id, string Nombre, string Slug, string? Descripcion, string? Whatsapp, string Color, ImagenDto? Logo);

// LogoImagenId: el Id que devolvió POST /imagenes. null = sin logo. Al cambiarlo, el logo anterior
// se borra (sus archivos incluidos).
public record ActualizarTiendaDto(string Nombre, string? Descripcion, string? Whatsapp, string Color, Guid? LogoImagenId);

// Cambiar la dirección rompe los enlaces que ya compartió: el front pide confirmación antes.
public record CambiarDireccionDto(string Slug);
