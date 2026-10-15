namespace Hermes.Public;

public static class Formato
{
    // "Bs 180" si es redondo, "Bs 58,50" si no (igual que el panel).
    public static string Bs(decimal monto)
        => monto == decimal.Truncate(monto)
            ? $"Bs {monto:0}"
            : $"Bs {monto:0.00}".Replace('.', ',');

    // "café · M" a partir de color y talla (los que existan).
    public static string Variante(string? color, string? talla)
        => string.Join(" · ", new[] { color, talla }.Where(s => !string.IsNullOrEmpty(s)));

    // El mensaje con el que llega el pedido a la dueña. Lo mismo hace publico.js cuando se cambia variante o cantidad.
    public static string MensajeDePedido(string tienda, string producto, string variante, int cantidad, decimal? precio, string direccion)
    {
        var detalle = variante.Length > 0 ? $" ({variante})" : string.Empty;
        var total = precio is { } p ? $" — {Bs(p * cantidad)}" : string.Empty;
        return $"Hola {tienda}! Quiero pedir: {cantidad} × {producto}{detalle}{total}. Lo vi en {direccion}";
    }

    // El mensaje con el que el cliente avisa por WhatsApp el pedido que hizo desde el carrito (ya quedó guardado en el panel).
    public static string MensajeDeCarrito(Application.Dtos.PedidoEnviadoDto p)
    {
        var lineas = string.Join("\n", p.Lineas.Select(l =>
            $"• {l.Unidades} × {l.Descripcion}" + (l.Precio is { } precio ? $" — {Bs(precio * l.Unidades)}" : string.Empty)));
        var total = p.Total is { } t ? $"\nTotal: {Bs(t)}" : string.Empty;
        var entrega = p.Entrega == Domain.Entities.EntregaPedido.Envio ? $"Envío a: {p.Direccion}" : "Paso a retirarlo.";
        return $"Hola {p.Tienda}! Te hice el pedido #{p.Numero} desde tu tienda:\n{lineas}{total}\n{entrega}\nSoy {p.Nombre}.";
    }

    public static string EnlaceWhatsapp(string telefono, string mensaje)
        => $"https://wa.me/{new string(telefono.Where(char.IsDigit).ToArray())}?text={Uri.EscapeDataString(mensaje)}";
}
