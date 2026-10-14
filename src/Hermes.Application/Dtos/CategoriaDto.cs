namespace Hermes.Application.Dtos;

public record CategoriaDto(Guid Id, string Nombre, bool Activa);

public record CrearCategoriaDto(string Nombre);

// "Eliminar" es Activa = false, nunca un DELETE: puede haber productos que la usan.
public record ActualizarCategoriaDto(string Nombre, bool Activa);
