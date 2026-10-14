using Hermes.Application.Interfaces;

namespace Hermes.Infrastructure;

// La "tienda actual" cuando NO hay sesión: la tienda pública la fija PublicoService a partir del slug de la dirección.
// Así el filtro global por tienda sigue protegiendo todas las consultas del público (un producto de otra tienda "no
// existe" aunque se pida su id), en vez de desactivar el filtro y acordarse de filtrar a mano.
//   · En la API (con login), TiendaActualDesdeToken usa esto SOLO si PublicoService la fijó en este pedido.
//   · En la tienda pública (sin login), este objeto ES el ITiendaActual.
// Un objeto por pedido (scoped): nunca se comparte entre pedidos.
public class TiendaPublicaContexto : ITiendaActual
{
    public Guid? TiendaId { get; set; }
}
