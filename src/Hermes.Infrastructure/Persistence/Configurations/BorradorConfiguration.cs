using Hermes.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hermes.Infrastructure.Persistence.Configurations;

public class BorradorConfiguration : IEntityTypeConfiguration<Borrador>
{
    public void Configure(EntityTypeBuilder<Borrador> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Estado).HasConversion<string>().HasMaxLength(12);
        builder.Property(b => b.Origen).HasMaxLength(20).IsRequired();
        builder.Property(b => b.Texto).HasMaxLength(5000).IsRequired();
        builder.Property(b => b.Nombre).HasMaxLength(120).IsRequired();
        builder.Property(b => b.Precio).HasPrecision(12, 2);
        builder.Property(b => b.PrecioAnterior).HasPrecision(12, 2);
        builder.Property(b => b.Colores).HasMaxLength(600).IsRequired();
        builder.Property(b => b.Tallas).HasMaxLength(600).IsRequired();
        builder.Property(b => b.Categoria).HasMaxLength(60);
        builder.Property(b => b.Descripcion).HasMaxLength(1000);
        builder.Property(b => b.Avisos).HasMaxLength(2000).IsRequired();
        builder.Property(b => b.InstagramMediaId).HasMaxLength(64);
        builder.Property(b => b.Enlace).HasMaxLength(300);

        builder.HasOne<Tienda>().WithMany()
            .HasForeignKey(b => b.TiendaId).OnDelete(DeleteBehavior.Restrict);
        // El producto en que se convirtió; un producto nunca se borra, así que Restrict.
        builder.HasOne<Producto>().WithMany()
            .HasForeignKey(b => b.ProductoId).OnDelete(DeleteBehavior.Restrict);

        // La bandeja: pendientes de la tienda, los más nuevos primero.
        builder.HasIndex(b => new { b.TiendaId, b.Estado, b.Id });
    }
}
