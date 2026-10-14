using Hermes.Application.Instagram;

namespace Hermes.UnitTests;

// El lector de publicaciones contra textos como los que escribe la gente en Instagram: con emojis, mayúsculas, sin tildes,
// el precio en cualquier lado, ofertas y carruseles. Cada caso dice qué debe entender; los que no se entienden bien se
// listan JUNTOS al final (así un cambio al lector muestra todo lo que rompió, no solo lo primero).
public class ParserPublicacionTests
{
    private record Caso(
        string Titulo, string Texto, string? Nombre, decimal? Precio = null, decimal? Anterior = null, string[]? Colores = null,
        string[]? Tallas = null, int? Stock = null, string? Categoria = null, int Productos = 1, string? Descripcion = null);

    private static readonly Caso[] Casos =
    [
        new("Formato completo", "Cartera de cuero Mía\nBs 180\nColores: café, negro\nTallas: S, M\nQuedan 5\n#carteras #hechoamano",
            "Cartera de cuero Mía", 180, Colores: ["café", "negro"], Tallas: ["S", "M"], Stock: 5, Categoria: "Carteras"),
        new("Con emojis", "✨ Aretes de plata Killa ✨\n💰 Precio: 95 Bs\n📦 Quedan 12 unidades\n#aretes #joyas",
            "Aretes de plata Killa", 95, Stock: 12, Categoria: "Aretes"),
        new("Precio en la primera línea", "Collar Salar turquesa - Bs 120\nPiedra natural boliviana\n#collares",
            "Collar Salar turquesa", 120, Categoria: "Collares", Descripcion: "Piedra natural boliviana"),
        new("Oferta en dos líneas", "Mochila Andina 🎒\nAntes Bs 250\nAhora Bs 199\nQuedan 3", "Mochila Andina", 199, 250, Stock: 3),
        new("Oferta en la misma línea", "Vestido floral: antes Bs 300 ahora Bs 240", "Vestido floral", 240, 300),
        new("Decimales con coma", "Pulsera de hilo\nBs. 58,50\n#pulseras", "Pulsera de hilo", 58.5m, Categoria: "Pulseras"),
        new("Miles con punto y tallas con guion", "Poncho de alpaca\nPrecio: Bs 1.200\nTallas: S-M-L", "Poncho de alpaca", 1200, Tallas: ["S", "M", "L"]),
        new("Talla única no es una talla", "Chalina tejida\nBs 90\nTalla: única", "Chalina tejida", 90, Tallas: []),
        new("Colores con «y»", "Billetera pequeña\nBs 65\nColores: rojo, negro y azul", "Billetera pequeña", 65, Colores: ["rojo", "negro", "azul"]),
        new("Carrusel numerado con punto", "Novedades de la semana 🛍️\n1. Cartera Bs 120\n2. Aretes Bs 95\n3. Collar Bs 150\n#accesorios",
            "Cartera", 120, Categoria: "Accesorios", Productos: 3),
        new("Carrusel con paréntesis y guion", "1) Pulsera - Bs 40\n2) Anillo - Bs 55", "Pulsera", 40, Productos: 2),
        new("Precio sin «Bs»", "Zapatos de cuero\nPrecio: 120\nTallas: 36-37-38-39", "Zapatos de cuero", 120, Tallas: ["36", "37", "38", "39"]),
        new("Precio como «120 Bs» y «Color:» en singular", "Bufanda de lana\n120 Bs\nColor: gris", "Bufanda de lana", 120, Colores: ["gris"]),
        new("Última unidad", "Casaca de cuero vintage\nBs 350\nÚltima unidad!", "Casaca de cuero vintage", 350, Stock: 1),
        new("Últimas N", "Zapatillas urbanas\nBs 280\nÚltimas 2", "Zapatillas urbanas", 280, Stock: 2),
        new("Stock con etiqueta", "Gorro de lana\nBs 45\nStock: 8", "Gorro de lana", 45, Stock: 8),
        new("Todo en mayúsculas y sin tildes", "CARTERA DE CUERO\nPRECIO: BS 200\nCOLORES: CAFE Y NEGRO\nQUEDAN 4", "CARTERA DE CUERO", 200, Colores: ["CAFE", "NEGRO"], Stock: 4),
        new("Teléfono que no es un precio", "Mochila escolar\nBs 150\nPedidos al 76815206 📲\n#mochilas", "Mochila escolar", 150, Categoria: "Mochilas", Descripcion: "Pedidos al 76815206"),
        new("Hashtag genérico primero", "Cuaderno artesanal\nBs 30\n#hechoamano #cochabamba #cuadernos", "Cuaderno artesanal", 30, Categoria: "Cuadernos"),
        new("Solo hashtags genéricos: sin categoría", "Llavero de cuero\nBs 15\n#hechoamano #bolivia #emprendimiento", "Llavero de cuero", 15, Categoria: null),
        new("«Solo quedan N pares»", "Aros de plata\nBs 80\nSolo quedan 3 pares", "Aros de plata", 80, Stock: 3),
        new("Oferta sin «antes»", "Falda tableada\nOferta Bs 99", "Falda tableada", 99),
        new("Descripción de varias líneas", "Cartera\nBs 100\nHecha a mano\nCuero genuino", "Cartera", 100, Descripcion: "Hecha a mano\nCuero genuino"),
        new("Sin precio", "Mañana llegan carteras nuevas", "Mañana llegan carteras nuevas", null),
        new("Solo el precio: sin nombre", "Bs 120", null, 120),
        new("Número de WhatsApp pegado con Bs", "Sandalias de cuero\nBs 110\nTallas: 35, 36, 37\nEscribinos al 76815206", "Sandalias de cuero", 110, Tallas: ["35", "36", "37"], Descripcion: "Escribinos al 76815206"),
        new("Texto con saltos de línea de Windows", "Mantel bordado\r\nBs 175\r\nColores: crudo y verde\r\n", "Mantel bordado", 175, Colores: ["crudo", "verde"]),
    ];

