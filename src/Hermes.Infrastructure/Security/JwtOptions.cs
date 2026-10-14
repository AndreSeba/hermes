namespace Hermes.Infrastructure.Security;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    // NUNCA en appsettings: dotnet user-secrets set "Jwt:SecretKey" "..." o la variable Jwt__SecretKey.
    public string SecretKey { get; set; } = string.Empty;
    // Token de acceso corto; la sesión larga se sostiene con el refresh token.
    public int ExpiracionMinutos { get; set; } = 15;
    public int RefreshDias { get; set; } = 30;
}
