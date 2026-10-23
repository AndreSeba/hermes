using Hermes.Api.Security;
using Hermes.Application.Dtos;
using Hermes.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hermes.Api.Controllers;

// El token de acceso va en el cuerpo (el front lo guarda en memoria). El refresh token va en una
// cookie HttpOnly limitada a esta ruta: JavaScript no puede leerla y solo viaja a /api/v1/auth.
[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    public const string CookieRefresh = "hermes_refresh";
    private const string RutaCookie = "/api/v1/auth";

    private readonly IAuthService _authService;

    public AuthController(IAuthService authService) => _authService = authService;

    [HttpPost("registro")]
    [EnableRateLimiting(Limites.Auth)]
    [ProducesResponseType(typeof(SesionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SesionDto>> Registrar([FromBody] RegistroDto dto, CancellationToken ct)
        => Ok(ConCookie(await _authService.RegistrarAsync(dto, ct)));

    [HttpPost("login")]
    [EnableRateLimiting(Limites.Auth)]
    [ProducesResponseType(typeof(SesionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<SesionDto>> Login([FromBody] LoginDto dto, CancellationToken ct)
        => Ok(ConCookie(await _authService.LoginAsync(dto, ct)));

    // Sin cuerpo: el refresh token sale de la cookie. El front lo llama al cargar la app y
    // cuando el token de acceso vence (401).
    [HttpPost("refresh")]
    [EnableRateLimiting(Limites.Renovacion)]
    [ProducesResponseType(typeof(SesionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    // Si falla, la cookie NO se borra: con dos pestañas renovando a la vez, la que pierde borraría
    // la cookie nueva y válida que acaba de dejar la otra (el navegador comparte la cookie).
    public async Task<ActionResult<SesionDto>> Renovar(CancellationToken ct)
        => Ok(ConCookie(await _authService.RenovarAsync(Request.Cookies[CookieRefresh] ?? string.Empty, ct)));

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> CerrarSesion(CancellationToken ct)
    {
        await _authService.CerrarSesionAsync(Request.Cookies[CookieRefresh] ?? string.Empty, ct);
        BorrarCookie();
        return NoContent();
    }

    [HttpGet("yo")]
    [Authorize]
    [ProducesResponseType(typeof(PerfilDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PerfilDto>> Yo(CancellationToken ct)
        => Ok(await _authService.ObtenerPerfilAsync(User.ObtenerUsuarioId(), User.ObtenerTiendaId(), ct));

    // Pide la contraseña actual: la cookie sola no alcanza para cambiarla. Las demás sesiones de la persona se cierran; esta sigue.
    [HttpPost("cambiar-password")]
    [Authorize]
    [EnableRateLimiting(Limites.Auth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CambiarPassword([FromBody] CambiarPasswordDto dto, CancellationToken ct)
    {
        await _authService.CambiarPasswordAsync(User.ObtenerUsuarioId(), Request.Cookies[CookieRefresh], dto, ct);
        return NoContent();
    }

    private SesionDto ConCookie(SesionEmitida emitida)
    {
        Response.Cookies.Append(CookieRefresh, emitida.RefreshToken, OpcionesCookie(emitida.RefreshExpiraEn));
        return emitida.Sesion;
    }

    private void BorrarCookie() => Response.Cookies.Delete(CookieRefresh, OpcionesCookie(null));

    private CookieOptions OpcionesCookie(DateTimeOffset? expira) => new()
    {
        HttpOnly = true,
        // En desarrollo la API va por HTTP detrás del proxy de Vite; en producción siempre HTTPS.
        Secure = Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = RutaCookie,
        Expires = expira,
        IsEssential = true,
    };
}
