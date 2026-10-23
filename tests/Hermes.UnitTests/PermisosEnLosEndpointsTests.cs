using System.Reflection;
using System.Text.RegularExpressions;
using Hermes.Api.Controllers;
using Hermes.Api.Security;
using Hermes.Application.Dtos;
using Hermes.Domain;
using Hermes.Domain.Seguridad;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Hermes.UnitTests;

// Lo que impide que una pantalla nueva quede abierta para cualquiera con sesión: recorre TODOS los controllers.
public class PermisosEnLosEndpointsTests
{
    // Rutas que no piden permiso a propósito: o son públicas, o alcanza con tener sesión (es de la persona misma o no es sensible).
    private static readonly HashSet<string> Publicas =
    [
        "AuthController.Registrar", "AuthController.Login", "AuthController.Renovar", "AuthController.CerrarSesion",
        "ArchivosController.*", "PublicoController.*",
    ];

    private static readonly HashSet<string> SoloConSesion =
    [
        "AuthController.Yo", "AuthController.CambiarPassword", // la persona misma
        "TiendaController.Obtener",                            // datos básicos de SU tienda (el menú los muestra a todos)
        "PlanesController.Listar", "PlanesController.MiPlan",  // para avisar límites; pedir un plan sí exige permiso
    ];

    private static IEnumerable<(Type Controller, MethodInfo Accion)> Acciones()
    {
        var controllers = typeof(AuthController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);
        foreach (var c in controllers)
            foreach (var m in c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                if (m.GetCustomAttributes<HttpMethodAttribute>().Any())
                    yield return (c, m);
    }

    private static bool EnLista(HashSet<string> lista, Type c, MethodInfo m)
        => lista.Contains($"{c.Name}.{m.Name}") || lista.Contains($"{c.Name}.*");

    private static IEnumerable<string> PoliticasDe(Type c, MethodInfo m)
        => c.GetCustomAttributes<AuthorizeAttribute>().Concat(m.GetCustomAttributes<AuthorizeAttribute>())
            .Select(a => a.Policy).Where(p => !string.IsNullOrEmpty(p))!;

    [Fact]
    public void Toda_accion_de_la_API_exige_un_permiso_o_esta_en_la_lista_de_las_que_no()
    {
        var sinProteger = new List<string>();
        foreach (var (c, m) in Acciones())
        {
            var etiqueta = $"{c.Name}.{m.Name}";
            var conAuthorize = c.GetCustomAttributes<AuthorizeAttribute>().Concat(m.GetCustomAttributes<AuthorizeAttribute>()).Any();
            var anonima = m.GetCustomAttribute<AllowAnonymousAttribute>() is not null || c.GetCustomAttribute<AllowAnonymousAttribute>() is not null;

            if (EnLista(Publicas, c, m)) continue;
            if (anonima || !conAuthorize) { sinProteger.Add($"{etiqueta}: abierta a cualquiera (¿falta [Authorize]?)"); continue; }
            if (!PoliticasDe(c, m).Any() && !EnLista(SoloConSesion, c, m))
                sinProteger.Add($"{etiqueta}: pide sesión pero ningún permiso (usar [Authorize(Policy = Permisos.…)])");
        }
        Assert.True(sinProteger.Count == 0, "Acciones sin permiso:\n" + string.Join('\n', sinProteger));
    }

    [Fact]
    public void Las_acciones_de_las_listas_existen_de_verdad()
    {
        var existentes = Acciones().Select(a => $"{a.Controller.Name}.{a.Accion.Name}").ToHashSet();
        var controllers = Acciones().Select(a => a.Controller.Name).ToHashSet();
        foreach (var e in Publicas.Concat(SoloConSesion))
            Assert.True(e.EndsWith(".*") ? controllers.Contains(e[..^2]) : existentes.Contains(e), $"La lista nombra «{e}» y no existe.");
    }

    [Fact]
    public void Todas_las_policies_son_permisos_del_catalogo_y_todos_los_permisos_se_usan()
    {
        var usados = new HashSet<string>();
        foreach (var (c, m) in Acciones())
            foreach (var policy in PoliticasDe(c, m))
                foreach (var codigo in policy.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (codigo == Politicas.Admin) continue;
                    Assert.True(Permisos.Existe(codigo), $"{c.Name}.{m.Name} pide «{codigo}», que no está en el catálogo (¿error de tipeo?).");
                    usados.Add(codigo);
                }

        var sinUso = Permisos.Todos.Where(p => !usados.Contains(p)).ToList();
        Assert.True(sinUso.Count == 0, "Permisos que ningún endpoint exige (un permiso que no protege nada engaña a quien lo da): " + string.Join(", ", sinUso));
    }

    // ───────────── Costos ─────────────

    // Todo DTO con campos de costo, margen o ganancia tiene que figurar acá: o lo enmascara `Enmascarar`, o es algo que solo
    // llega a quien ya tiene el permiso del módulo, o es lo que se ENVÍA (no se devuelve). Un DTO nuevo con costos que no
    // esté en la lista hace fallar esta prueba: hay que decidir cómo se oculta antes de publicarlo.
    private static readonly HashSet<string> ConCostosYContemplados =
    [
        // Los enmascara Enmascarar (sin costos.ver sale en 0 / null).
        nameof(ProductoResumenDto), nameof(ProductoDto), nameof(ProductoStockDto), nameof(ResumenStockDto), nameof(MovimientoDto),
        nameof(VentaLineaDto), nameof(VentaDto), nameof(VentaResumenDto), nameof(ResumenVentasDto), nameof(MasVendidoDto),
        nameof(ProductoVendibleDto), nameof(InicioDto),
        // Solo con el permiso del módulo (costos.ver para el costeo; compras.ver / compras.gestionar para las compras).
        nameof(CosteoDto), nameof(CompraLineaDto), nameof(UltimoPrecioDto), nameof(AvisoMargenDto), nameof(PrecioProveedorDto),
        // Lo que se envía, no lo que se devuelve.
        nameof(GuardarProductoDto), nameof(IngresoDto), nameof(GuardarCosteoDto), nameof(LineaCompraDto),
    ];

    [Fact]
    public void Todo_DTO_con_costo_margen_o_ganancia_tiene_su_regla_de_ocultamiento()
    {
        var campo = new Regex("costo|ganancia|margen", RegexOptions.IgnoreCase);
        var encontrados = typeof(ProductoDto).Assembly.GetTypes()
            .Where(t => t.Namespace == "Hermes.Application.Dtos" && t.IsPublic)
            .Where(t => t.GetProperties().Any(p => campo.IsMatch(p.Name)))
            .Select(t => t.Name).ToHashSet();

        var nuevos = encontrados.Except(ConCostosYContemplados).Order().ToList();
        Assert.True(nuevos.Count == 0, "DTOs con costos sin regla de ocultamiento: " + string.Join(", ", nuevos));
        var sobran = ConCostosYContemplados.Except(encontrados).Order().ToList();
        Assert.True(sobran.Count == 0, "La lista nombra DTOs que ya no tienen campos de costo: " + string.Join(", ", sobran));
    }

    [Fact]
    public void Sin_ver_costos_no_sale_ningun_costo_margen_ni_ganancia()
    {
        var resumen = new ProductoResumenDto(Guid.NewGuid(), "Taza", 15, 8.75m, 6.25m, EstadoPrecio.Ok, null, null, null, true, false, 1, 5, EstadoStock.Ok);
        var producto = new ProductoDto(Guid.NewGuid(), "Taza", null, 15, 8.75m, 6.25m, EstadoPrecio.Ok, null, true, false, [], [], true, 3);
        var linea = new VentaLineaDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Taza", 2, 15, 8.75m, 30, 12.5m);
        var venta = new VentaDto(Guid.NewGuid(), DateTimeOffset.UtcNow, Hermes.Domain.Enums.MetodoPago.Efectivo, null, false, null, 2, 30, 12.5m, [linea]);

        var p = new PaginaDeProductosDto([resumen], null).Costos(false).Items.Single();
        Assert.Equal((0m, 0m, EstadoPrecio.SinCosto, 15m), (p.Costo, p.Ganancia, p.EstadoPrecio, p.Precio)); // el precio SÍ se ve

        var d = producto.Costos(false);
        Assert.Equal((0m, 0m, EstadoPrecio.SinCosto), (d.Costo, d.Ganancia, d.EstadoPrecio));

        var v = venta.Costos(false);
        Assert.Null(v.Ganancia);
        Assert.Null(v.Lineas.Single().CostoUnitario);
        Assert.Null(v.Lineas.Single().Ganancia);
        Assert.Equal(30m, v.Total);

        Assert.Equal(0m, new ResumenVentasDto(1, 2, 30, 12.5m, 3, 0).Costos(false).Ganancia);
        IReadOnlyList<MasVendidoDto> masVendidos = [new(Guid.NewGuid(), "Taza", 2, 12.5m)];
        Assert.Null(masVendidos.Costos(false).Single().Ganancia);
        Assert.Equal(0m, new ResumenStockDto(100, 200, 0, 0, 0).Costos(false).ValorCosto);

        // Con permiso, todo igual.
        Assert.Same(venta, venta.Costos(true));
        Assert.Equal(6.25m, producto.Costos(true).Ganancia);
    }

    [Fact]
    public void El_Inicio_solo_muestra_los_bloques_de_los_modulos_que_la_persona_puede_ver()
    {
        var inicio = new InicioDto(
            10, 20, 3000, 1200, 2, 800, 150, 400, 500, 3, 4, 2, [new(Guid.NewGuid(), "Taza", EstadoStock.Agotado, 0)],
            [new(TipoActividad.Venta, Guid.NewGuid(), DateTimeOffset.UtcNow, "v", 30), new(TipoActividad.Gasto, Guid.NewGuid(), DateTimeOffset.UtcNow, "g", 10),
             new(TipoActividad.Compra, Guid.NewGuid(), DateTimeOffset.UtcNow, "c", 20)],
            PedidosPendientes: 5, BorradoresListos: 1, BorradoresSinPrecio: 2, UnidadesDeLaTienda: 7);

        var soloVentas = inicio.SegunPermisos(c => c == Permisos.InicioVer);
        Assert.Equal((10, 3000m), (soloVentas.Ventas, soloVentas.TotalVendido)); // lo de ventas se ve
        Assert.Equal((0m, 0m, 0m, 0m), (soloVentas.Ganancia, soloVentas.Gastos, soloVentas.Resultado, soloVentas.PorPagar));
        Assert.Null(soloVentas.PorcentajeCubierto);
        Assert.Empty(soloVentas.ParaReponer);
        Assert.Equal([TipoActividad.Venta], soloVentas.Recientes.Select(a => a.Tipo));
        Assert.Equal((0, 0, 0), (soloVentas.PedidosPendientes, soloVentas.BorradoresListos, soloVentas.UnidadesDeLaTienda));

        var todo = inicio.SegunPermisos(_ => true); // con todo, igual (los registros comparan sus listas por referencia: se miran los campos)
        Assert.Equal((1200m, 800m, 400m, 500m, 150), (todo.Ganancia, todo.Gastos, todo.Resultado, todo.PorPagar, todo.PorcentajeCubierto));
        Assert.Equal(3, todo.Recientes.Count);
        Assert.Single(todo.ParaReponer);
        Assert.Equal((5, 1, 2, 7), (todo.PedidosPendientes, todo.BorradoresListos, todo.BorradoresSinPrecio, todo.UnidadesDeLaTienda));
        // La ganancia sola no alcanza para "ya cubriste el %": hace falta ver también los gastos.
        Assert.Null(inicio.SegunPermisos(c => c is Permisos.InicioVer or Permisos.CostosVer).PorcentajeCubierto);
    }
}
