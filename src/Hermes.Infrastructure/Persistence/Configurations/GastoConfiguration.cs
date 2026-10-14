using Hermes.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hermes.Infrastructure.Persistence.Configurations;

public class GastoConfiguration : IEntityTypeConfiguration<Gasto>
{
    public void Configure(EntityTypeBuilder<Gasto> builder)
    {
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Concepto).HasMaxLength(120).IsRequired();
        builder.Property(g => g.Monto).HasPrecision(12, 2);

        builder.HasOne<Tienda>().WithMany()
            .HasForeignKey(g => g.TiendaId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(g => new { g.TiendaId, g.Fecha });
        builder.HasIndex(g => new { g.TiendaId, g.ClaveIdempotencia }).IsUnique();

        builder.ToTable(t => t.HasCheckConstraint("ck_gasto_monto", "monto > 0"));
    }
}
