using Hermes.Domain.Common;

namespace Hermes.Domain.Entities;

// Una foto de un producto (la imagen vive en `imagenes`). Orden 0 = la portada. Una imagen solo
// puede estar en un producto (índice único): así borrar un producto o sacar una foto sabe de quién es.
public class ProductoFoto : IPerteneceATienda
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TiendaId { get; set; }
    public Guid ProductoId { get; set; }
    public Producto? Producto { get; set; }
    public Guid ImagenId { get; set; }
    public int Orden { get; set; }
}