    [Fact]
    public void Lee_bien_los_textos_de_ejemplo()
    {
        var fallas = new List<string>();
        foreach (var c in Casos)
        {
            var lectura = ParserPublicacion.Leer(c.Texto);
            void Falla(string campo, object? esperado, object? real) => fallas.Add($"[{c.Titulo}] {campo}: esperaba «{esperado}» y leyó «{real}»");

            if (lectura.Productos.Count != c.Productos) { Falla("cantidad de productos", c.Productos, lectura.Productos.Count); continue; }
            var p = lectura.Productos[0];
            if (p.Nombre != c.Nombre) Falla("nombre", c.Nombre, p.Nombre);
            if (p.Precio != c.Precio) Falla("precio", c.Precio, p.Precio);
            if (p.PrecioAnterior != c.Anterior) Falla("precio anterior", c.Anterior, p.PrecioAnterior);
            if (!(c.Colores ?? []).SequenceEqual(p.Colores)) Falla("colores", string.Join(",", c.Colores ?? []), string.Join(",", p.Colores));
            if (!(c.Tallas ?? []).SequenceEqual(p.Tallas)) Falla("tallas", string.Join(",", c.Tallas ?? []), string.Join(",", p.Tallas));
            if (p.Stock != c.Stock) Falla("stock", c.Stock, p.Stock);
            if (p.Categoria != c.Categoria) Falla("categoría", c.Categoria, p.Categoria);
            if (c.Descripcion is not null && p.Descripcion != c.Descripcion) Falla("descripción", c.Descripcion, p.Descripcion);
        }

        Assert.True(fallas.Count == 0, "El lector no entendió:\n" + string.Join("\n", fallas));
    }

    [Fact]
    public void Un_carrusel_trae_un_producto_por_linea_con_su_nombre_y_su_precio()
    {
        var r = ParserPublicacion.Leer("Novedades 🛍️\n1. Cartera de cuero Bs 120\n2. Aretes de plata Bs 95\n3. Collar turquesa Bs 150\nTallas: única\nQuedan 4\n#accesorios");

        Assert.Equal(["Cartera de cuero", "Aretes de plata", "Collar turquesa"], r.Productos.Select(p => p.Nombre));
        Assert.Equal<decimal?>([120, 95, 150], r.Productos.Select(p => p.Precio));
        Assert.All(r.Productos, p => Assert.Equal((4, "Accesorios"), (p.Stock, p.Categoria))); // lo suelto vale para todos
    }

    [Fact]
    public void Una_sola_linea_numerada_no_es_un_carrusel()
    {
        var r = ParserPublicacion.Leer("1. Cartera de cuero Bs 120\nHecha a mano");

        Assert.Single(r.Productos);
    }

    [Fact]
    public void Un_carrusel_largo_se_corta_en_veinte_y_avisa()
    {
        var texto = string.Join("\n", Enumerable.Range(1, 25).Select(i => $"{i}. Producto {i} Bs {10 + i}"));

        var r = ParserPublicacion.Leer(texto);

        Assert.Equal(ParserPublicacion.MaximoProductos, r.Productos.Count);
        Assert.Contains(r.Productos[^1].Avisos, a => a.Contains("primeros 20"));
    }

