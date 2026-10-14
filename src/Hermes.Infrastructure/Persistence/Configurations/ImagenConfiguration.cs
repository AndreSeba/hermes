using Hermes.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hermes.Infrastructure.Persistence.Configurations;

public class ImagenConfiguration : IEntityTypeConfiguration<Imagen>
{
    public void Configure(EntityTypeBuilder<Imagen> builder)
    {
        builder.HasKey(i => i.Id);
        builder.HasOne<Tienda>().WithMany()
            .HasForeignKey(i => i.TiendaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => i.TiendaId);
    }
}

public class ArchivoConfiguration : IEntityTypeConfiguration<Archivo>
{
    public void Configure(EntityTypeBuilder<Archivo> builder)
    {
        builder.HasKey(a => a.Clave);
        builder.Property(a => a.Clave).HasMaxLength(200);
        builder.Property(a => a.ContentType).HasMaxLength(50).IsRequired();
        // Los bytes van en su propia tabla a propósito (lección de CMP): ninguna consulta de negocio
        // los trae. Nunca hacer Include/Select de Datos fuera de ObtenerAsync.
        builder.Property(a => a.Datos).IsRequired();
    }
}
