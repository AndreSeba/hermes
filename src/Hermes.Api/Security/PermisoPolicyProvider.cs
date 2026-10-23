using Hermes.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Hermes.Api.Security;

// Cualquier nombre de policy que no esté registrado a mano (como "Admin") se trata como un código de permiso: así
// `[Authorize(Policy = Permisos.VentasRegistrar)]` funciona sin registrar una policy por cada permiso. Varios códigos
// separados por «|» alcanzan con tener alguno.
public class PermisoPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _porDefecto;

    public PermisoPolicyProvider(IOptions<AuthorizationOptions> opciones) => _porDefecto = new DefaultAuthorizationPolicyProvider(opciones);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _porDefecto.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _porDefecto.GetFallbackPolicyAsync();

    public async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var registrada = await _porDefecto.GetPolicyAsync(policyName);
        if (registrada is not null) return registrada;

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermisoRequirement(policyName))
            .Build();
    }
}

public class PermisoRequirement : IAuthorizationRequirement
{
    public string[] Codigos { get; }

    public PermisoRequirement(string codigos)
        => Codigos = codigos.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public class PermisoAuthorizationHandler : AuthorizationHandler<PermisoRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermisoRequirement requirement)
    {
        if (requirement.Codigos.Any(c => context.User.HasClaim(JwtTokenService.ClaimPermiso, c)))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
