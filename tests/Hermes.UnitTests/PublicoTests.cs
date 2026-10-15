using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Domain.Entities;
using Hermes.Domain.Enums;
using Hermes.Infrastructure;
using Hermes.Infrastructure.Imagenes;
using Hermes.Infrastructure.Persistence;
using Hermes.Infrastructure.Services;
using Hermes.Infrastructure.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hermes.UnitTests;

public class PublicoTests : IDisposable
{
    private readonly BaseDePrueba _bd = new();
    private readonly FakeTimeProvider _reloj = new(new DateTimeOffset(2026, 10, 14, 15, 0, 0, TimeSpan.Zero));

    public void Dispose() => _bd.Dispose();

    // El lado de la dueña: crea productos y stock en su tienda.
    private record Duena(HermesDbContext Db, ProductoService Productos, StockService Stock, CategoriaService Categorias, TiendaService Tienda);

    private Duena ComoDuena(Guid tienda)
    {
        var db = _bd.Contexto(tienda);
        var almacen = new AlmacenEnBaseDeDatos(db, _reloj);
        var sesion = new BaseDePrueba.TiendaFija(tienda);
        var imagenes = new ImagenService(db, almacen, sesion, _reloj);
        return new Duena(db, new ProductoService(db, imagenes, almacen, sesion, _reloj),
            new StockService(db, new OperacionesStock(db, _reloj), almacen, sesion), new CategoriaService(db),
            new TiendaService(db, imagenes, almacen, sesion));
    }

    // El lado del público: sin sesión; la tienda la fija el servicio con el slug.
    private PublicoService ComoPublico()
    {
        var contexto = new TiendaPublicaContexto();
        var db = _bd.ContextoPublico(contexto);
        return new PublicoService(db, new AlmacenEnBaseDeDatos(db, _reloj), contexto);
    }

    private void Configurar(Guid tienda, Action<Tienda> cambios)
    {
        using var db = _bd.Contexto(null);
        var t = db.Tiendas.Single(x => x.Id == tienda);
        cambios(t);
        db.SaveChanges();
    }

    private static Task<ProductoDto> Crear(Duena d, string nombre, decimal precio = 180, decimal costo = 110, string[]? colores = null,
        bool visible = true, bool controla = true, Guid? categoria = null)
        => d.Productos.CrearAsync(new GuardarProductoDto(nombre, "Hecha a mano", precio, costo, categoria, visible, colores, null, null, controla), default);

    private static async Task<ProductoDto> ConStock(Duena d, string nombre, int unidades, string[]? colores = null, decimal costo = 110)
    {
        var p = await Crear(d, nombre, colores: colores, costo: costo);
        await d.Stock.IngresarAsync(new(p.Id, p.Variantes.Select(v => new LineaIngresoDto(v.Id, unidades)).ToList()), default);
        return p;
    }

    // ───────────── La tienda ─────────────

    [Fact]
    public async Task La_tienda_muestra_su_identidad_sin_nada_del_dueno()
    {
        Configurar(_bd.TiendaA, t => { t.Descripcion = "Accesorios hechos a mano"; t.Whatsapp = "+59171234567"; t.Color = "#C2410C"; t.Fondo = FondoTienda.Oscuro; });

        var t = await ComoPublico().ObtenerTiendaAsync("tienda-a", default);

        Assert.Equal(("Tienda A", "tienda-a", "Accesorios hechos a mano", "#C2410C", FondoTienda.Oscuro), (t.Nombre, t.Slug, t.Descripcion, t.Color, t.Fondo));
        Assert.Equal("+59171234567", t.Whatsapp);
        Assert.True(t.MostrarPrecios && t.MostrarDisponibilidad);
    }

