using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hermes.Public.Pages;

// 404 y cualquier otro error, con un mensaje sin detalles (nunca el de la excepción). Sin tienda: página neutra.
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ErrorModel : PageModel
{
    public int Codigo { get; private set; } = 500;

    public void OnGet(int? codigo)
    {
        Codigo = codigo ?? 500;
        Response.StatusCode = Codigo;
    }
}
