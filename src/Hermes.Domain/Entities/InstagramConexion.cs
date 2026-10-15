using Hermes.Domain.Common;

namespace Hermes.Domain.Entities;

// La cuenta de Instagram (profesional: Business o Creator) conectada a una tienda (M12). Una por tienda.
// El token de Meta se guarda CIFRADO (nunca sale de la API ni se escribe en logs). Dura 60 días y se renueva solo.
public class InstagramConexion : IPerteneceATienda
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TiendaId { get; set; }

    public string IgUsuarioId { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty; // @usuario, para mostrar
    public string TokenCifrado { get; set; } = string.Empty;
    // UTC (DateTime, no DateTimeOffset: se compara en SQL también con SQLite en las pruebas).
    public DateTime TokenVence { get; set; }
    public DateTime TokenRenovado { get; set; }
    public DateTime ConectadaEn { get; set; }

    public DateTime? UltimaSincronizacion { get; set; }
    // El último problema (token vencido, Meta no responde…). null = todo bien.
    public string? UltimoError { get; set; }
    // El token ya no sirve (vencido, revocado, la dueña cambió la contraseña): hay que volver a conectar.
    public bool NecesitaReconectar { get; set; }
    public bool Activa { get; set; } = true;
}

// Una publicación de Instagram que ya se procesó (para no importarla dos veces). Productos = cuántos borradores salieron
// (0 = no se entendió ningún producto en el texto: igual queda anotada).
public class InstagramPublicacion : IPerteneceATienda
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TiendaId { get; set; }
    public string MediaId { get; set; } = string.Empty;
    public string? Enlace { get; set; }
    public DateTime PublicadaEn { get; set; }
    public DateTime ImportadaEn { get; set; }
    public int Productos { get; set; }
}

// Las fotos que vinieron con un borrador de Instagram (M12). Al publicarlo pasan a ser las fotos del producto.
public class BorradorFoto : IPerteneceATienda
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TiendaId { get; set; }
    public Guid BorradorId { get; set; }
    public Borrador? Borrador { get; set; }
    public Guid ImagenId { get; set; }
    public int Orden { get; set; }
}
