namespace Hermes.Domain.Entities;

// Un archivo guardado EN LA BASE (implementación actual de IAlmacenArchivos, decisión del usuario
// mientras la plataforma la usa una sola persona; ver docs/PLAN.md, M2). Cuando las fotos pasen a
// Cloudflare R2 esta tabla se vacía copiando cada fila al bucket con la MISMA clave.
// No implementa IPerteneceATienda a propósito: las fotos de productos son públicas (la tienda
// pública las muestra) y la tienda va dentro de la clave.
public class Archivo
{
    public string Clave { get; set; } = string.Empty;
    public byte[] Datos { get; set; } = [];
    public string ContentType { get; set; } = string.Empty;
    public int Bytes { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}
