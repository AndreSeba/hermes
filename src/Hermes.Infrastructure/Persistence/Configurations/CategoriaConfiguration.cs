using Hermes.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hermes.Infrastructure.Persistence.Configurations;

public class CategoriaConfiguration : IEntityTypeConfiguration<Categoria>
{
    public void Configure(EntityTypeBuilder<Categoria> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Nombre).HasMaxLength(60).IsRequired();

        builder.HasOne<Tienda>().WithMany()
            .HasForeignKey(c => c.TiendaId).OnDelete(DeleteBehavior.Restrict);

        // Único por tienda y solo entre las activas: una desactivada no bloquea el nombre.
        builder.HasIndex(c => new { c.TiendaId, c.Nombre }).IsUnique().HasFilter("activa");
    }
}
