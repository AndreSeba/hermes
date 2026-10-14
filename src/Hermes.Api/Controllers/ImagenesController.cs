using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Infrastructure.Imagenes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hermes.Api.Controllers;

[ApiController]
[Route("api/v1/imagenes")]
[Authorize]
public class ImagenesController : ControllerBase
{
    private readonly IImagenService _imagenService;

    public ImagenesController(IImagenService imagenService) => _imagenService = imagenService;

    // multipart/form-data con un campo "archivo". Devuelve las urls de los 3 tamaños.
    [HttpPost]
    [RequestSizeLimit(ProcesadorImagenes.MaximoBytes + 64 * 1024)] // la foto + el sobre multipart
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ImagenDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ImagenDto>> Subir(IFormFile archivo, CancellationToken ct)
    {
        if (archivo.Length is 0 or > ProcesadorImagenes.MaximoBytes)
            throw new ImagenInvalidaException(archivo.Length == 0
                ? "El archivo está vacío."
                : $"La foto pesa demasiado (máximo {ProcesadorImagenes.MaximoBytes / 1024 / 1024} MB).");

        await using var flujo = archivo.OpenReadStream();
        using var memoria = new MemoryStream((int)archivo.Length);
        await flujo.CopyToAsync(memoria, ct);

        var creada = await _imagenService.SubirAsync(memoria.ToArray(), ct);
        return CreatedAtAction(nameof(Subir), new { id = creada.Id }, creada);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Borrar(Guid id, CancellationToken ct)
    {
        await _imagenService.BorrarAsync(id, ct);
        return NoContent();
    }
}
