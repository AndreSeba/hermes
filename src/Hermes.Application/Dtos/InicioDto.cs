using Hermes.Domain;

namespace Hermes.Application.Dtos;

// ───────────── Gastos ─────────────

// Fecha: cuándo se pagó (opcional: por defecto ahora; no puede ser futura ni de antes del 2000).
public record RegistrarGastoDto(Guid ClaveIdempotencia, string Concepto, decimal Monto, DateTimeOffset? Fecha = null);

public record GastoDto(Guid Id, DateTimeOffset Fecha, string Concepto, decimal Monto, bool Anulado, DateTimeOffset? AnuladoEn);

public record FiltroGastosDto(DateTimeOffset? Desde = null, DateTimeOffset? Hasta = null, bool IncluirAnulados = false, Guid? Cursor = null, int? Limite = null);

// Total: de los gastos vigentes del PERÍODO pedido (no solo de esta página).
public record PaginaDeGastosDto(IReadOnlyList<GastoDto> Items, Guid? Siguiente, decimal Total);

// ───────────── Inicio ─────────────

public enum TipoActividad { Venta, Compra, Gasto }

// Lo último que pasó en el negocio. Titulo: "2 Cartera · 1 Aretes", "Cueros Don Beto", "Alquiler".
public record ActividadDto(TipoActividad Tipo, Guid Id, DateTimeOffset Fecha, string Titulo, decimal Monto);

public record ProductoParaReponerDto(Guid Id, string Nombre, EstadoStock Estado, int Stock);

// El resumen del período pedido (el front manda el mes de quien mira). Las anuladas no cuentan en nada.
// Ganancia: la bruta de las ventas, solo con las líneas de costo conocido (UnidadesSinCosto avisa lo que falta).
// PorcentajeCubierto: cuánto de los gastos ya cubrió la ganancia (null si no hay gastos); puede pasar de 100.
// Resultado: Ganancia − Gastos. PorPagar / EnCamino: de las compras, no dependen del período.
public record InicioDto(
    int Ventas, int Unidades, decimal TotalVendido, decimal Ganancia, int UnidadesSinCosto,
    decimal Gastos, int? PorcentajeCubierto, decimal Resultado,
    decimal PorPagar, int EnCamino,
    int Agotados, int PorAgotarse, IReadOnlyList<ProductoParaReponerDto> ParaReponer,
    IReadOnlyList<ActividadDto> Recientes,
    int PedidosPendientes = 0); // del carrito de la tienda pública: Nuevos + Confirmados, sin importar el período
