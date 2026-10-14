using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Hermes.Application.Instagram;

// Lo que se entendió de UN producto del texto de una publicación. Todo es opcional: el parser nunca inventa; lo que no
// encuentra queda en null y lo dice en `Avisos`. La dueña lo revisa en la bandeja antes de que llegue al catálogo.
public record ProductoLeido(
    string? Nombre, decimal? Precio, decimal? PrecioAnterior, IReadOnlyList<string> Colores, IReadOnlyList<string> Tallas,
    int? Stock, string? Categoria, string? Descripcion, IReadOnlyList<string> Avisos);

public record LecturaPublicacion(IReadOnlyList<ProductoLeido> Productos, IReadOnlyList<string> Hashtags);

// El lector de publicaciones de Instagram POR REGLAS (sin IA): lógica pura, sin base de datos ni red.
//
// Reglas (docs/CONTEXTO.md, "Lector de Instagram"):
//   · Nombre = la primera línea con texto (sin emojis, hashtags ni el precio).
//   · Precio: "Bs 120", "120 Bs", "Precio: 120", "Bs. 58,50", "Bs 1.200". Ofertas: "Antes Bs 200 · Ahora Bs 150" (el
//     precio es el de "ahora"; el otro queda como precio anterior).
//   · "Colores: rojo, negro y café" / "Tallas: S-M-L" (o "talla única").
//   · Stock: "Quedan 5", "Última unidad", "Stock: 3", "8 unidades".
//   · Categoría = el primer hashtag que no sea genérico (#carteras sí, #hechoamano no).
//   · Carrusel: varias líneas numeradas con precio ("1. Cartera Bs 120", "2) Aretes - Bs 95") = varios productos; los
//     colores, tallas, stock y categoría sueltos valen para todos.
// Las coincidencias ignoran mayúsculas y tildes ("Última" = "ultima").
public static partial class ParserPublicacion
{
    public const int MaximoTexto = 5000;
    public const int MaximoProductos = 20;
    public const int MaximoNombre = 120;
    public const int MaximoDescripcion = 1000;

    private const string Monto = @"(\d{1,3}(?:\.\d{3})+(?:,\d{1,2})?|\d+(?:[.,]\d{1,2})?)";

    // Hashtags demasiado genéricos para ser una categoría.
    private static readonly HashSet<string> HashtagsGenericos = new(StringComparer.Ordinal)
    {
        "hechoamano", "hechoamanobolivia", "handmade", "bolivia", "cochabamba", "lapaz", "santacruz", "sucre", "oruro", "potosi", "tarija",
        "emprendimiento", "emprendedora", "emprendedor", "emprendimientosbolivianos", "nuevo", "nueva", "oferta", "ofertas", "promo", "promocion",
        "ventas", "venta", "tienda", "instagram", "moda", "boliviana", "bolivianos", "cbba", "elalto", "novedad", "novedades", "disponible",
        "pedidos", "delivery", "envios", "hechoenbolivia", "orgullosamentebolivia", "compralocal", "apoyalocal",
    };

    // ───────────── Entrada ─────────────

