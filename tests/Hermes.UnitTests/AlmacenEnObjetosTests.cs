using Hermes.Application.Interfaces;
using Hermes.Domain.Entities;
using Hermes.Infrastructure.Imagenes;
using Hermes.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hermes.UnitTests;

// El almacén en un bucket (Cloudflare R2) con un cliente falso en memoria: lo que importa es CUÁNDO sube y borra respecto
// del SaveChanges del servicio, y las direcciones. El cliente real (ClienteR2) no se puede probar sin cuenta.
public class AlmacenEnObjetosTests : IDisposable
{
    private readonly BaseDePrueba _bd = new();
    private readonly FakeTimeProvider _reloj = new(DateTimeOffset.UtcNow);

    public void Dispose() => _bd.Dispose();

    private sealed class BucketFalso : IClienteDeObjetos
    {
        public Dictionary<string, (byte[] Datos, string Tipo)> Objetos { get; } = [];
        public TaskCompletionSource Borrado { get; private set; } = new();

        public Task SubirAsync(string clave, byte[] datos, string contentType, CancellationToken ct)
        {
            lock (Objetos) Objetos[clave] = (datos, contentType);
            return Task.CompletedTask;
        }

        public Task<ArchivoLeido?> BajarAsync(string clave, CancellationToken ct)
        {
            lock (Objetos) return Task.FromResult(Objetos.TryGetValue(clave, out var o) ? new ArchivoLeido(o.Datos, o.Tipo) : null);
        }

        public Task BorrarAsync(IReadOnlyList<string> claves, CancellationToken ct)
        {
            lock (Objetos) foreach (var c in claves) Objetos.Remove(c);
            Borrado.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private static IOptions<AlmacenamientoOptions> Opciones() => Options.Create(new AlmacenamientoOptions
    {
        Tipo = "R2",
        R2 = new R2Options { UrlPublica = "https://fotos.ejemplo.test/" },
    });

    [Fact]
    public async Task Sube_al_guardar_la_foto_y_borra_recien_despues_de_que_el_servicio_guarda()
    {
        var bucket = new BucketFalso();
        var db = _bd.Contexto(_bd.TiendaA);
        var almacen = new AlmacenEnObjetos(db, bucket, Opciones());
        var imagenes = new ImagenService(db, almacen, new BaseDePrueba.TiendaFija(_bd.TiendaA), _reloj);

        var foto = await imagenes.SubirAsync(FotoDePrueba.Jpeg(), default);

        Assert.Equal(3, bucket.Objetos.Count); // los tres tamaños, ya en el bucket
        Assert.Equal(0, await db.Archivos.CountAsync()); // y NADA en la tabla de la base
        Assert.StartsWith("https://fotos.ejemplo.test/", foto.Miniatura);
        Assert.EndsWith("/miniatura.webp", foto.Miniatura);
        Assert.Equal(Imagen.ClaveDe(_bd.TiendaA, foto.Id, Hermes.Domain.Enums.TamanoImagen.Detalle), almacen.ClaveDeUrl(foto.Detalle));
        Assert.Null(almacen.ClaveDeUrl("/archivos/otra/cosa.webp"));
        Assert.NotNull(await almacen.ObtenerAsync(almacen.ClaveDeUrl(foto.Detalle)!, default));

        await imagenes.BorrarAsync(foto.Id, default);
        await bucket.Borrado.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(bucket.Objetos);
        Assert.Equal(0, await db.Imagenes.CountAsync());
    }

    [Fact]
    public async Task Si_el_guardado_del_servicio_falla_no_se_borra_nada_del_bucket()
    {
        var bucket = new BucketFalso();
        await bucket.SubirAsync("a/b/c.webp", [1], "image/webp", default);
        var db = _bd.Contexto(_bd.TiendaA);
        var almacen = new AlmacenEnObjetos(db, bucket, Opciones());

        await almacen.BorrarAsync(["a/b/c.webp"], default);
        db.Categorias.Add(new Categoria { Nombre = new string('x', 10) });
        db.Categorias.Add(new Categoria { Nombre = new string('x', 10) }); // duplicada: el índice único hace fallar el guardado
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        await Task.Delay(200);
        Assert.Single(bucket.Objetos);
    }

    [Fact]
    public async Task Copiar_desde_la_base_sube_cada_archivo_con_la_misma_clave()
    {
        var db = _bd.Contexto(_bd.TiendaA);
        var enBase = new ImagenService(db, new AlmacenEnBaseDeDatos(db, _reloj), new BaseDePrueba.TiendaFija(_bd.TiendaA), _reloj);
        await enBase.SubirAsync(FotoDePrueba.Jpeg(), default);
        await enBase.SubirAsync(FotoDePrueba.Jpeg(500, 400), default);
        var bucket = new BucketFalso();

        var copiados = await AlmacenEnObjetos.CopiarDesdeLaBaseAsync(_bd.Contexto(null), bucket, null, default);

        Assert.Equal(6, copiados);
        var claves = await _bd.Contexto(null).Archivos.Select(a => a.Clave).OrderBy(c => c).ToListAsync();
        Assert.Equal(claves, bucket.Objetos.Keys.Order());
        Assert.Equal(6, await AlmacenEnObjetos.CopiarDesdeLaBaseAsync(_bd.Contexto(null), bucket, null, default)); // se puede repetir
    }
}
