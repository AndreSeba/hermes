using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Hermes.Application.Interfaces;
using Hermes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hermes.Infrastructure.Imagenes;

// Configuración de las fotos ("Almacenamiento"). Tipo = "Base" (hoy, por defecto: tabla `archivos` de Postgres) o "R2"
// (Cloudflare R2, cuando haya muchas tiendas: ver docs/PLAN.md M2/M14 y docs/DESPLIEGUE.md).
public class AlmacenamientoOptions
{
    public const string SectionName = "Almacenamiento";

    public string Tipo { get; set; } = "Base";
    public R2Options R2 { get; set; } = new();
}

public class R2Options
{
    public string CuentaId { get; set; } = string.Empty;   // el "Account ID" de Cloudflare
    public string ClaveAcceso { get; set; } = string.Empty; // Access Key ID del token de R2
    public string ClaveSecreta { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;
    // Dirección pública del bucket (dominio propio conectado a R2), sin barra final: https://fotos.midominio.com
    public string UrlPublica { get; set; } = string.Empty;
}

// Lo mínimo que hace falta de un almacén de objetos (R2, S3…): así AlmacenEnObjetos se prueba sin una cuenta real.
public interface IClienteDeObjetos
{
    Task SubirAsync(string clave, byte[] datos, string contentType, CancellationToken ct);
    Task<ArchivoLeido?> BajarAsync(string clave, CancellationToken ct);
    Task BorrarAsync(IReadOnlyList<string> claves, CancellationToken ct);
}

// IAlmacenArchivos sobre un almacén de objetos. A diferencia de la base, el bucket NO entra en la transacción de Postgres:
//  - Guardar sube en el momento. Si después el SaveChanges del servicio falla, la foto queda subida sin registro (un archivo de
//    más en el bucket, nunca un registro sin foto).
//  - Borrar NO borra en el momento: espera a que el SaveChanges del servicio salga bien (evento SavedChanges del contexto).
//    Si falla, no se borra nada: nunca queda un registro apuntando a una foto que ya no existe.
// Así se respeta lo que el resto del código espera ("guardar y borrar van con el SaveChanges del servicio") lo más posible.
public class AlmacenEnObjetos : IAlmacenArchivos
{
    private readonly IClienteDeObjetos _cliente;
    private readonly string _urlPublica;
    private readonly ILogger<AlmacenEnObjetos>? _log;
    private readonly List<string> _porBorrar = [];

    public AlmacenEnObjetos(HermesDbContext db, IClienteDeObjetos cliente, IOptions<AlmacenamientoOptions> opciones, ILogger<AlmacenEnObjetos>? log = null)
    {
        _cliente = cliente;
        _urlPublica = opciones.Value.R2.UrlPublica.TrimEnd('/');
        _log = log;
        db.SavedChanges += (_, _) => BorrarPendientes();
        db.SaveChangesFailed += (_, _) => _porBorrar.Clear();
    }

    public Task GuardarAsync(string clave, byte[] datos, string contentType, CancellationToken ct)
        => _cliente.SubirAsync(clave, datos, contentType, ct);

    public Task<ArchivoLeido?> ObtenerAsync(string clave, CancellationToken ct) => _cliente.BajarAsync(clave, ct);

    public Task BorrarAsync(IEnumerable<string> claves, CancellationToken ct)
    {
        _porBorrar.AddRange(claves);
        return Task.CompletedTask;
    }

    public string Url(string clave) => $"{_urlPublica}/{clave}";

    public string? ClaveDeUrl(string url)
        => url.StartsWith(_urlPublica + "/", StringComparison.Ordinal) ? url[(_urlPublica.Length + 1)..] : null;

