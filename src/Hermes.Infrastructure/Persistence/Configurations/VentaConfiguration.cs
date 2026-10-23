using Hermes.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hermes.Infrastructure.Persistence.Configurations;

public class VentaConfiguration : IEntityTypeConfiguration<Venta>
{
    public void Configure(EntityTypeBuilder<Venta> builder)
    {
        builder.HasKey(v => v.Id);
        builder.Property(v => v.MetodoPago).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.Cliente).HasMaxLength(120);

        builder.HasOne<Tienda>().WithMany()
            .HasForeignKey(v => v.TiendaId).OnDelete(DeleteBehavior.Restrict);
        // Quién la registró: una persona nunca se borra (se desactiva), así que nada en cascada.
        builder.HasOne<Usuario>().WithMany()
            .HasForeignKey(v => v.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(v => new { v.TiendaId, v.UsuarioId, v.Fecha });

        // Listados por período.
        builder.HasIndex(v => new { v.TiendaId, v.Fecha });
        // Una clave por tienda: el segundo intento de la misma venta choca acá (y el servicio devuelve la primera).
        builder.HasIndex(v => new { v.TiendaId, v.ClaveIdempotencia }).IsUnique();
    }
}

public class VentaLineaConfiguration : IEntityTypeConfiguration<VentaLinea>
{
    public void Configure(EntityTypeBuilder<VentaLinea> builder)
    {
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Descripcion).HasMaxLength(200).IsRequired();
        builder.Property(l => l.PrecioUnitario).HasPrecision(12, 2);
        builder.Property(l => l.CostoUnitario).HasPrecision(12, 2);

        builder.HasOne<Tienda>().WithMany()
            .HasForeignKey(l => l.TiendaId).OnDelete(DeleteBehavior.Restrict);
        // Una venta nunca se borra: nada en cascada.
        builder.HasOne(l => l.Venta).WithMany(v => v.Lineas)
            .HasForeignKey(l => l.VentaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Producto>().WithMany()
            .HasForeignKey(l => l.ProductoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Variante>().WithMany()
            .HasForeignKey(l => l.VarianteId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => l.VentaId);
        builder.HasIndex(l => l.VarianteId);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_venta_linea_cantidad", "cantidad > 0");
            t.HasCheckConstraint("ck_venta_linea_precio", "precio_unitario > 0");
            t.HasCheckConstraint("ck_venta_linea_costo", "costo_unitario >= 0");
        });
    }
}
