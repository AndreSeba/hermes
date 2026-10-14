namespace Hermes.Domain;

public enum EstadoStock
{
    SinControl, // "por encargo": no se cuentan unidades, siempre disponible
    Ok,
    Pocos,      // "por agotarse": quedan StockMinimo o menos
    Agotado,
}

// LA regla del estado del stock, en un solo lugar. El listado de Stock la usa también en SQL (para
// ordenar y filtrar): hay una prueba que compara las dos.
public static class ReglaStock
{
    public const int MinimoPorDefecto = 3;

    public static EstadoStock Estado(bool controlaStock, int total, int minimo)
    {
        if (!controlaStock) return EstadoStock.SinControl;
        if (total <= 0) return EstadoStock.Agotado;
        return total <= minimo ? EstadoStock.Pocos : EstadoStock.Ok;
    }

    // Orden de urgencia de la pantalla de Stock: lo que hay que reponer primero.
    public static int Prioridad(EstadoStock estado) => estado switch
    {
        EstadoStock.Agotado => 0,
        EstadoStock.Pocos => 1,
        EstadoStock.Ok => 2,
        _ => 3,
    };
}
