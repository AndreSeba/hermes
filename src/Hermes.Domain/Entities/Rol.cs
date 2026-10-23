using Hermes.Domain.Common;

namespace Hermes.Domain.Entities;

// Un conjunto de permisos con nombre que el dueño le da a las personas de su equipo ("Vendedor", "Encargado"…). Es de la tienda:
// cada negocio tiene los suyos y los edita. Se desactiva, no se borra (los miembros que lo tuvieron lo referencian).
// El DUEÑO de la tienda no tiene un Rol: tiene siempre todo el catálogo de permisos.
public class Rol : IPerteneceATienda
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TiendaId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public bool Activo { get; set; } = true;

    public List<RolPermiso> Permisos { get; set; } = [];
}

// Un permiso del rol: el código sale de Hermes.Domain.Seguridad.Permisos.Catalogo (no hay tabla de permisos).
public class RolPermiso : IPerteneceATienda
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TiendaId { get; set; }
    public Guid RolId { get; set; }
    public string Codigo { get; set; } = string.Empty;
}

// Una persona (con su propia cuenta) que trabaja en la tienda con un rol. El dueño no figura acá: es Tienda.DuenoId.
// Se desactiva, no se borra (sus ventas quedan a su nombre).
public class MiembroTienda : IPerteneceATienda
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TiendaId { get; set; }
    public Guid UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public Guid RolId { get; set; }
    public Rol? Rol { get; set; }
    public bool Activo { get; set; } = true;
    public DateTimeOffset CreadoEn { get; set; } = DateTimeOffset.UtcNow;
}
