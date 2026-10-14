using Hermes.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hermes.Infrastructure.Persistence.Configurations;

public class ProductoConfiguration : IEntityTypeConfiguration<Producto>
{
    public void Configure(EntityTypeBuilder<Producto> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Nombre).HasMaxLength(120).IsRequired();
        builder.Property(p => p.Descripcion).HasMaxLength(1000);
        builder.Property(p => p.Precio).HasPrecision(12, 2);
        builder.Property(p => p.Costo).HasPrecision(12, 2);

        builder.HasOne<Tienda>().WithMany()
            .HasForeignKey(p => p.TiendaId).OnDelete(DeleteBehavior.Restrict);
        // Las categorías se desactivan, nunca se borran: no hay forma de dejar un producto huérfano.
        builder.HasOne(p => p.Categoria).WithMany()
            .HasForeignKey(p => p.CategoriaId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.TiendaId, p.Archivado });
        builder.HasIndex(p => p.CategoriaId);

        // Un CHECK por regla de negocio, como en CMP: no solo validado en C#.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_producto_precio", "precio > 0");
            t.HasCheckConstraint("ck_producto_costo", "costo >= 0");
        });
    }
}

public class VarianteConfiguration : IEntityTypeConfiguration<Variante>
{
    public void Configure(EntityTypeBuilder<Variante> builder)
    {
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Color).HasMaxLength(30).IsRequired();
        builder.Property(v => v.Talla).HasMaxLength(30).IsRequired();

        builder.HasOne<Tienda>().WithMany()
            .HasForeignKey(v => v.TiendaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(v => v.Producto).WithMany(p => p.Variantes)
            .HasForeignKey(v => v.ProductoId).OnDelete(DeleteBehavior.Restrict);

        // Una combinación por producto. Color/Talla vacíos (no null) para que esto valga también
        // para la variante "Único". Se compara en minúsculas desde el servicio.
        builder.HasIndex(v => new { v.ProductoId, v.Color, v.Talla }).IsUnique();
    }
}

public class ProductoFotoConfiguration : IEntityTypeConfiguration<ProductoFoto>
{
    public void Configure(EntityTypeBuilder<ProductoFoto> builder)
    {
        builder.HasKey(f => f.Id);

        builder.HasOne<Tienda>().WithMany()
            .HasForeignKey(f => f.TiendaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(f => f.Producto).WithMany(p => p.Fotos)
            .HasForeignKey(f => f.ProductoId).OnDelete(DeleteBehavior.Cascade);
        // Borrar la imagen (ImagenService) se lleva su vínculo con el producto.
        builder.HasOne<Imagen>().WithMany()
            .HasForeignKey(f => f.ImagenId).OnDelete(DeleteBehavior.Cascade);

        // Una imagen, un solo producto. SIN índice único en Orden: al reordenar, las filas se
        // actualizan una por una y se chocarían a mitad de camino.
        builder.HasIndex(f => f.ImagenId).IsUnique();
        builder.HasIndex(f => new { f.ProductoId, f.Orden });
    }
}