    [Fact]
    public async Task El_slug_no_distingue_mayusculas_y_una_tienda_inexistente_da_404()
    {
        var publico = ComoPublico();

        Assert.Equal("Tienda A", (await publico.ObtenerTiendaAsync("  TIENDA-A ", default)).Nombre);
        await Assert.ThrowsAsync<TiendaPublicaNoEncontradaException>(() => ComoPublico().ObtenerTiendaAsync("no-existe", default));
        await Assert.ThrowsAsync<TiendaPublicaNoEncontradaException>(() => ComoPublico().ObtenerTiendaAsync("", default));
        await Assert.ThrowsAsync<TiendaPublicaNoEncontradaException>(() => ComoPublico().ProductosAsync("no-existe", new(), default));
        await Assert.ThrowsAsync<TiendaPublicaNoEncontradaException>(() => ComoPublico().ProductoAsync("no-existe", Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Una_tienda_apagada_no_se_muestra_en_ninguna_ruta()
    {
        var d = ComoDuena(_bd.TiendaA);
        var p = await Crear(d, "Cartera");
        Configurar(_bd.TiendaA, t => t.Activa = false);

        await Assert.ThrowsAsync<TiendaPublicaNoEncontradaException>(() => ComoPublico().ObtenerTiendaAsync("tienda-a", default));
        await Assert.ThrowsAsync<TiendaPublicaNoEncontradaException>(() => ComoPublico().ProductosAsync("tienda-a", new(), default));
        await Assert.ThrowsAsync<TiendaPublicaNoEncontradaException>(() => ComoPublico().ProductoAsync("tienda-a", p.Id, default));
        Configurar(_bd.TiendaA, t => t.Activa = true);
        Assert.Single((await ComoPublico().ProductosAsync("tienda-a", new(), default)).Items); // al encenderla vuelve a verse
    }

    [Fact]
    public async Task El_whatsapp_solo_sale_si_los_pedidos_por_whatsapp_estan_encendidos()
    {
        Configurar(_bd.TiendaA, t => t.Whatsapp = "+59171234567");
        Assert.Equal("+59171234567", (await ComoPublico().ObtenerTiendaAsync("tienda-a", default)).Whatsapp);

        Configurar(_bd.TiendaA, t => t.PedidosPorWhatsapp = false);
        Assert.Null((await ComoPublico().ObtenerTiendaAsync("tienda-a", default)).Whatsapp);
    }

    [Fact]
    public async Task Las_categorias_son_solo_las_que_tienen_productos_para_mostrar()
    {
        var d = ComoDuena(_bd.TiendaA);
        var carteras = await d.Categorias.CrearAsync(new CrearCategoriaDto("Carteras"), default);
        var vacia = await d.Categorias.CrearAsync(new CrearCategoriaDto("Sin nada"), default);
        var oculta = await d.Categorias.CrearAsync(new CrearCategoriaDto("Solo oculta"), default);
        await Crear(d, "Cartera", categoria: carteras.Id);
        await Crear(d, "Escondida", categoria: oculta.Id, visible: false);

        var t = await ComoPublico().ObtenerTiendaAsync("tienda-a", default);

        Assert.Equal(["Carteras"], t.Categorias.Select(c => c.Nombre));
        Assert.DoesNotContain(t.Categorias, c => c.Id == vacia.Id || c.Id == oculta.Id);
    }

    // ───────────── Productos ─────────────

    [Fact]
    public async Task Solo_se_muestran_los_productos_visibles_y_no_archivados_de_esa_tienda()
    {
        var a = ComoDuena(_bd.TiendaA);
        var b = ComoDuena(_bd.TiendaB);
        await Crear(a, "Visible");
        await Crear(a, "Oculta", visible: false);
        var archivado = await Crear(a, "Archivado");
        await a.Productos.ArchivarAsync(archivado.Id, true, default);
        await Crear(b, "De la otra tienda");

        var items = (await ComoPublico().ProductosAsync("tienda-a", new(), default)).Items;

        Assert.Equal(["Visible"], items.Select(i => i.Nombre));
    }

    [Fact]
    public async Task Un_producto_de_otra_tienda_oculto_o_archivado_no_se_pide_por_id()
    {
        var a = ComoDuena(_bd.TiendaA);
        var b = ComoDuena(_bd.TiendaB);
        var deB = await Crear(b, "De B");
        var oculta = await Crear(a, "Oculta", visible: false);
        var archivado = await Crear(a, "Archivado");
        await a.Productos.ArchivarAsync(archivado.Id, true, default);

        await Assert.ThrowsAsync<TiendaPublicaNoEncontradaException>(() => ComoPublico().ProductoAsync("tienda-a", deB.Id, default));
        await Assert.ThrowsAsync<TiendaPublicaNoEncontradaException>(() => ComoPublico().ProductoAsync("tienda-a", oculta.Id, default));
        await Assert.ThrowsAsync<TiendaPublicaNoEncontradaException>(() => ComoPublico().ProductoAsync("tienda-a", archivado.Id, default));
        await Assert.ThrowsAsync<TiendaPublicaNoEncontradaException>(() => ComoPublico().ProductoAsync("tienda-a", Guid.NewGuid(), default));
        Assert.Equal("De B", (await ComoPublico().ProductoAsync("tienda-b", deB.Id, default)).Nombre); // por la suya sí
    }

    [Fact]
    public async Task El_detalle_trae_variantes_activas_con_su_disponibilidad_sin_cantidades()
    {
        var d = ComoDuena(_bd.TiendaA);
        var p = await Crear(d, "Cartera", colores: ["café", "negro", "rojo"]);
        await d.Stock.IngresarAsync(new(p.Id, [new(p.Variantes[0].Id, 4), new(p.Variantes[1].Id, 1)]), default); // café 4, negro 1, rojo 0
        await d.Stock.AjustarAsync(new(p.Id, [new(p.Variantes[2].Id, 0)]), default).ContinueWith(_ => { }); // (rojo ya está en 0)
        await d.Productos.ActualizarAsync(p.Id, new GuardarProductoDto("Cartera", "Hecha a mano", 180, 110, null, true, ["café", "negro"], null, null), default); // rojo ya no se vende

        var r = await ComoPublico().ProductoAsync("tienda-a", p.Id, default);

        Assert.Equal(["café", "negro"], r.Variantes.Select(v => v.Color));
        Assert.Equal([true, true], r.Variantes.Select(v => v.Disponible));
        Assert.True(r.Disponible);
        Assert.Equal((180m, "Hecha a mano"), (r.Precio, r.Descripcion));
    }

    [Fact]
    public async Task La_disponibilidad_es_si_hay_o_no_hay_y_lo_por_encargo_siempre_esta_disponible()
    {
        var d = ComoDuena(_bd.TiendaA);
        await ConStock(d, "Con stock", 3);
        await Crear(d, "Agotado");
        await Crear(d, "Por encargo", controla: false);

        var items = (await ComoPublico().ProductosAsync("tienda-a", new(Limite: 100), default)).Items.ToDictionary(i => i.Nombre);

        Assert.True(items["Con stock"].Disponible);
        Assert.False(items["Agotado"].Disponible);
        Assert.True(items["Por encargo"].Disponible);
    }

    [Fact]
    public async Task Un_producto_con_una_variante_agotada_y_otra_no_sigue_disponible_y_el_detalle_dice_cual()
    {
        var d = ComoDuena(_bd.TiendaA);
        var p = await Crear(d, "Collar", colores: ["azul", "verde"]);
        await d.Stock.IngresarAsync(new(p.Id, [new(p.Variantes[1].Id, 2)]), default);

        var r = await ComoPublico().ProductoAsync("tienda-a", p.Id, default);

        Assert.True(r.Disponible);
        Assert.Equal([false, true], r.Variantes.Select(v => v.Disponible));
    }

    // ───────────── Lo que se puede ocultar ─────────────

    [Fact]
    public async Task Sin_mostrar_precios_el_precio_no_sale_en_ninguna_ruta()
    {
        var d = ComoDuena(_bd.TiendaA);
        var p = await Crear(d, "Cartera", precio: 180);
        Configurar(_bd.TiendaA, t => t.MostrarPrecios = false);

        var lista = await ComoPublico().ProductosAsync("tienda-a", new(), default);
        var detalle = await ComoPublico().ProductoAsync("tienda-a", p.Id, default);

        Assert.Null(lista.Items[0].Precio);
        Assert.Null(detalle.Precio);
        Assert.False((await ComoPublico().ObtenerTiendaAsync("tienda-a", default)).MostrarPrecios);
    }

    [Fact]
    public async Task Sin_mostrar_disponibilidad_no_sale_ni_en_el_dato_ni_en_el_orden()
    {
        var d = ComoDuena(_bd.TiendaA);
        await Crear(d, "Agotado 1");
        await ConStock(d, "Con stock", 5);
        await Crear(d, "Agotado 2");
        Configurar(_bd.TiendaA, t => t.MostrarDisponibilidad = false);

        var lista = (await ComoPublico().ProductosAsync("tienda-a", new(), default)).Items;
        var detalle = await ComoPublico().ProductoAsync("tienda-a", lista[0].Id, default);

        Assert.All(lista, i => Assert.Null(i.Disponible));
        Assert.Null(detalle.Disponible);
        Assert.All(detalle.Variantes, v => Assert.Null(v.Disponible));
        // Todos van por fecha de carga (el más nuevo primero): ordenar por agotado revelaría lo que se ocultó.
        Assert.Equal(["Agotado 2", "Con stock", "Agotado 1"], lista.Select(i => i.Nombre));
    }

    // ───────────── Orden, cursor y filtros ─────────────

    [Fact]
    public async Task Lo_disponible_va_primero_y_los_agotados_al_final_y_el_cursor_no_repite()
    {
        var d = ComoDuena(_bd.TiendaA);
        for (var i = 1; i <= 4; i++) await Crear(d, $"Agotado {i}");
        for (var i = 1; i <= 3; i++) await ConStock(d, $"Con stock {i}", 2);

        var vistos = new List<ProductoPublicoResumenDto>();
        string? cursor = null;
        do
        {
            var pagina = await ComoPublico().ProductosAsync("tienda-a", new(Limite: 3, Cursor: cursor), default);
            vistos.AddRange(pagina.Items);
            cursor = pagina.Siguiente;
        } while (cursor is not null);

        Assert.Equal(7, vistos.Select(v => v.Id).Distinct().Count());
        Assert.All(vistos.Take(3), v => Assert.True(v.Disponible));
        Assert.All(vistos.Skip(3), v => Assert.False(v.Disponible));
        await Assert.ThrowsAsync<ValidacionException>(() => ComoPublico().ProductosAsync("tienda-a", new(Cursor: "9.xyz"), default));
    }

    [Fact]
    public async Task Se_busca_por_nombre_y_se_filtra_por_categoria()
    {
        var d = ComoDuena(_bd.TiendaA);
        var joyas = await d.Categorias.CrearAsync(new CrearCategoriaDto("Joyas"), default);
        await Crear(d, "Aretes de plata", categoria: joyas.Id);
        await Crear(d, "Collar de plata", categoria: joyas.Id);
        await Crear(d, "Cartera de cuero");

        Assert.Equal(["Collar de plata", "Aretes de plata"], (await ComoPublico().ProductosAsync("tienda-a", new(Buscar: " PLATA "), default)).Items.Select(i => i.Nombre));
        Assert.Equal(2, (await ComoPublico().ProductosAsync("tienda-a", new(CategoriaId: joyas.Id), default)).Items.Count);
        Assert.Equal("Joyas", (await ComoPublico().ProductosAsync("tienda-a", new(CategoriaId: joyas.Id), default)).Items[0].Categoria);
        Assert.Empty((await ComoPublico().ProductosAsync("tienda-a", new(Buscar: "inexistente"), default)).Items);
    }

    // ───────────── Lo que NUNCA sale ─────────────

    private static readonly Regex Prohibido = new("costo|margen|ganancia|stock|cantidad|movimiento|proveedor|compra", RegexOptions.IgnoreCase);

    [Fact]
    public void Ningun_dto_publico_tiene_siquiera_un_campo_de_costo_margen_o_stock()
    {
        // Los DTOs públicos son los de PublicoDto.cs. Se revisan por reflexión, recursivamente: si alguien agrega un
        // campo así, esta prueba falla antes de que llegue a una respuesta.
        // Lista EXPLÍCITA (no por nombre): un DTO nuevo que se muestre al público tiene que sumarse acá a propósito.
        var tipos = new List<Type>
        {
            typeof(TiendaPublicaDto), typeof(CategoriaPublicaDto), typeof(ProductoPublicoResumenDto), typeof(VariantePublicaDto),
            typeof(ProductoPublicoDto), typeof(PaginaPublicaDto), typeof(FiltroPublicoDto),
            typeof(RevisarCarritoDto), typeof(ArticuloCarritoDto), typeof(CarritoPublicoDto), typeof(ArticuloPedidoDto),
            typeof(NuevoPedidoDto), typeof(LineaPedidoPublicaDto), typeof(PedidoEnviadoDto),
        };

        var vistos = new HashSet<Type>();
        void Revisar(Type tipo)
        {
            if (!vistos.Add(tipo)) return;
            foreach (var p in tipo.GetProperties())
            {
                Assert.False(Prohibido.IsMatch(p.Name), $"{tipo.Name}.{p.Name} no puede estar en un DTO público.");
                var interno = p.PropertyType.IsGenericType ? p.PropertyType.GetGenericArguments()[0] : p.PropertyType;
                if (interno.Namespace == "Hermes.Application.Dtos") Revisar(interno);
            }
        }
        tipos.ForEach(Revisar);
    }

    [Fact]
    public async Task Lo_que_realmente_se_serializa_no_lleva_costo_ni_cantidades_ni_el_costo_como_valor()
    {
        var d = ComoDuena(_bd.TiendaA);
        var p = await ConStock(d, "Cartera", 7, colores: ["café", "negro"], costo: 4321.5m); // un costo y un stock que se reconocerían
        Configurar(_bd.TiendaA, t => t.Whatsapp = "+59171234567");

        var opciones = new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        var json = string.Join("\n",
            JsonSerializer.Serialize(await ComoPublico().ObtenerTiendaAsync("tienda-a", default), opciones),
            JsonSerializer.Serialize(await ComoPublico().ProductosAsync("tienda-a", new(), default), opciones),
            JsonSerializer.Serialize(await ComoPublico().ProductoAsync("tienda-a", p.Id, default), opciones));

        Assert.DoesNotMatch(Prohibido, json);
        Assert.DoesNotContain("4321", json); // el costo, ni siquiera como valor suelto
        Assert.DoesNotContain("\"Disponible\":7", json);
    }

    // ───────────── Aislamiento y lo que no se mezcla ─────────────

    [Fact]
    public async Task Pedir_una_tienda_despues_de_otra_en_el_mismo_pedido_no_mezcla_nada()
    {
        var a = ComoDuena(_bd.TiendaA);
        var b = ComoDuena(_bd.TiendaB);
        await Crear(a, "De A");
        await Crear(b, "De B");

        // Un solo servicio (un solo contexto de pedido) usado con dos slugs seguidos: el último manda y no queda nada del anterior.
        var publico = ComoPublico();
        var deA = (await publico.ProductosAsync("tienda-a", new(), default)).Items.Select(i => i.Nombre);
        var deB = (await publico.ProductosAsync("tienda-b", new(), default)).Items.Select(i => i.Nombre);

        Assert.Equal(["De A"], deA);
        Assert.Equal(["De B"], deB);
    }

    // ───────────── Configuración desde el panel ─────────────

    [Fact]
    public async Task La_configuracion_publica_arranca_toda_encendida_con_fondo_crema_y_se_guarda()
    {
        var d = ComoDuena(_bd.TiendaA);

        var inicial = await d.Tienda.ObtenerConfiguracionPublicaAsync(default);
        Assert.Equal(new ConfiguracionPublicaDto(true, true, true, true, FondoTienda.Crema), inicial);

        var nueva = new ConfiguracionPublicaDto(true, false, false, true, FondoTienda.Oscuro);
        Assert.Equal(nueva, await d.Tienda.GuardarConfiguracionPublicaAsync(nueva, default));
        Assert.Equal(nueva, await ComoDuena(_bd.TiendaA).Tienda.ObtenerConfiguracionPublicaAsync(default));
        Assert.Equal(FondoTienda.Oscuro, (await ComoPublico().ObtenerTiendaAsync("tienda-a", default)).Fondo);
    }

    [Fact]
    public async Task Un_fondo_inventado_se_rechaza_y_la_configuracion_de_una_tienda_no_toca_la_de_otra()
    {
        var a = ComoDuena(_bd.TiendaA);
        var b = ComoDuena(_bd.TiendaB);

        await Assert.ThrowsAsync<ValidacionException>(() => a.Tienda.GuardarConfiguracionPublicaAsync(new(true, true, true, true, (FondoTienda)99), default));
        await a.Tienda.GuardarConfiguracionPublicaAsync(new(false, false, false, false, FondoTienda.Claro), default);

        Assert.Equal(new ConfiguracionPublicaDto(true, true, true, true, FondoTienda.Crema), await b.Tienda.ObtenerConfiguracionPublicaAsync(default));
        await Assert.ThrowsAsync<TiendaPublicaNoEncontradaException>(() => ComoPublico().ObtenerTiendaAsync("tienda-a", default)); // "Publicada = false" la apagó
    }

    [Fact]
    public async Task Sin_tienda_en_sesion_no_se_puede_configurar()
    {
        var sinSesion = new TiendaService(_bd.Contexto(null), null!, null!, new BaseDePrueba.TiendaFija(null));

        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => sinSesion.ObtenerConfiguracionPublicaAsync(default));
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => sinSesion.GuardarConfiguracionPublicaAsync(new(true, true, true, true, FondoTienda.Crema), default));
    }
}
