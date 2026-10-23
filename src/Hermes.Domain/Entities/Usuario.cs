namespace Hermes.Domain.Entities;

// Cuenta de una persona. No pertenece a una tienda: es dueña de una (o más, a futuro) o trabaja en una con un rol
// (MiembroTienda).
public class Usuario
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Telefono { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    // Administrador de la PLATAFORMA (aprueba pagos, asigna planes): distinto de ser dueña de una tienda. Se asigna SOLO
    // desde el servidor (`dotnet run --project src/Hermes.Api -- promover-admin <email>`), nunca desde la web.
    public bool EsAdministrador { get; set; }
    // La cuenta la creó el dueño con una contraseña provisoria: al entrar tiene que elegir la suya (el front la pide).
    public bool DebeCambiarPassword { get; set; }
    public DateTimeOffset CreadoEn { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UltimoLoginEn { get; set; }

    public List<Tienda> Tiendas { get; set; } = [];
}
