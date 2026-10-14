using Hermes.Domain;

namespace Hermes.Application.Dtos;

public record GuardarCosteoDto(decimal Material, decimal Empaque, decimal ManoDeObra, decimal CostoFijo, int MargenObjetivo);

// Tiene = false: el producto no tiene costeo (los componentes vienen en 0 y el margen en el de por defecto);
// CostoTotal es entonces el costo directo del producto.
// MargenActual: % real que deja el precio de hoy (null si no hay costo). GananciaConSugerido: por unidad.
public record CosteoDto(
    bool Tiene, decimal Material, decimal Empaque, decimal ManoDeObra, decimal CostoFijo, int MargenObjetivo,
    decimal CostoTotal, decimal PrecioSugerido, decimal GananciaConSugerido,
    decimal PrecioActual, decimal GananciaActual, decimal? MargenActual, EstadoPrecio EstadoActual);