    [Fact]
    public void Con_varios_precios_sin_marcar_toma_el_primero_y_avisa()
    {
        var p = ParserPublicacion.Leer("Combo aretes y collar\nAretes Bs 95\nCollar Bs 120").Productos[0];

        Assert.Equal(95m, p.Precio);
        Assert.Contains(p.Avisos, a => a.Contains("varios precios") && a.Contains("95") && a.Contains("120"));
    }

    [Fact]
    public void Lo_que_no_encuentra_queda_vacio_y_lo_avisa_sin_inventar()
    {
        var p = ParserPublicacion.Leer("Hola a todos, mañana abrimos").Productos[0];

        Assert.Null(p.Precio);
        Assert.Empty(p.Colores);
        Assert.Empty(p.Tallas);
        Assert.Null(p.Stock);
        Assert.Contains(p.Avisos, a => a.Contains("No encontramos el precio"));
    }

    [Fact]
    public void Sin_nombre_lo_avisa()
    {
        var p = ParserPublicacion.Leer("Bs 120\n#carteras").Productos[0];

        Assert.Null(p.Nombre);
        Assert.Contains(p.Avisos, a => a.Contains("nombre"));
    }

    [Fact]
    public void Un_precio_anterior_sin_precio_de_ahora_no_se_toma_como_el_precio()
    {
        var p = ParserPublicacion.Leer("Casaca de lana\nAntes Bs 300").Productos[0];

        Assert.Null(p.Precio);
        Assert.Equal(300m, p.PrecioAnterior);
        Assert.Contains(p.Avisos, a => a.Contains("anterior"));
    }

    [Fact]
    public void Un_nombre_demasiado_largo_se_recorta_y_avisa()
    {
        var p = ParserPublicacion.Leer(new string('a', 200) + "\nBs 10").Productos[0];

        Assert.Equal(ParserPublicacion.MaximoNombre, p.Nombre!.Length);
        Assert.Contains(p.Avisos, a => a.Contains("recortamos"));
    }

    [Fact]
    public void Un_color_o_una_talla_absurdamente_larga_no_se_toma_y_avisa()
    {
        var p = ParserPublicacion.Leer("Bolso\nBs 100\nColores: rojo, " + new string('x', 60) + "\nTallas: ABCDEFGHIJKL").Productos[0];

        Assert.Equal(["rojo"], p.Colores);
        Assert.Empty(p.Tallas);
        Assert.Equal(2, p.Avisos.Count(a => a.Contains("color") || a.Contains("tallas")));
    }

    [Fact]
    public void Colores_y_tallas_repetidos_se_juntan_sin_distinguir_mayusculas()
    {
        var p = ParserPublicacion.Leer("Polera\nBs 80\nColores: Rojo, rojo, ROJO y azul\nTallas: S, s, M").Productos[0];

        Assert.Equal(["Rojo", "azul"], p.Colores);
        Assert.Equal(["S", "M"], p.Tallas);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  \n")]
    [InlineData("#carteras #hechoamano")]
    [InlineData("✨✨✨\n-----")]
    public void Un_texto_vacio_o_de_puros_adornos_no_trae_productos_con_nombre(string? texto)
    {
        var r = ParserPublicacion.Leer(texto);

        Assert.All(r.Productos, p => Assert.Null(p.Nombre));
    }

    [Fact]
    public void Un_texto_gigante_no_se_cuelga_ni_se_pasa_del_limite()
    {
        var texto = string.Join("\n", Enumerable.Range(0, 5000).Select(i => $"Linea {i} con cosas Bs {i} #tag{i}"));

        var r = ParserPublicacion.Leer(texto);

        Assert.True(r.Productos.Count <= ParserPublicacion.MaximoProductos);
    }

    [Fact]
    public void Los_hashtags_se_devuelven_sin_el_numeral_y_sin_repetir()
    {
        var r = ParserPublicacion.Leer("Cartera Bs 100\n#Carteras #hechoamano #carteras");

        Assert.Equal(["Carteras", "hechoamano"], r.Hashtags);
    }

    [Fact]
    public void Un_monto_absurdo_no_se_toma_como_precio()
    {
        var p = ParserPublicacion.Leer("Casa\nBs 99999999").Productos[0];

        Assert.Null(p.Precio);
    }
}
