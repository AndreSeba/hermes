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
    // El rol de plataforma (administrador). La política "Admin" de la API exige este claim.
    public const string ClaimRol = "rol";
    public const string RolAdministrador = "admin";

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
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, usuario.Email),
            new(JwtRegisteredClaimNames.Name, usuario.Nombre),
            new(ClaimTienda, tienda.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        // El rol de plataforma SOLO viaja en el token si en la base el usuario es administrador (se decide al emitirlo, no lo
        // elige el cliente). Dura lo que el token (15 min): si se le quita el rol, deja de renovarse.
        if (usuario.EsAdministrador) claims.Add(new Claim(ClaimRol, RolAdministrador));

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
