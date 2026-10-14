using Hermes.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hermes.Infrastructure.Persistence.Configurations;

public class CosteoConfiguration : IEntityTypeConfiguration<Costeo>
{
    public void Configure(EntityTypeBuilder<Costeo> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Material).HasPrecision(12, 2);
        builder.Property(c => c.Empaque).HasPrecision(12, 2);
        builder.Property(c => c.ManoDeObra).HasPrecision(12, 2);
        builder.Property(c => c.CostoFijo).HasPrecision(12, 2);

        builder.HasOne<Tienda>().WithMany()
            .HasForeignKey(c => c.TiendaId).OnDelete(DeleteBehavior.Restrict);
        // Un producto nunca se borra, así que su costeo tampoco: Restrict.
        builder.HasOne<Producto>().WithMany()
            .HasForeignKey(c => c.ProductoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => c.ProductoId).IsUnique();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_costeo_componentes", "material >= 0 and empaque >= 0 and mano_de_obra >= 0 and costo_fijo >= 0");
            t.HasCheckConstraint("ck_costeo_margen", "margen_objetivo between 1 and 90");
        });
    }
}
