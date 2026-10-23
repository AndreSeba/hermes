using Hermes.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hermes.Infrastructure.Persistence.Configurations;

public class RolConfiguration : IEntityTypeConfiguration<Rol>
{
    public void Configure(EntityTypeBuilder<Rol> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Nombre).HasMaxLength(60).IsRequired();
        builder.Property(r => r.Descripcion).HasMaxLength(300);

        builder.HasOne<Tienda>().WithMany()
            .HasForeignKey(r => r.TiendaId).OnDelete(DeleteBehavior.Restrict);

        // Único por tienda y solo entre los activos: uno desactivado no bloquea el nombre.
        builder.HasIndex(r => new { r.TiendaId, r.Nombre }).IsUnique().HasFilter("activo");
    }
}

public class RolPermisoConfiguration : IEntityTypeConfiguration<RolPermiso>
{
    public void Configure(EntityTypeBuilder<RolPermiso> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Codigo).HasMaxLength(60).IsRequired();

        builder.HasOne<Tienda>().WithMany()
            .HasForeignKey(p => p.TiendaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Rol>().WithMany(r => r.Permisos)
            .HasForeignKey(p => p.RolId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => new { p.RolId, p.Codigo }).IsUnique();
        builder.HasIndex(p => p.TiendaId);
    }
}

public class MiembroTiendaConfiguration : IEntityTypeConfiguration<MiembroTienda>
{
    public void Configure(EntityTypeBuilder<MiembroTienda> builder)
    {
        builder.HasKey(m => m.Id);

        builder.HasOne<Tienda>().WithMany()
            .HasForeignKey(m => m.TiendaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.Usuario).WithMany()
            .HasForeignKey(m => m.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.Rol).WithMany()
            .HasForeignKey(m => m.RolId).OnDelete(DeleteBehavior.Restrict);

        // Una persona tiene un solo rol en cada tienda.
        builder.HasIndex(m => new { m.TiendaId, m.UsuarioId }).IsUnique();
        // "¿En qué tiendas trabaja?" lo pregunta el inicio de sesión.
        builder.HasIndex(m => m.UsuarioId);
        builder.HasIndex(m => m.RolId);
    }
}
