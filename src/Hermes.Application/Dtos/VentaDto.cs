using Hermes.Domain.Enums;

namespace Hermes.Application.Dtos;

// ───────────── Registrar ─────────────

public record LineaVentaDto(Guid VarianteId, int Cantidad);

// ClaveIdempotencia: la genera el celular al abrir la pantalla y se reenvía igual en cada reintento: la misma
// clave devuelve la misma venta (no duplica ni vuelve a descontar stock).
public record RegistrarVentaDto(Guid ClaveIdempotencia, IReadOnlyList<LineaVentaDto> Lineas, MetodoPago MetodoPago, string? Cliente = null);

// ───────────── Lo que se devuelve ─────────────

// CostoUnitario y Ganancia son null si el costo no estaba cargado cuando se vendió.
public record VentaLineaDto(
    Guid Id, Guid ProductoId, Guid VarianteId, string Descripcion, int Cantidad,
    decimal PrecioUnitario, decimal? CostoUnitario, decimal Subtotal, decimal? Ganancia);

// Ganancia es null si alguna línea no tenía costo (no se puede saber cuánto se ganó).
// Vendedor: quién la registró (null en las ventas anteriores a los roles).
public record VentaDto(
    Guid Id, DateTimeOffset Fecha, MetodoPago MetodoPago, string? Cliente, bool Anulada, DateTimeOffset? AnuladaEn,
    int Unidades, decimal Total, decimal? Ganancia, IReadOnlyList<VentaLineaDto> Lineas, string? Vendedor = null);

// Una fila del historial. Detalle: "2 Cartera de cuero Mía · 1 Aretes de plata Killa" (resumido).
public record VentaResumenDto(
    Guid Id, DateTimeOffset Fecha, MetodoPago MetodoPago, string? Cliente, bool Anulada,
    int Unidades, decimal Total, decimal? Ganancia, string Detalle, string? Vendedor = null);

// Desde incluido, Hasta excluido (el front manda el día local: medianoche a medianoche).
public record FiltroVentasDto(DateTimeOffset? Desde = null, DateTimeOffset? Hasta = null, bool IncluirAnuladas = false, Guid? Cursor = null, int? Limite = null);

public record PaginaDeVentasDto(IReadOnlyList<VentaResumenDto> Items, Guid? Siguiente);

public record FiltroPeriodoDto(DateTimeOffset? Desde = null, DateTimeOffset? Hasta = null);

// Los números de un período (las anuladas NO cuentan). Ganancia suma solo las líneas con costo conocido;
// UnidadesSinCosto dice cuántas unidades quedaron afuera por no tener costo cargado.
public record ResumenVentasDto(int Ventas, int Unidades, decimal Total, decimal Ganancia, int UnidadesSinCosto, int Anuladas);

// Para el gráfico del Inicio: Desde = la medianoche, EN LA HORA DE QUIEN MIRA, del primer día; DesfaseMinutos = lo que devuelve
// `new Date().getTimezoneOffset()` del navegador (Bolivia: 240). Con eso el servidor sabe dónde empieza y termina cada día.
public record FiltroPorDiaDto(DateTimeOffset Desde, int Dias = 14, int DesfaseMinutos = 0);

// Un día del gráfico (siempre vienen TODOS los días pedidos, con 0 si no se vendió). Fecha = "2026-10-12", día de quien mira.
public record VentaDiaDto(DateOnly Fecha, decimal Total, int Ventas);

public record FiltroMasVendidosDto(DateTimeOffset? Desde = null, DateTimeOffset? Hasta = null, int? Limite = null);

// Ganancia = null si ninguna de sus unidades tiene costo cargado (no se inventa).
public record MasVendidoDto(Guid ProductoId, string Nombre, int Unidades, decimal? Ganancia);

// ───────────── Para elegir qué vender ─────────────

// Stock es null si el producto no lleva control (por encargo: siempre se puede vender).
public record VarianteVendibleDto(Guid Id, string? Color, string? Talla, int? Stock);

public record ProductoVendibleDto(
    Guid Id, string Nombre, decimal Precio, decimal Costo, ImagenDto? Portada,
    bool ControlaStock, int? Stock, IReadOnlyList<VarianteVendibleDto> Variantes);

public record FiltroVendiblesDto(string? Buscar = null, string? Cursor = null, int? Limite = null);

// Lo que se puede vender primero, los agotados al final. Siguiente: cursor opaco.
public record PaginaDeVendiblesDto(IReadOnlyList<ProductoVendibleDto> Items, string? Siguiente);
