namespace Hermes.Domain.Common;

// Toda entidad de negocio que implementa esta interfaz queda aislada por tienda:
// HermesDbContext le aplica un filtro global (solo ve las filas de la tienda de la
// sesión) y completa TiendaId solo al insertar. Nunca se asigna a mano en un servicio.
public interface IPerteneceATienda
{
    Guid TiendaId { get; set; }
}
