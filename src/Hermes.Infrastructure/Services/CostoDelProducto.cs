using Hermes.Domain;
using Hermes.Domain.Entities;
using Hermes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hermes.Infrastructure.Services;

// Un solo lugar donde "llega un costo nuevo" (una compra recibida, "Llegaron más" con costo): si el producto tiene
// costeo, el costo pagado pasa a ser el componente MATERIAL y el costo del producto es el total del costeo (así no
// se desincronizan); si no, es el costo directo. Va dentro de la transacción de quien llama (no guarda).
public static class CostoDelProducto
{
    public static async Task FijarPagadoAsync(HermesDbContext db, Producto producto, decimal costoPagado, CancellationToken ct)
    {
        var costeo = await db.Costeos.FirstOrDefaultAsync(c => c.ProductoId == producto.Id, ct);
        if (costeo is null)
        {
            producto.Costo = costoPagado;
            return;
        }
        costeo.Material = costoPagado;
        producto.Costo = ReglaCosteo.Total(costeo.Material, costeo.Empaque, costeo.ManoDeObra, costeo.CostoFijo);
    }
}
