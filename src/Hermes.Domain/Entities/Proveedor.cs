using Hermes.Domain.Common;

namespace Hermes.Domain.Entities;

// A quién le compra la tienda. Nunca se borra (las compras lo referencian): se desactiva.
public class Proveedor : IPerteneceATienda
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TiendaId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    // WhatsApp, guardado como "+591XXXXXXXX" (Validacion.Whatsapp). Es lo que usa "Pedirle a …".
    public string? Telefono { get; set; }
    public string? Notas { get; set; }
    public bool Activo { get; set; } = true;
}
