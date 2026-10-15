using Hermes.Application.Dtos;
using Hermes.Domain.Entities;
using Hermes.Infrastructure.Imagenes;
using Hermes.Infrastructure.Mantenimiento;
using Hermes.Infrastructure.Persistence.Configurations;
using Hermes.Infrastructure.Security;
using Hermes.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hermes.UnitTests;

public class LimpiezaTests : IDisposable
{
    private readonly BaseDePrueba _bd = new();
    // Las fotos que se suben en la prueba llevan un Id v7 con la hora REAL: el reloj de la limpieza se corre desde ahí.
    private static readonly DateTimeOffset Ahora = DateTimeOffset.UtcNow;

    public void Dispose() => _bd.Dispose();

    private Limpieza LimpiezaA(DateTimeOffset cuando)
    {
        var db = _bd.Contexto(null);
        var reloj = new FakeTimeProvider(cuando);
        return new Limpieza(db, new AlmacenEnBaseDeDatos(db, reloj), reloj, Options.Create(new LimpiezaOptions()), Options.Create(new JwtOptions { RefreshDias = 30 }));
    }

    private async Task<Guid> Subir(Guid tienda)
    {
        var db = _bd.Contexto(tienda);
        var reloj = new FakeTimeProvider(Ahora);
        var imagenes = new ImagenService(db, new AlmacenEnBaseDeDatos(db, reloj), new BaseDePrueba.TiendaFija(tienda), reloj);
        return (await imagenes.SubirAsync(FotoDePrueba.Jpeg(), default)).Id;
    }

    [Fact]
    public async Task Borra_las_fotos_sin_usar_de_todas_las_tiendas_pero_nunca_las_que_se_usan()
    {
        var suelta = await Subir(_bd.TiendaA);
        var sueltaDeB = await Subir(_bd.TiendaB);
        var deProducto = await Subir(_bd.TiendaA);
        var logo = await Subir(_bd.TiendaA);
        var comprobante = await Subir(_bd.TiendaB);

        await using (var db = _bd.Contexto(_bd.TiendaA))
        {
            var reloj = new FakeTimeProvider(Ahora);
            var almacen = new AlmacenEnBaseDeDatos(db, reloj);
            var sesion = new BaseDePrueba.TiendaFija(_bd.TiendaA);
            await new ProductoService(db, new ImagenService(db, almacen, sesion, reloj), almacen, sesion, reloj)
                .CrearAsync(new GuardarProductoDto("Cartera", null, 100, 0, null, true, null, null, [deProducto]), default);
        }
        await using (var db = _bd.Contexto(null))
        {
            (await db.Tiendas.SingleAsync(t => t.Id == _bd.TiendaA)).LogoImagenId = logo;
            db.PagosPlan.Add(new PagoPlan { TiendaId = _bd.TiendaB, PlanId = PlanConfiguration.IdEmprendedor, Meses = 1, Monto = 39, Metodo = "Qr", ComprobanteImagenId = comprobante, CreadoEn = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var archivosAntes = await _bd.Contexto(null).Archivos.CountAsync();

        // Recién subidas: todavía no se tocan (la dueña puede estar armando el producto).
        Assert.Equal(new ResultadoLimpieza(0, 0), await LimpiezaA(Ahora.AddHours(1)).EjecutarAsync(default));

        var r = await LimpiezaA(Ahora.AddDays(2)).EjecutarAsync(default);

        Assert.Equal(2, r.FotosBorradas);
        var quedan = await _bd.Contexto(null).Imagenes.IgnoreQueryFilters().Select(i => i.Id).ToListAsync();
        Assert.Equal(new[] { deProducto, logo, comprobante }.Order(), quedan.Order());
        Assert.DoesNotContain(suelta, quedan);
        Assert.DoesNotContain(sueltaDeB, quedan);
        Assert.Equal(archivosAntes - 2 * 3, await _bd.Contexto(null).Archivos.CountAsync()); // los 3 tamaños de cada una
        Assert.Equal(new ResultadoLimpieza(0, 0), await LimpiezaA(Ahora.AddDays(3)).EjecutarAsync(default)); // otra vuelta: nada
    }

    [Fact]
    public async Task Borra_solo_los_refresh_tokens_ya_vencidos()
    {
        Guid viejo = Guid.CreateVersion7(Ahora.AddDays(-40)), reciente = Guid.CreateVersion7(Ahora.AddDays(-5));
        await using (var db = _bd.Contexto(null))
        {
            var usuario = await db.Usuarios.FirstAsync();
            db.RefreshTokens.AddRange(
                new RefreshToken { Id = viejo, UsuarioId = usuario.Id, TiendaId = _bd.TiendaA, TokenHash = "a", CreadoEn = Ahora.AddDays(-40), ExpiraEn = Ahora.AddDays(-10) },
                new RefreshToken { Id = reciente, UsuarioId = usuario.Id, TiendaId = _bd.TiendaA, TokenHash = "b", CreadoEn = Ahora.AddDays(-5), ExpiraEn = Ahora.AddDays(25) });
            await db.SaveChangesAsync();
        }

        var r = await LimpiezaA(Ahora).EjecutarAsync(default);

        Assert.Equal(1, r.TokensBorrados);
        Assert.Equal([reciente], await _bd.Contexto(null).RefreshTokens.Select(t => t.Id).ToListAsync());
    }
}
