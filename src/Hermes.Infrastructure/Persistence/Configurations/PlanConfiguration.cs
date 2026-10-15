using Hermes.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hermes.Infrastructure.Persistence.Configurations;

public class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    // Ids fijos: el código y las pruebas los usan, y la migración los inserta.
    public static readonly Guid IdGratis = Guid.Parse("0194f000-0000-7000-8000-000000000001");
    public static readonly Guid IdEmprendedor = Guid.Parse("0194f000-0000-7000-8000-000000000002");
    public static readonly Guid IdNegocio = Guid.Parse("0194f000-0000-7000-8000-000000000003");

    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Codigo).HasMaxLength(20).IsRequired();
        builder.HasIndex(p => p.Codigo).IsUnique();
        builder.Property(p => p.Nombre).HasMaxLength(40).IsRequired();
        builder.Property(p => p.PrecioMensual).HasPrecision(12, 2);
        builder.ToTable(t => t.HasCheckConstraint("ck_plan_precio", "precio_mensual >= 0"));

        // ⚠️ PRECIOS PROVISIONALES: el plan pide verificar los de la competencia antes de fijarlos. El administrador los
        // cambia desde el panel (PUT /api/v1/admin/planes/{id}); esto es solo el punto de partida.
        builder.HasData(
            new Plan { Id = IdGratis, Codigo = Plan.CodigoGratis, Nombre = "Gratis", PrecioMensual = 0, MaxProductos = 15, MaxAlmacenamientoMb = 100, Orden = 1 },
            new Plan { Id = IdEmprendedor, Codigo = Plan.CodigoEmprendedor, Nombre = "Emprendedor", PrecioMensual = 39, MaxProductos = 150, MaxAlmacenamientoMb = 1000, Orden = 2 },
            new Plan { Id = IdNegocio, Codigo = Plan.CodigoNegocio, Nombre = "Negocio", PrecioMensual = 89, MaxProductos = null, MaxAlmacenamientoMb = 5000, Orden = 3 });
    }
}

public class SuscripcionConfiguration : IEntityTypeConfiguration<Suscripcion>
{
    public void Configure(EntityTypeBuilder<Suscripcion> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Origen).HasMaxLength(10).IsRequired();

        builder.HasOne<Tienda>().WithMany().HasForeignKey(s => s.TiendaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.Plan).WithMany().HasForeignKey(s => s.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PagoPlan>().WithMany().HasForeignKey(s => s.PagoId).OnDelete(DeleteBehavior.Restrict);

        // "¿Qué plan tiene esta tienda ahora?" es la consulta más frecuente.
        builder.HasIndex(s => new { s.TiendaId, s.Desde });
    }
}

public class PagoPlanConfiguration : IEntityTypeConfiguration<PagoPlan>
{
    public void Configure(EntityTypeBuilder<PagoPlan> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Estado).HasConversion<string>().HasMaxLength(12);
        builder.Property(p => p.Metodo).HasMaxLength(20).IsRequired();
        builder.Property(p => p.Monto).HasPrecision(12, 2);
        builder.Property(p => p.Motivo).HasMaxLength(300);

        builder.HasOne<Tienda>().WithMany().HasForeignKey(p => p.TiendaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Plan).WithMany().HasForeignKey(p => p.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Imagen>().WithMany().HasForeignKey(p => p.ComprobanteImagenId).OnDelete(DeleteBehavior.SetNull);

        // Una sola solicitud PENDIENTE por tienda, garantizado por la base (dos pedidos simultáneos no pueden crear dos).
        builder.HasIndex(p => p.TiendaId).IsUnique().HasFilter("estado = 'Pendiente'").HasDatabaseName("ux_pago_plan_pendiente");
        builder.HasIndex(p => new { p.Estado, p.Id });

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_pago_plan_meses", "meses > 0");
            t.HasCheckConstraint("ck_pago_plan_monto", "monto > 0");
        });
    }
}
