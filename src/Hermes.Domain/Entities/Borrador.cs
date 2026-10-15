using Hermes.Domain.Common;

namespace Hermes.Domain.Entities;

public enum EstadoBorrador
{
    Pendiente, // en la bandeja, esperando que la dueña lo revise
    Publicado, // ya es un producto del catálogo (ProductoId)
    Descartado, // la dueña dijo que no
}

// Un producto que se leyó de la publicación de Instagram y todavía NO es parte del catálogo: espera en la bandeja de
// revisión. Nada llega al catálogo sin que la dueña lo revise y lo publique. Nunca se borra: se publica o se descarta.
public class Borrador : IPerteneceATienda
{
    public const int MaximoPendientes = 200;

    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TiendaId { get; set; }

    public EstadoBorrador Estado { get; set; } = EstadoBorrador.Pendiente;
    // De dónde vino: "Manual" (la dueña pegó el texto, M11) o "Instagram" (sincronización automática, M12).
    public string Origen { get; set; } = "Manual";
    // El texto original de la publicación, tal cual (para volver a mirarlo al revisar).
    public string Texto { get; set; } = string.Empty;

    // Lo que se entendió. Todo opcional menos el nombre (sin nombre no se crea el borrador).
    public string Nombre { get; set; } = string.Empty;
    public decimal? Precio { get; set; }
    public decimal? PrecioAnterior { get; set; }
    // Listas separadas por ", " (los nombres no pueden tener comas: el lector parte por ahí).
    public string Colores { get; set; } = string.Empty;
    public string Tallas { get; set; } = string.Empty;
    public int? Stock { get; set; }
    public string? Categoria { get; set; }
    public string? Descripcion { get; set; }
    // Lo que el lector no estuvo seguro (uno por línea).
    public string Avisos { get; set; } = string.Empty;

    // El producto en que se convirtió (Estado = Publicado).
    public Guid? ProductoId { get; set; }

    // Origen = "Instagram" (M12): de qué publicación vino y su enlace. Una publicación con varios productos (carrusel con
    // lista) da varios borradores con el mismo MediaId.
    public string? InstagramMediaId { get; set; }
    public string? Enlace { get; set; }
    public List<BorradorFoto> Fotos { get; set; } = [];

    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset? ResueltoEn { get; set; }
}
