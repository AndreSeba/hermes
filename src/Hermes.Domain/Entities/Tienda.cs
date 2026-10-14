namespace Hermes.Domain.Entities;

// El tenant. Todo lo de negocio (catálogo, stock, ventas, compras…) cuelga de una tienda.
// Slug = la dirección pública: hermes.bo/{slug}.
public class Tienda
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid DuenoId { get; set; }
    public Usuario? Dueno { get; set; }

    public string Slug { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public string? Whatsapp { get; set; }
    public string Color { get; set; } = "#7C5CFC";
    public bool Activa { get; set; } = true;
    public DateTimeOffset CreadaEn { get; set; } = DateTimeOffset.UtcNow;
}
