using Hermes.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hermes.Infrastructure.Persistence.Configurations;

public class TiendaConfiguration : IEntityTypeConfiguration<Tienda>
{
    public void Configure(EntityTypeBuilder<Tienda> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Slug).HasMaxLength(40).IsRequired();
        builder.HasIndex(t => t.Slug).IsUnique();
        builder.Property(t => t.Nombre).HasMaxLength(120).IsRequired();
        builder.Property(t => t.Descripcion).HasMaxLength(500);
        builder.Property(t => t.Whatsapp).HasMaxLength(20);
        builder.Property(t => t.Color).HasMaxLength(7).IsRequired();

        builder.HasOne(t => t.Dueno).WithMany(u => u.Tiendas)
            .HasForeignKey(t => t.DuenoId).OnDelete(DeleteBehavior.Restrict);

        // El formato del slug (^[a-z0-9][a-z0-9-]{1,38}$) y del color lo valida el servicio:
        // un CHECK con regex (~) es propio de Postgres y rompe las pruebas sobre SQLite.
    }
}