    // El evento es sincrónico: se borra en segundo plano. Si falla, quedan archivos de más en el bucket (nunca de menos).
    private void BorrarPendientes()
    {
        if (_porBorrar.Count == 0) return;
        var claves = _porBorrar.ToList();
        _porBorrar.Clear();
        _ = Task.Run(async () =>
        {
            try
            {
                await _cliente.BorrarAsync(claves, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _log?.LogError(ex, "No se pudieron borrar {Cantidad} archivos del almacén de objetos", claves.Count);
            }
        });
    }

    // Para pasar las fotos de la base al bucket UNA vez (comando `copiar-fotos-a-r2`): sube cada archivo con la MISMA clave.
    // Se puede correr de nuevo sin problema (vuelve a subir lo mismo). Recién después se cambia Almacenamiento:Tipo a "R2".
    public static async Task<int> CopiarDesdeLaBaseAsync(HermesDbContext db, IClienteDeObjetos cliente, Action<int>? avance, CancellationToken ct)
    {
        var copiados = 0;
        string? ultima = null;
        while (true)
        {
            var lote = await db.Archivos.AsNoTracking()
                .Where(a => ultima == null || string.Compare(a.Clave, ultima) > 0)
                .OrderBy(a => a.Clave).Take(50).ToListAsync(ct);
            if (lote.Count == 0) return copiados;
            foreach (var a in lote)
            {
                await cliente.SubirAsync(a.Clave, a.Datos, a.ContentType, ct);
                copiados++;
            }
            ultima = lote[^1].Clave;
            avance?.Invoke(copiados);
        }
    }
}

// El cliente real para Cloudflare R2 (API compatible con S3). NO probado contra R2 de verdad (no hay cuenta todavía): la
// primera vez, subir una foto, verla en la tienda y borrarla, mirando el bucket.
public sealed class ClienteR2 : IClienteDeObjetos, IDisposable
{
    private readonly AmazonS3Client _s3;
    private readonly string _bucket;

    public ClienteR2(IOptions<AlmacenamientoOptions> opciones)
    {
        var r2 = opciones.Value.R2;
        if (string.IsNullOrWhiteSpace(r2.CuentaId) || string.IsNullOrWhiteSpace(r2.Bucket) || string.IsNullOrWhiteSpace(r2.UrlPublica))
            throw new InvalidOperationException("Almacenamiento:Tipo es R2 pero falta Almacenamiento:R2 (CuentaId, Bucket, UrlPublica, claves).");
        _bucket = r2.Bucket;
        _s3 = new AmazonS3Client(new BasicAWSCredentials(r2.ClaveAcceso, r2.ClaveSecreta), new AmazonS3Config
        {
            ServiceURL = $"https://{r2.CuentaId}.r2.cloudflarestorage.com",
            ForcePathStyle = true,
            AuthenticationRegion = "auto",
            // R2 no acepta las sumas de verificación que el SDK nuevo agrega por defecto a cada pedido.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        });
    }

    public async Task SubirAsync(string clave, byte[] datos, string contentType, CancellationToken ct)
    {
        using var flujo = new MemoryStream(datos, writable: false);
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = clave,
            InputStream = flujo,
            ContentType = contentType,
            // La clave lleva el Id de la foto: un archivo nunca cambia, se puede guardar en caché para siempre.
            Headers = { CacheControl = "public, max-age=31536000, immutable" },
            DisablePayloadSigning = true,
        }, ct);
    }

    public async Task<ArchivoLeido?> BajarAsync(string clave, CancellationToken ct)
    {
        try
        {
            using var r = await _s3.GetObjectAsync(_bucket, clave, ct);
            using var copia = new MemoryStream();
            await r.ResponseStream.CopyToAsync(copia, ct);
            return new ArchivoLeido(copia.ToArray(), r.Headers.ContentType ?? "application/octet-stream");
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task BorrarAsync(IReadOnlyList<string> claves, CancellationToken ct)
    {
        foreach (var lote in claves.Chunk(1000))
            await _s3.DeleteObjectsAsync(new DeleteObjectsRequest
            {
                BucketName = _bucket,
                Objects = lote.Select(k => new KeyVersion { Key = k }).ToList(),
            }, ct);
    }

    public void Dispose() => _s3.Dispose();
}
