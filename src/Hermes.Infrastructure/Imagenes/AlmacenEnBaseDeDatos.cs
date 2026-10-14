using Hermes.Application.Interfaces;
using Hermes.Domain.Entities;
using Hermes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hermes.Infrastructure.Imagenes;

// Implementación actual de IAlmacenArchivos: los archivos van a la tabla `archivos` de Postgres.
// Decisión del usuario (la plataforma la usa una sola persona por ahora); ver docs/PLAN.md, M2.
// Guardar y borrar solo marcan el cambio en el contexto: se persisten con el SaveChanges del
// servicio que llama, en la MISMA transacción que el registro de la imagen.
public class AlmacenEnBaseDeDatos : IAlmacenArchivos
{
    private readonly HermesDbContext _db;
    private readonly TimeProvider _reloj;

    public AlmacenEnBaseDeDatos(HermesDbContext db, TimeProvider reloj)
    {
        _db = db;
        _reloj = reloj;
    }

    public Task GuardarAsync(string clave, byte[] datos, string contentType, CancellationToken ct)
    {
        _db.Archivos.Add(new Archivo
        {
            Clave = clave,
            Datos = datos,
            ContentType = contentType,
            Bytes = datos.Length,
            CreadoEn = _reloj.GetUtcNow(),
        });
        return Task.CompletedTask;
    }

    public async Task<ArchivoLeido?> ObtenerAsync(string clave, CancellationToken ct)
        => await _db.Archivos.AsNoTracking()
            .Where(a => a.Clave == clave)
            .Select(a => new ArchivoLeido(a.Datos, a.ContentType))
            .FirstOrDefaultAsync(ct);

    public async Task BorrarAsync(IEnumerable<string> claves, CancellationToken ct)
    {
        var lista = claves.ToList();
        var existentes = await _db.Archivos.Where(a => lista.Contains(a.Clave)).ToListAsync(ct);
        _db.Archivos.RemoveRange(existentes);
    }

    public string Url(string clave) => $"/archivos/{clave}";
}
