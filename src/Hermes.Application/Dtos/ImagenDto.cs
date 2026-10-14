namespace Hermes.Application.Dtos;

// Urls de los tres tamaños, listas para <img src> / srcset.
public record ImagenDto(Guid Id, int Ancho, int Alto, string Miniatura, string Catalogo, string Detalle);
