using Hermes.Domain;
using Hermes.Domain.Enums;

namespace Hermes.Application.Dtos;

public record VarianteStockDto(Guid Id, string? Color, string? Talla, int Stock);

// Una fila de la pantalla de Stock. Stock es null si el producto no lleva control (por encargo).
public record ProductoStockDto(
    Guid Id, string Nombre, decimal Precio, decimal Costo, ImagenDto? Portada,
    bool ControlaStock, int StockMinimo, int? Stock, EstadoStock Estado,
    IReadOnlyList<VarianteStockDto> Variantes);

public enum VistaStock { Todos, AReponer, PorEncargo }

// Cursor: lo devuelve `siguiente` de la respuesta anterior (es opaco: no armarlo a mano).
public record FiltroStockDto(string? Buscar = null, VistaStock Vista = VistaStock.Todos, string? Cursor = null, int? Limite = null);

// Agotados primero, después "por agotarse", "bien" y por último los de por encargo.
public record PaginaDeStockDto(IReadOnlyList<ProductoStockDto> Items, string? Siguiente);

// La tarjeta oscura de arriba: lo que tenés en mercadería (a costo y a precio de venta) y los avisos.
// Solo cuentan los productos con control de stock que no están archivados.
public record ResumenStockDto(decimal ValorCosto, decimal ValorVenta, int Agotados, int PorAgotarse, int SinControl);

public record MovimientoDto(
    Guid Id, Guid VarianteId, string? Color, string? Talla, TipoMovimiento Tipo, int Cantidad, int Saldo,
    string? Nota, decimal? CostoUnitario, DateTimeOffset Fecha);

public record PaginaDeMovimientosDto(IReadOnlyList<MovimientoDto> Items, Guid? Siguiente);

public record FiltroMovimientosDto(Guid? Cursor = null, int? Limite = null);

public record LineaIngresoDto(Guid VarianteId, int Cantidad);

// "Llegaron más": cantidades por variante (todas del mismo producto). CostoUnitario es opcional: si viene
// y es mayor a 0, ese pasa a ser el costo del producto ("el costo se actualiza cuando llega mercadería").
public record IngresoDto(Guid ProductoId, IReadOnlyList<LineaIngresoDto> Lineas, decimal? CostoUnitario = null, string? Nota = null);

public record LineaAjusteDto(Guid VarianteId, int NuevoTotal);

// "Conté y hay otra cantidad": se dice cuánto HAY (no la diferencia); el servidor calcula la diferencia
// dentro del candado. El motivo es opcional ("Corrección de stock" si no viene).
public record AjusteDto(Guid ProductoId, IReadOnlyList<LineaAjusteDto> Lineas, string? Motivo = null);
