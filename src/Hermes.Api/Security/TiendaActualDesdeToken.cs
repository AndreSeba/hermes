using Hermes.Application.Interfaces;
using Hermes.Infrastructure;

namespace Hermes.Api.Security;

// La tienda de la sesión sale del claim "tienda" del JWT. Sin request (migraciones, tareas
// en segundo plano) o sin claim, devuelve null y el filtro global no muestra nada.
//
// Excepción: en un pedido a la tienda pública (/public/{slug}, sin login), PublicoService fija la tienda a partir del slug
// en TiendaPublicaContexto y esa manda. Un pedido con sesión que no pasa por PublicoService nunca la fija.
public class TiendaActualDesdeToken : ITiendaActual
{
    public const string Claim = "tienda";

    private readonly IHttpContextAccessor _http;
    private readonly TiendaPublicaContexto _publica;

    public TiendaActualDesdeToken(IHttpContextAccessor http, TiendaPublicaContexto publica)
    {
        _http = http;
        _publica = publica;
    }

    public Guid? TiendaId =>
        _publica.TiendaId ?? (Guid.TryParse(_http.HttpContext?.User.FindFirst(Claim)?.Value, out var id) ? id : null);
}