    public static LecturaPublicacion Leer(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return new LecturaPublicacion([], []);
        if (texto.Length > MaximoTexto) texto = texto[..MaximoTexto];

        var hashtags = Hashtags().Matches(texto).Select(m => m.Value[1..]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var categoria = hashtags.Select(h => (Original: h, Plano: Plano(h))).FirstOrDefault(h => !HashtagsGenericos.Contains(h.Plano)).Original;
        var categoriaTexto = categoria is null ? null : char.ToUpperInvariant(categoria[0]) + categoria[1..].ToLowerInvariant();

        var lineas = texto.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select(Limpiar).Where(l => l.Length > 0).Select(l => new Linea(l)).ToList();

        // ¿Es un carrusel? Varias líneas numeradas, cada una con su precio.
        var items = lineas.Select(l => (Linea: l, Numerada: Numerada().Match(l.Texto))).Where(x => x.Numerada.Success).ToList()
            .Where(x => BuscarPrecios(x.Numerada.Groups[2].Value).Any()).ToList();
        if (items.Count >= 2) return new LecturaPublicacion(Carrusel(lineas, items.Select(x => (x.Linea, x.Numerada.Groups[2].Value)).ToList(), categoriaTexto), hashtags);

        return new LecturaPublicacion([Individual(lineas, categoriaTexto)], hashtags);
    }

    // ───────────── Un solo producto ─────────────

    private static ProductoLeido Individual(List<Linea> lineas, string? categoria)
    {
        var comunes = Comunes(lineas);

        // Precio: se busca en todas las líneas que no son de colores/tallas.
        var encontrados = new List<(Linea Linea, Precio Precio)>();
        foreach (var l in lineas.Where(l => !l.Consumida))
            foreach (var p in BuscarPrecios(l.Texto)) encontrados.Add((l, p));
        var (precio, anterior, avisos) = ElegirPrecio(encontrados.Select(e => e.Precio).ToList());

        // El nombre: la primera línea sin consumir, sin el precio. Lo que sobra de las demás es la descripción.
        string? nombre = null;
        var descripcion = new List<string>();
        foreach (var l in lineas.Where(l => !l.Consumida))
        {
            var sinPrecio = QuitarPrecios(l.Texto, encontrados.Where(e => e.Linea == l).Select(e => e.Precio));
            if (!TieneLetras(sinPrecio)) continue; // era solo el precio ("Bs 180", "💰 Precio: 180")
            if (nombre is null) nombre = Recortar(sinPrecio, MaximoNombre, "El nombre era muy largo: lo recortamos.", avisos);
            else descripcion.Add(sinPrecio);
        }

        if (nombre is null) avisos.Add("No encontramos el nombre del producto: escribilo en la primera línea.");
        if (precio is null && !avisos.Any(a => a.Contains("precio"))) avisos.Add("No encontramos el precio. Probá con \"Bs 120\" o \"Precio: 120\".");
        avisos.AddRange(comunes.Avisos);

        return new ProductoLeido(nombre, precio, anterior, comunes.Colores, comunes.Tallas, comunes.Stock, categoria,
            descripcion.Count == 0 ? null : Recortar(string.Join("\n", descripcion), MaximoDescripcion, null, avisos), avisos);
    }

    // ───────────── Carrusel ─────────────

    private static List<ProductoLeido> Carrusel(List<Linea> lineas, List<(Linea Linea, string Resto)> items, string? categoria)
    {
        foreach (var i in items) i.Linea.Consumida = true;
        var comunes = Comunes(lineas); // colores / tallas / stock sueltos: valen para todos

        var resultado = new List<ProductoLeido>();
        foreach (var (_, resto) in items.Take(MaximoProductos))
        {
            var avisos = new List<string>();
            var (precio, anterior, avisosPrecio) = ElegirPrecio(BuscarPrecios(resto));
            avisos.AddRange(avisosPrecio);
            var nombre = QuitarPrecios(resto, BuscarPrecios(resto));
            nombre = string.IsNullOrWhiteSpace(nombre) ? null : Recortar(nombre, MaximoNombre, "El nombre era muy largo: lo recortamos.", avisos);
            if (nombre is null) avisos.Add("No encontramos el nombre de este producto.");
            avisos.AddRange(comunes.Avisos);
            resultado.Add(new ProductoLeido(nombre, precio, anterior, comunes.Colores, comunes.Tallas, comunes.Stock, categoria, null, avisos));
        }
        if (items.Count > MaximoProductos)
            resultado[^1] = resultado[^1] with { Avisos = [.. resultado[^1].Avisos, $"Solo leímos los primeros {MaximoProductos} productos de la publicación."] };
        return resultado;
    }

    // ───────────── Lo que vale para todos: colores, tallas y stock ─────────────

    private record Comun(List<string> Colores, List<string> Tallas, int? Stock, List<string> Avisos);

    private static Comun Comunes(List<Linea> lineas)
    {
        var colores = new List<string>();
        var tallas = new List<string>();
        var avisos = new List<string>();
        int? stock = null;

        foreach (var l in lineas.Where(l => !l.Consumida))
        {
            var m = EtiquetaColores().Match(l.Plano);
            if (m.Success)
            {
                colores.AddRange(Lista(l.Texto[m.Length..], avisos, "color"));
                l.Consumida = true;
                continue;
            }
            m = EtiquetaTallas().Match(l.Plano);
            if (m.Success)
            {
                var valor = l.Texto[m.Length..].Trim();
                // "Talla única" no es una talla: el producto queda como "Único".
                if (!Regex.IsMatch(Plano(valor), @"^(unic[oa]s?|talla\s+unica|unica\s+talla|ts?)\b")) tallas.AddRange(ListaTallas(valor, avisos));
                l.Consumida = true;
                continue;
            }
        }

        // Stock: la primera línea que lo diga. Si la línea no tiene nada más, se consume.
        foreach (var l in lineas.Where(l => !l.Consumida))
        {
            var s = BuscarStock(l.Plano, out var inicio, out var largo);
            if (s is null) continue;
            stock = s;
            var resto = (l.Texto[..inicio] + l.Texto[(inicio + largo)..]).Trim();
            if (!TieneLetras(resto)) l.Consumida = true;
            else l.Texto = resto.Trim(' ', '-', '–', '·', '|', ',', '.');
            l.Plano = Plano(l.Texto);
            break;
        }

        return new Comun(colores.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), tallas.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), stock, avisos);
    }

    // "rojo, negro y café" → [rojo, negro, café]
    private static List<string> Lista(string valor, List<string> avisos, string que)
    {
        var partes = SeparadorLista().Split(valor).Select(p => p.Trim().Trim('.', ';', ':', '-', '–')).Where(p => p.Length > 0).ToList();
        var largos = partes.Where(p => p.Length > 30).ToList();
        if (largos.Count > 0) avisos.Add($"Algún {que} era muy largo y no lo tomamos: revisá la lista.");
        return partes.Where(p => p.Length <= 30).ToList();
    }

    // "S-M-L", "S M L XL", "36/37/38", "S, M"
    private static List<string> ListaTallas(string valor, List<string> avisos)
    {
        var partes = Regex.Split(valor, @"[,;/\-–·\s]+|\s+y\s+").Select(p => p.Trim().Trim('.', ':')).Where(p => p.Length > 0).ToList();
        if (partes.Any(p => p.Length > 6)) { avisos.Add("Las tallas no se entendieron del todo: revisalas."); partes = partes.Where(p => p.Length <= 6).ToList(); }
        return partes.Select(p => p.ToUpperInvariant()).ToList();
    }

    private static int? BuscarStock(string plano, out int inicio, out int largo)
    {
        inicio = largo = 0;
        foreach (var patron in PatronesStock)
        {
            var m = patron.Match(plano);
            if (!m.Success) continue;
            inicio = m.Index;
            largo = m.Length;
            return m.Groups.Count > 1 && m.Groups[1].Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 1;
        }
        return null;
    }

    private static readonly Regex[] PatronesStock =
    [
        PatronStock(@"(?:solo\s+|ultimas?\s+)?quedan?\s+(\d{1,5})(?:\s+(?:unidades?|piezas?|pares?))?"),
        PatronStock(@"ultimas?\s+(\d{1,5})(?:\s+(?:unidades?|piezas?|pares?))?"),
        PatronStock(@"ultim[ao]s?\s+(?:unidad|pieza|par)\b"),
        PatronStock(@"(?:stock|disponibles?)\s*[:\-]\s*(\d{1,5})"),
        PatronStock(@"(\d{1,5})\s+(?:unidades?|piezas?|pares?)(?:\s+disponibles?)?\b"),
        PatronStock(@"(\d{1,5})\s+disponibles?\b"),
    ];

    private static Regex PatronStock(string p) => new(p, RegexOptions.CultureInvariant);

    // ───────────── Precios ─────────────

    private enum Tipo { Ahora, Antes, Normal }

    private record Precio(Tipo Tipo, decimal Valor, int Inicio, int Largo);

    // Los patrones, del más específico al más general. Se aplican sobre el texto sin tildes ni mayúsculas.
    private static readonly (Tipo Tipo, Regex Regex)[] PatronesPrecio =
    [
        // (?<![a-z]) / (?![a-z]): la palabra entera. Sin eso, "era" calza dentro de "cart-era" y "puls-era".
        (Tipo.Ahora, new(@"(?<![a-z])(?:ahora|oferta|promo(?:cion)?)(?![a-z])\s*(?:a|en|solo)?\s*[:\-]?\s*(?:bs\.?|bob)?\s*" + Monto, RegexOptions.CultureInvariant)),
        (Tipo.Antes, new(@"(?<![a-z])(?:antes|normal|regular|precio\s+normal|precio\s+anterior|era)(?![a-z])\s*[:\-]?\s*(?:bs\.?|bob)?\s*" + Monto, RegexOptions.CultureInvariant)),
        (Tipo.Normal, new(@"(?<![a-z])precio(?:\s+(?:de\s+venta|final|unitario))?(?![a-z])\s*[:\-]?\s*(?:bs\.?|bob)?\s*" + Monto + @"(?:\s*(?:bs\.?|bob|bolivianos))?", RegexOptions.CultureInvariant)),
        (Tipo.Normal, new(@"(?<![a-z])(?:bs\.?|bob)\s*" + Monto, RegexOptions.CultureInvariant)),
        (Tipo.Normal, new(Monto + @"\s*(?:bs\.?|bob|bolivianos)(?![a-z])", RegexOptions.CultureInvariant)),
    ];

    private static List<Precio> BuscarPrecios(string original)
    {
        var plano = Plano(original);
        var todos = new List<Precio>();
        foreach (var (tipo, regex) in PatronesPrecio)
            foreach (Match m in regex.Matches(plano))
            {
                var valor = LeerMonto(m.Groups[1].Value);
                if (valor is { } v and > 0) todos.Add(new Precio(tipo, v, m.Index, m.Length));
            }
        // Sin repetir: si dos patrones tocan el mismo pedazo, gana el primero (el más específico).
        var elegidos = new List<Precio>();
        foreach (var p in todos.OrderBy(p => p.Inicio))
            if (!elegidos.Any(e => p.Inicio < e.Inicio + e.Largo && e.Inicio < p.Inicio + p.Largo)) elegidos.Add(p);
        return elegidos;
    }

    // "1.200" → 1200 · "58,50" → 58,5 · "120" → 120. Con punto y 3 dígitos se entiende como miles ("1.200").
    private static decimal? LeerMonto(string texto)
    {
        var t = Regex.IsMatch(texto, @"^\d{1,3}(\.\d{3})+(,\d{1,2})?$") ? texto.Replace(".", "").Replace(',', '.') : texto.Replace(',', '.');
        return decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) && v <= 1_000_000m ? v : null;
    }

    private static (decimal? Precio, decimal? Anterior, List<string> Avisos) ElegirPrecio(List<Precio> precios)
    {
        var avisos = new List<string>();
        var ahora = precios.FirstOrDefault(p => p.Tipo == Tipo.Ahora);
        var antes = precios.FirstOrDefault(p => p.Tipo == Tipo.Antes);
        var normales = precios.Where(p => p.Tipo == Tipo.Normal).ToList();

        decimal? precio = ahora?.Valor ?? normales.FirstOrDefault()?.Valor;
        if (ahora is null)
        {
            var distintos = normales.Select(p => p.Valor).Distinct().ToList();
            if (distintos.Count > 1)
                avisos.Add($"Encontramos varios precios ({string.Join(", ", distintos.Select(d => $"Bs {d.ToString("0.##", new CultureInfo("es-BO"))}"))}): revisá cuál es el del producto.");
        }
        if (precio is null && antes is not null) avisos.Add("Solo encontramos un precio anterior (\"antes\"): falta el precio de ahora.");
        return (precio, antes?.Valor, avisos);
    }

    private static string QuitarPrecios(string texto, IEnumerable<Precio> precios)
    {
        var sb = new StringBuilder(texto);
        foreach (var p in precios.OrderByDescending(p => p.Inicio)) sb.Remove(p.Inicio, p.Largo);
        return Recorte().Replace(sb.ToString(), " ").Trim().Trim('-', '–', '—', ':', '|', '·', '•', '*', ',', '.', '!', '¡', '(', ')', ' ');
    }

    // ───────────── Texto ─────────────

    private class Linea(string texto)
    {
        public string Texto { get; set; } = texto;
        public string Plano { get; set; } = ParserPublicacion.Plano(texto);
        public bool Consumida { get; set; }
    }

    // Sin emojis, hashtags ni espacios de más. Una línea que quedó solo con adornos ("-----") se descarta.
    private static string Limpiar(string linea)
    {
        var l = Hashtags().Replace(linea, " ");
        l = Emojis().Replace(l, string.Empty);
        l = Recorte().Replace(l, " ").Trim();
        return TieneLetras(l) || l.Any(char.IsDigit) ? l : string.Empty;
    }

    private static bool TieneLetras(string s) => s.Any(char.IsLetter);

    private static string Recortar(string texto, int maximo, string? avisoSiSePasa, List<string> avisos)
    {
        if (texto.Length <= maximo) return texto;
        if (avisoSiSePasa is not null) avisos.Add(avisoSiSePasa);
        return texto[..maximo].TrimEnd();
    }

    // Minúsculas y sin tildes, con el MISMO largo que el original (así las posiciones coinciden): "Última" → "ultima".
    private static string Plano(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormC))
        {
            var d = c.ToString().Normalize(NormalizationForm.FormD);
            sb.Append(char.ToLowerInvariant(char.IsLetter(d[0]) ? d[0] : c));
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"#[\p{L}\p{N}_]+")]
    private static partial Regex Hashtags();

    // Emojis y símbolos (los emojis son pares sustitutos en UTF-16), selectores de variación y uniones.
    [GeneratedRegex(@"[\p{So}\p{Cs}\p{Cn}️‍⃣​]")]
    private static partial Regex Emojis();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Recorte();

    // "1. Cartera Bs 120", "2) Aretes", "3- Collar", "4 Pulsera Bs 40" (esta última solo si lleva precio)
    [GeneratedRegex(@"^\s*(\d{1,2})\s*(?:[.)\-–:]\s*|\s+(?=\p{L}))(\p{L}.*)$")]
    private static partial Regex Numerada();

    [GeneratedRegex(@"^(colores?|color)\s*[:\-–]\s*")]
    private static partial Regex EtiquetaColores();

    [GeneratedRegex(@"^(tallas?|talles?)\s*[:\-–]\s*")]
    private static partial Regex EtiquetaTallas();

    [GeneratedRegex(@"\s*[,;/]\s*|\s+y\s+|\s+e\s+|\s+o\s+|\s*&\s*", RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorLista();
}
