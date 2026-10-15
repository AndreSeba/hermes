using Hermes.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;

namespace Hermes.Api.Security;

public static class Politicas
{
    // Administrador de la plataforma: el claim `rol` = `admin` (solo se emite si el usuario lo es en la base).
    public const string Admin = "Admin";

    public static AuthorizationOptions AgregarPoliticas(this AuthorizationOptions opciones)
    {
        opciones.AddPolicy(Admin, p => p.RequireAuthenticatedUser().RequireClaim(JwtTokenService.ClaimRol, JwtTokenService.RolAdministrador));
        return opciones;
    }
}
