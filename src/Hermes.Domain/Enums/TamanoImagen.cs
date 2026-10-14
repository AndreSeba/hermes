namespace Hermes.Domain.Enums;

// Lado más largo de cada versión: Miniatura ~200 px (listas), Catalogo ~600 px (grilla),
// Detalle ~1200 px (vista del producto). Los valores exactos están en ProcesadorImagenes.
public enum TamanoImagen
{
    Miniatura,
    Catalogo,
    Detalle,
}
