using Hermes.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hermes.Infrastructure.Persistence.Configurations;

public class MovimientoStockConfiguration : IEntityTypeConfiguration<MovimientoStock>
{
    public void Configure(EntityTypeBuilder<MovimientoStock> builder)
    {
        builder.HasKey(m => m.Id);
        // Texto y no número: se lee en la base ("Ingreso", "Venta") y reordenar el enum no la rompe.
        builder.Property(m => m.Tipo).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Nota).HasMaxLength(200);
        builder.Property(m => m.CostoUnitario).HasPrecision(12, 2);

        builder.HasOne<Tienda>().WithMany()
            .HasForeignKey(m => m.TiendaId).OnDelete(DeleteBehavior.Restrict);
        // Nunca se borra un movimiento ni la variante que lo tiene: Restrict.
        builder.HasOne(m => m.Variante).WithMany(v => v.Movimientos)
            .HasForeignKey(m => m.VarianteId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Venta>().WithMany()
            .HasForeignKey(m => m.VentaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(m => m.VentaId);

        // Sumar el stock de una variante y leer su historial pasan por acá.
        builder.HasIndex(m => new { m.VarianteId, m.Id });

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_movimiento_cantidad", "cantidad <> 0");
            t.HasCheckConstraint("ck_movimiento_saldo", "saldo >= 0");
        });
    }
}
