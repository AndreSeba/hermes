namespace Hermes.Application.Interfaces;

// La tienda de la sesión. En la API sale del token (claim "tienda"); en las pruebas se fija
// a mano. HermesDbContext la usa para el filtro global por tienda.
public interface ITiendaActual
{
    // null = sin sesión (rutas anónimas, migraciones). El filtro global entonces no
    // devuelve ninguna fila de negocio.
    Guid? TiendaId { get; }
}
