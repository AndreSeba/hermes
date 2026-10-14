namespace Hermes.Domain;

// Cómo viene el margen de un producto: lo que muestran las etiquetas del catálogo.
public enum EstadoPrecio
{
    SinCosto, // todavía no cargó lo que le cuesta: no se puede saber
    Pierde,   // el precio no cubre el costo
    Bajo,     // gana menos del 20 % del precio
    Ok,
}

// LA regla del margen, en un solo lugar. ProductoService la usa también como filtro en la base
// ("Revisar precio" = Pierde o Bajo): si se cambia acá, cambia en los dos.
public static class MargenProducto
{
    public const decimal UmbralBajo = 0.20m;

    public static decimal Ganancia(decimal precio, decimal costo) => precio - costo;

    public static EstadoPrecio Estado(decimal precio, decimal costo)
    {
        if (costo <= 0) return EstadoPrecio.SinCosto;
        var ganancia = Ganancia(precio, costo);
        if (ganancia <= 0) return EstadoPrecio.Pierde;
        return ganancia < precio * UmbralBajo ? EstadoPrecio.Bajo : EstadoPrecio.Ok;
    }
}
