using Hermes.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hermes.Infrastructure.Persistence.Configurations;

public class UsuarioConfiguration : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Nombre).HasMaxLength(120).IsRequired();
        // Se guarda siempre en minúsculas (lo normaliza el servicio), así el índice único
        // alcanza sin depender de citext.
        builder.Property(u => u.Email).HasMaxLength(254).IsRequired();
        builder.HasIndex(u => u.Email).IsUnique();
        builder.Property(u => u.Telefono).HasMaxLength(20);
        builder.Property(u => u.PasswordHash).HasMaxLength(100).IsRequired();
    }
}
