using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Hermes.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Hermes.Infrastructure.Security;

public class JwtTokenService
{
    // Mismo nombre que lee TiendaActualDesdeToken en la API.
    public const string ClaimTienda = "tienda";

    private readonly JwtOptions _opciones;
    private readonly TimeProvider _reloj;

    public JwtTokenService(IOptions<JwtOptions> opciones, TimeProvider reloj)
    {
        _opciones = opciones.Value;
        _reloj = reloj;
    }

    public (string Token, DateTimeOffset ExpiraEn) EmitirAccessToken(Usuario usuario, Tienda tienda)
    {
        var ahora = _reloj.GetUtcNow();
        var expira = ahora.AddMinutes(_opciones.ExpiracionMinutos);

        // "sub" llega a la API renombrado a ClaimTypes.NameIdentifier por el mapeo por defecto
        // de AddJwtBearer (misma trampa documentada en CMP). "tienda" no se renombra.
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, usuario.Email),
            new Claim(JwtRegisteredClaimNames.Name, usuario.Nombre),
            new Claim(ClaimTienda, tienda.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var credenciales = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opciones.SecretKey)), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _opciones.Issuer,
            audience: _opciones.Audience,
            claims: claims,
            notBefore: ahora.UtcDateTime,
            expires: expira.UtcDateTime,
            signingCredentials: credenciales);

        return (new JwtSecurityTokenHandler().WriteToken(token), expira);
    }

    // 256 bits aleatorios en base64url: es lo que viaja en la cookie.
    public static string GenerarRefreshToken()
        => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    // Lo que se guarda en la base.
    public static string Hash(string refreshToken)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
}
