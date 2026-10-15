using Hermes.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hermes.Infrastructure.Persistence.Configurations;

public class PedidoConfiguration : IEntityTypeConfiguration<Pedido>
{
    public void Configure(EntityTypeBuilder<Pedido> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.ClienteNombre).HasMaxLength(80).IsRequired();
        builder.Property(p => p.ClienteCelular).HasMaxLength(20).IsRequired();
        builder.Property(p => p.Entrega).HasConversion<string>().HasMaxLength(12);
        builder.Property(p => p.Estado).HasConversion<string>().HasMaxLength(12);
        builder.Property(p => p.Direccion).HasMaxLength(200);
        builder.Property(p => p.Referencia).HasMaxLength(200);
        builder.Property(p => p.Nota).HasMaxLength(300);
        builder.Property(p => p.MotivoCancelacion).HasMaxLength(300);

        builder.HasOne<Tienda>().WithMany().HasForeignKey(p => p.TiendaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Venta>().WithMany().HasForeignKey(p => p.VentaId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.TiendaId, p.Numero }).IsUnique();
        builder.HasIndex(p => new { p.TiendaId, p.ClaveIdempotencia }).IsUnique();
        builder.HasIndex(p => new { p.TiendaId, p.Estado });

        builder.ToTable(t => t.HasCheckConstraint("ck_pedido_numero", "numero > 0"));
    }
}

public class PedidoLineaConfiguration : IEntityTypeConfiguration<PedidoLinea>
{
    public void Configure(EntityTypeBuilder<PedidoLinea> builder)
    {
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Descripcion).HasMaxLength(200).IsRequired();
        builder.Property(l => l.PrecioUnitario).HasPrecision(12, 2);

        builder.HasOne<Tienda>().WithMany().HasForeignKey(l => l.TiendaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.Pedido).WithMany(p => p.Lineas).HasForeignKey(l => l.PedidoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Producto>().WithMany().HasForeignKey(l => l.ProductoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Variante>().WithMany().HasForeignKey(l => l.VarianteId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => l.PedidoId);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_pedido_linea_cantidad", "cantidad > 0");
            t.HasCheckConstraint("ck_pedido_linea_precio", "precio_unitario > 0");
        });
    }
}
