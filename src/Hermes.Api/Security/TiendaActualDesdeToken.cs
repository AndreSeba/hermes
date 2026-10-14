using Hermes.Application.Interfaces;

namespace Hermes.Api.Security;

// La tienda de la sesión sale del claim "tienda" del JWT. Sin request (migraciones, tareas
// en segundo plano) o sin claim, devuelve null y el filtro global no muestra nada.
public class TiendaActualDesdeToken : ITiendaActual
{
    public const string Claim = "tienda";

    private readonly IHttpContextAccessor _http;

    public TiendaActualDesdeToken(IHttpContextAccessor http) => _http = http;

    public Guid? TiendaId =>
        Guid.TryParse(_http.HttpContext?.User.FindFirst(Claim)?.Value, out var id) ? id : null;
}
