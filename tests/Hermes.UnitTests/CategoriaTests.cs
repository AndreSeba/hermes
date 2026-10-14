using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Infrastructure.Services;

namespace Hermes.UnitTests;

public class CategoriaTests
{
    [Fact]
    public async Task Crear_asigna_la_tienda_de_la_sesion()
    {
        using var bd = new BaseDePrueba();
        await using var db = bd.Contexto(bd.TiendaA);

        var creada = await new CategoriaService(db).CrearAsync(new CrearCategoriaDto("  Carteras "), default);

        Assert.Equal("Carteras", creada.Nombre);
        Assert.Equal(bd.TiendaA, db.Categorias.Single().TiendaId);
    }

    [Fact]
    public async Task Una_tienda_no_ve_las_categorias_de_otra()
    {
        using var bd = new BaseDePrueba();
        await using (var dbA = bd.Contexto(bd.TiendaA))
            await new CategoriaService(dbA).CrearAsync(new CrearCategoriaDto("Carteras"), default);

        await using var dbB = bd.Contexto(bd.TiendaB);
        var deB = await new CategoriaService(dbB).ListarAsync(incluirInactivas: true, default);

        Assert.Empty(deB);
    }

    [Fact]
    public async Task Una_tienda_no_puede_editar_la_categoria_de_otra()
    {
        using var bd = new BaseDePrueba();
        CategoriaDto deA;
        await using (var dbA = bd.Contexto(bd.TiendaA))
            deA = await new CategoriaService(dbA).CrearAsync(new CrearCategoriaDto("Carteras"), default);

        await using var dbB = bd.Contexto(bd.TiendaB);
        await Assert.ThrowsAsync<CategoriaNoEncontradaException>(() =>
            new CategoriaService(dbB).ActualizarAsync(deA.Id, new ActualizarCategoriaDto("Robada", true), default));
    }

    [Fact]
    public async Task Sin_tienda_en_sesion_no_se_ve_ni_se_crea_nada()
    {
        using var bd = new BaseDePrueba();
        await using (var dbA = bd.Contexto(bd.TiendaA))
            await new CategoriaService(dbA).CrearAsync(new CrearCategoriaDto("Carteras"), default);

        await using var anonimo = bd.Contexto(null);
        var servicio = new CategoriaService(anonimo);

        Assert.Empty(await servicio.ListarAsync(incluirInactivas: true, default));
        await Assert.ThrowsAsync<SinTiendaEnSesionException>(() => servicio.CrearAsync(new CrearCategoriaDto("Otra"), default));
    }

    [Fact]
    public async Task El_mismo_nombre_se_puede_repetir_en_otra_tienda_pero_no_en_la_misma()
    {
        using var bd = new BaseDePrueba();
        await using (var dbA = bd.Contexto(bd.TiendaA))
        {
            var servicioA = new CategoriaService(dbA);
            await servicioA.CrearAsync(new CrearCategoriaDto("Carteras"), default);
            await Assert.ThrowsAsync<CategoriaDuplicadaException>(() => servicioA.CrearAsync(new CrearCategoriaDto("CARTERAS"), default));
        }

        await using var dbB = bd.Contexto(bd.TiendaB);
        var deB = await new CategoriaService(dbB).CrearAsync(new CrearCategoriaDto("Carteras"), default);
        Assert.Equal("Carteras", deB.Nombre);
    }

    [Fact]
    public async Task Texto_vacio_es_error_de_validacion()
    {
        using var bd = new BaseDePrueba();
        await using var db = bd.Contexto(bd.TiendaA);

        await Assert.ThrowsAsync<ValidacionException>(() => new CategoriaService(db).CrearAsync(new CrearCategoriaDto("   "), default));
    }
}
