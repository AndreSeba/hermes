using Hermes.Domain.Common;

namespace Hermes.Domain.Entities;

// Módulo de referencia: el patrón completo (entidad → configuración → servicio →
// controller → pruebas) para copiar en los módulos siguientes.
public class Categoria : IPerteneceATienda
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TiendaId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public bool Activa { get; set; } = true;
}
