using Hermes.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hermes.Infrastructure.Persistence.Configurations;

public class InstagramConexionConfiguration : IEntityTypeConfiguration<InstagramConexion>
{
    public void Configure(EntityTypeBuilder<InstagramConexion> builder)
    {
        builder.ToTable("instagram_conexiones");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.IgUsuarioId).HasMaxLength(64).IsRequired();
        builder.Property(c => c.Usuario).HasMaxLength(64).IsRequired();
        builder.Property(c => c.TokenCifrado).HasMaxLength(2000).IsRequired();
        builder.Property(c => c.UltimoError).HasMaxLength(300);
        builder.HasOne<Tienda>().WithMany().HasForeignKey(c => c.TiendaId).OnDelete(DeleteBehavior.Restrict);
        // Una cuenta por tienda.
        builder.HasIndex(c => c.TiendaId).IsUnique();
    }
}

public class InstagramPublicacionConfiguration : IEntityTypeConfiguration<InstagramPublicacion>
{
    public void Configure(EntityTypeBuilder<InstagramPublicacion> builder)
    {
        builder.ToTable("instagram_publicaciones");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.MediaId).HasMaxLength(64).IsRequired();
        builder.Property(p => p.Enlace).HasMaxLength(300);
        builder.HasOne<Tienda>().WithMany().HasForeignKey(p => p.TiendaId).OnDelete(DeleteBehavior.Restrict);
        // Una publicación se procesa una sola vez por tienda (dos sincronizaciones a la vez: el índice corta la segunda).
        builder.HasIndex(p => new { p.TiendaId, p.MediaId }).IsUnique();
    }
}

public class BorradorFotoConfiguration : IEntityTypeConfiguration<BorradorFoto>
{
    public void Configure(EntityTypeBuilder<BorradorFoto> builder)
    {
        builder.ToTable("borrador_fotos");
        builder.HasKey(f => f.Id);
        builder.HasOne<Tienda>().WithMany().HasForeignKey(f => f.TiendaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(f => f.Borrador).WithMany(b => b.Fotos).HasForeignKey(f => f.BorradorId).OnDelete(DeleteBehavior.Restrict);
        // Borrar la imagen (limpieza, o la dueña la saca al revisar) se lleva su vínculo, como en producto_fotos.
        builder.HasOne<Imagen>().WithMany().HasForeignKey(f => f.ImagenId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(f => f.BorradorId);
        builder.HasIndex(f => f.ImagenId);
    }
}
