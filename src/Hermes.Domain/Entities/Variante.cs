using Hermes.Domain.Common;

namespace Hermes.Domain.Entities;

// Una combinación de color y talla de un producto. Todo producto tiene al menos una: sin color ni
// talla es la variante "Único" (Color y Talla vacíos). Vacío = "no aplica", NO null: así el índice
// único (producto, color, talla) funciona igual en Postgres y en SQLite (los null no chocan entre sí).
//
// NUNCA se borra: con M5 tendrá movimientos de stock. Si la dueña la saca de la lista, se desactiva
// (y si la vuelve a poner, se reactiva la MISMA, con su historial).
public class Variante : IPerteneceATienda
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TiendaId { get; set; }
    public Guid ProductoId { get; set; }
    public Producto? Producto { get; set; }

    public string Color { get; set; } = string.Empty;
    public string Talla { get; set; } = string.Empty;
    public bool Activa { get; set; } = true;
    // El orden en que la dueña las escribió ("S, M, L" no puede salir "L, S, M"). NO se ordena por Id:
    // los Guid v7 creados en el mismo milisegundo salen en cualquier orden.
    public int Orden { get; set; }

    public List<MovimientoStock> Movimientos { get; set; } = [];
}
