using Hermes.Domain;

namespace Hermes.Application.Dtos;

// ───────────── Proveedores ─────────────

// Telefono: WhatsApp de Bolivia (con o sin +591); se guarda como +591XXXXXXXX.
public record GuardarProveedorDto(string Nombre, string? Telefono = null, string? Notas = null);

// PorPagar: lo que se le debe (compras no anuladas sin pagar). EnCamino: pedidos que todavía no llegaron.
public record ProveedorDto(
    Guid Id, string Nombre, string? Telefono, string? Notas, bool Activo, decimal PorPagar, int EnCamino, int Compras);

// Lo último que le pagamos (o pedimos) a este proveedor por cada producto.
public record UltimoPrecioDto(Guid ProductoId, string Producto, decimal CostoUnitario, DateTimeOffset Fecha);

public record ProveedorDetalleDto(ProveedorDto Proveedor, IReadOnlyList<UltimoPrecioDto> UltimosPrecios);

// ───────────── Compras ─────────────

public record LineaCompraDto(Guid VarianteId, int Cantidad, decimal CostoUnitario);

// ClaveIdempotencia: la genera el celular y se reenvía igual en cada reintento (no duplica la compra).
// `Recibida` / `Pagada`: para anotar una compra que ya llegó o ya se pagó (lo habitual cuando se carga después).
public record RegistrarCompraDto(
    Guid ClaveIdempotencia, Guid ProveedorId, IReadOnlyList<LineaCompraDto> Lineas, string? Notas = null,
    bool Recibida = false, bool Pagada = false);

public enum EstadoCompra { EnCamino, Recibida, Anulada }

public record CompraLineaDto(Guid Id, Guid ProductoId, Guid VarianteId, string Descripcion, int Cantidad, decimal CostoUnitario, decimal Subtotal);

public record CompraDto(
    Guid Id, DateTimeOffset Fecha, Guid ProveedorId, string Proveedor, string? ProveedorTelefono, EstadoCompra Estado, bool Pagada,
    DateTimeOffset? RecibidaEn, DateTimeOffset? PagadaEn, DateTimeOffset? AnuladaEn, string? Notas,
    int Unidades, decimal Total, IReadOnlyList<CompraLineaDto> Lineas);

public record CompraResumenDto(
    Guid Id, DateTimeOffset Fecha, Guid ProveedorId, string Proveedor, EstadoCompra Estado, bool Pagada,
    int Unidades, decimal Total, string Detalle);

public enum VistaCompras { Todas, EnCamino, PorPagar }

public record FiltroComprasDto(VistaCompras Vista = VistaCompras.Todas, Guid? ProveedorId = null, Guid? Cursor = null, int? Limite = null);

public record PaginaDeComprasDto(IReadOnlyList<CompraResumenDto> Items, Guid? Siguiente);

// PorPagar: deuda total con proveedores (compras no anuladas sin pagar). EnCamino: pedidos sin recibir.
public record ResumenComprasDto(decimal PorPagar, int ComprasPorPagar, int EnCamino, decimal ValorEnCamino);

// Un producto que quedó con margen flojo después de recibir (el costo nuevo ya es el del producto).
public record AvisoMargenDto(Guid ProductoId, string Producto, decimal Precio, decimal Costo, EstadoPrecio Estado);

public record ResultadoRecibirDto(CompraDto Compra, IReadOnlyList<AvisoMargenDto> Avisos);

// ───────────── Comparador ─────────────

// El último costo de un proveedor para un producto; el más barato va primero.
public record PrecioProveedorDto(Guid ProveedorId, string Proveedor, string? Telefono, decimal CostoUnitario, DateTimeOffset Fecha, bool EsElMasBarato);
