namespace Hermes.Domain.Entities;

// Sesión larga. En la base solo se guarda el hash SHA-256 del token, nunca el token: quien lea
// la tabla no puede usarlo. Rota en cada uso (el viejo queda revocado y apunta al nuevo); si se
// presenta uno ya revocado, alguien lo robó y se revocan todas las sesiones del usuario.
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    // La tienda con la que se abrió la sesión; el token de acceso renovado la conserva.
    public Guid TiendaId { get; set; }

    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset ExpiraEn { get; set; }
    public DateTimeOffset? RevocadoEn { get; set; }
    public Guid? ReemplazadoPorId { get; set; }
}
