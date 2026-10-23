using Hermes.Domain;
using Hermes.Domain.Seguridad;

namespace Hermes.Application.Dtos;

// Lo que NO sale de la API según los permisos de quien pregunta. Los servicios devuelven todo; el controller pasa la respuesta
// por acá. Sin costos.ver no viaja ningún costo, margen ni ganancia (el front tampoco los muestra, pero un pedido a mano no
// los trae): los números quedan en 0 / null y "sin costo" como estado.
//
// Si se agrega un campo con costo, margen o ganancia a un DTO que llega a los controllers, agregarlo acá (hay una prueba que
// recorre los DTOs de las respuestas y falla si queda algún campo con esos nombres sin enmascarar).
public static class Enmascarar
{
    // ───────────── Costos ─────────────

    public static ProductoResumenDto Costos(this ProductoResumenDto p, bool ver)
        => ver ? p : p with { Costo = 0, Ganancia = 0, EstadoPrecio = EstadoPrecio.SinCosto };

    public static PaginaDeProductosDto Costos(this PaginaDeProductosDto p, bool ver)
        => ver ? p : p with { Items = p.Items.Select(i => i.Costos(false)).ToList() };

    public static ProductoDto Costos(this ProductoDto p, bool ver)
        => ver ? p : p with { Costo = 0, Ganancia = 0, EstadoPrecio = EstadoPrecio.SinCosto };

    public static ProductoStockDto Costos(this ProductoStockDto p, bool ver) => ver ? p : p with { Costo = 0 };

    public static PaginaDeStockDto Costos(this PaginaDeStockDto p, bool ver)
        => ver ? p : p with { Items = p.Items.Select(i => i.Costos(false)).ToList() };

    public static ResumenStockDto Costos(this ResumenStockDto r, bool ver) => ver ? r : r with { ValorCosto = 0 };

    public static PaginaDeMovimientosDto Costos(this PaginaDeMovimientosDto p, bool ver)
        => ver ? p : p with { Items = p.Items.Select(m => m with { CostoUnitario = null }).ToList() };

    public static VentaDto Costos(this VentaDto v, bool ver)
        => ver ? v : v with
        {
            Ganancia = null,
            Lineas = v.Lineas.Select(l => l with { CostoUnitario = null, Ganancia = null }).ToList(),
        };

    public static PaginaDeVentasDto Costos(this PaginaDeVentasDto p, bool ver)
        => ver ? p : p with { Items = p.Items.Select(v => v with { Ganancia = null }).ToList() };

    public static ResumenVentasDto Costos(this ResumenVentasDto r, bool ver) => ver ? r : r with { Ganancia = 0, UnidadesSinCosto = 0 };

    public static IReadOnlyList<MasVendidoDto> Costos(this IReadOnlyList<MasVendidoDto> l, bool ver)
        => ver ? l : l.Select(m => m with { Ganancia = null }).ToList();

    public static PaginaDeVendiblesDto Costos(this PaginaDeVendiblesDto p, bool ver)
        => ver ? p : p with { Items = p.Items.Select(i => i with { Costo = 0 }).ToList() };

    // ───────────── El Inicio ─────────────

    // Cada bloque del Inicio sale solo si la persona puede ver ese módulo; sin permiso queda en cero.
    public static InicioDto SegunPermisos(this InicioDto i, Func<string, bool> tiene)
    {
        var costos = tiene(Permisos.CostosVer);
        var gastos = tiene(Permisos.GastosVer);
        var compras = tiene(Permisos.ComprasVer);
        var stock = tiene(Permisos.StockVer);
        var pedidos = tiene(Permisos.PedidosVer);
        var instagram = tiene(Permisos.InstagramGestionar);

        return i with
        {
            Ganancia = costos ? i.Ganancia : 0,
            UnidadesSinCosto = costos ? i.UnidadesSinCosto : 0,
            Gastos = gastos ? i.Gastos : 0,
            // El % cubierto y el resultado necesitan la ganancia Y los gastos.
            PorcentajeCubierto = costos && gastos ? i.PorcentajeCubierto : null,
            Resultado = costos && gastos ? i.Resultado : 0,
            PorPagar = compras ? i.PorPagar : 0,
            EnCamino = compras ? i.EnCamino : 0,
            Agotados = stock ? i.Agotados : 0,
            PorAgotarse = stock ? i.PorAgotarse : 0,
            ParaReponer = stock ? i.ParaReponer : [],
            Recientes = i.Recientes.Where(a => a.Tipo switch
            {
                TipoActividad.Gasto => gastos,
                TipoActividad.Compra => compras,
                _ => true,
            }).ToList(),
            PedidosPendientes = pedidos ? i.PedidosPendientes : 0,
            UnidadesDeLaTienda = pedidos ? i.UnidadesDeLaTienda : 0,
            BorradoresListos = instagram ? i.BorradoresListos : 0,
            BorradoresSinPrecio = instagram ? i.BorradoresSinPrecio : 0,
        };
    }
}
