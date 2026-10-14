using Hermes.Domain.Enums;

namespace Hermes.Domain.Entities;

// El tenant. Todo lo de negocio (catálogo, stock, ventas, compras…) cuelga de una tienda.
// Slug = la dirección pública de la tienda (/{slug}).
public class Tienda
{
    // Mostaza: el color por defecto del prototipo. Cada tienda elige el suyo (Validacion.ColorHex).
    public const string ColorPorDefecto = "#F2A33A";

    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid DuenoId { get; set; }
    public Usuario? Dueno { get; set; }

    public string Slug { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    // Siempre normalizado: "+591" + 8 dígitos de celular (Validacion.Whatsapp).
    public string? Whatsapp { get; set; }
    public string Color { get; set; } = ColorPorDefecto;
    // La foto del logo (tabla `imagenes`). Si esa imagen se borra, la base lo deja en null.
    public Guid? LogoImagenId { get; set; }
    // ¿La tienda pública se ve? Apagada: /{slug} responde "no existe" (404), como si nunca la hubiera creado.
    public bool Activa { get; set; } = true;

    // "Qué ven tus clientes" (M10). Todo encendido por defecto: para eso existe la tienda pública.
    public bool MostrarPrecios { get; set; } = true;
    // Disponible / agotado por producto y por variante (NUNCA la cantidad exacta). Apagado: no se muestra nada.
    public bool MostrarDisponibilidad { get; set; } = true;
    // El botón "Pedir por WhatsApp" (necesita que la tienda tenga su WhatsApp cargado).
    public bool PedidosPorWhatsapp { get; set; } = true;
    public FondoTienda Fondo { get; set; } = FondoTienda.Crema;

    public DateTimeOffset CreadaEn { get; set; } = DateTimeOffset.UtcNow;
}
