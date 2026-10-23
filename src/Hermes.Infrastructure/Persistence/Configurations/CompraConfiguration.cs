using Hermes.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hermes.Infrastructure.Persistence.Configurations;

public class ProveedorConfiguration : IEntityTypeConfiguration<Proveedor>
{
    public void Configure(EntityTypeBuilder<Proveedor> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Nombre).HasMaxLength(120).IsRequired();
        builder.Property(p => p.Telefono).HasMaxLength(20);
        builder.Property(p => p.Correo).HasMaxLength(254);
        builder.Property(p => p.Notas).HasMaxLength(300);

        builder.HasOne<Tienda>().WithMany()
            .HasForeignKey(p => p.TiendaId).OnDelete(DeleteBehavior.Restrict);
        // Un nombre por tienda (el servicio además compara sin distinguir mayúsculas).
        builder.HasIndex(p => new { p.TiendaId, p.Nombre }).IsUnique();
    }
}

public class CompraConfiguration : IEntityTypeConfiguration<Compra>
{
    public void Configure(EntityTypeBuilder<Compra> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Notas).HasMaxLength(300);

        builder.HasOne<Tienda>().WithMany()
            .HasForeignKey(c => c.TiendaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.Proveedor).WithMany()
            .HasForeignKey(c => c.ProveedorId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.TiendaId, c.Fecha });
        builder.HasIndex(c => c.ProveedorId);
        builder.HasIndex(c => new { c.TiendaId, c.ClaveIdempotencia }).IsUnique();
    }
}

public class CompraLineaConfiguration : IEntityTypeConfiguration<CompraLinea>
{
    public void Configure(EntityTypeBuilder<CompraLinea> builder)
    {
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Descripcion).HasMaxLength(200).IsRequired();
        builder.Property(l => l.CostoUnitario).HasPrecision(12, 2);

        builder.HasOne<Tienda>().WithMany()
            .HasForeignKey(l => l.TiendaId).OnDelete(DeleteBehavior.Restrict);
        // Una compra nunca se borra: nada en cascada.
        builder.HasOne(l => l.Compra).WithMany(c => c.Lineas)
            .HasForeignKey(l => l.CompraId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Producto>().WithMany()
            .HasForeignKey(l => l.ProductoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Variante>().WithMany()
            .HasForeignKey(l => l.VarianteId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => l.CompraId);
        builder.HasIndex(l => l.VarianteId);
        builder.HasIndex(l => l.ProductoId);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_compra_linea_cantidad", "cantidad > 0");
            t.HasCheckConstraint("ck_compra_linea_costo", "costo_unitario > 0");
        });
    }
}
