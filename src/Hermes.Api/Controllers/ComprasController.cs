using Hermes.Application.Dtos;
using Hermes.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hermes.Api.Controllers;

[ApiController]
[Route("api/v1/compras")]
[Authorize]
public class ComprasController : ControllerBase
{
    private readonly ICompraService _compras;

    public ComprasController(ICompraService compras) => _compras = compras;

    // Más nuevas primero, por cursor. Vistas: Todas, EnCamino (sin recibir) y PorPagar (deuda con proveedores).
    [HttpGet]
    public async Task<ActionResult<PaginaDeComprasDto>> Listar([FromQuery] FiltroComprasDto filtro, CancellationToken ct)
        => Ok(await _compras.ListarAsync(filtro, ct));

    [HttpGet("resumen")]
    public async Task<ActionResult<ResumenComprasDto>> Resumen(CancellationToken ct)
        => Ok(await _compras.ResumenAsync(ct));

    // Comparador: el último costo de cada proveedor para un producto, el más barato primero.
    [HttpGet("comparador/{productoId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<PrecioProveedorDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<PrecioProveedorDto>>> Comparar(Guid productoId, CancellationToken ct)
        => Ok(await _compras.CompararAsync(productoId, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CompraDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CompraDto>> Obtener(Guid id, CancellationToken ct)
        => Ok(await _compras.ObtenerAsync(id, ct));

    // 201 si se registró; 200 si esa ClaveIdempotencia ya había registrado la compra (se devuelve la misma).
    [HttpPost]
    [ProducesResponseType(typeof(CompraDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(CompraDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CompraDto>> Registrar([FromBody] RegistrarCompraDto dto, CancellationToken ct)
    {
        var resultado = await _compras.RegistrarAsync(dto, ct);
        return resultado.Creada
            ? CreatedAtAction(nameof(Obtener), new { id = resultado.Compra.Id }, resultado.Compra)
            : Ok(resultado.Compra);
    }

    // Llegó la mercadería: suma stock, actualiza el costo y devuelve los avisos de margen.
    [HttpPost("{id:guid}/recibir")]
    [ProducesResponseType(typeof(ResultadoRecibirDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ResultadoRecibirDto>> Recibir(Guid id, CancellationToken ct)
        => Ok(await _compras.RecibirAsync(id, ct));

    [HttpPost("{id:guid}/pagar")]
    [ProducesResponseType(typeof(CompraDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CompraDto>> Pagar(Guid id, CancellationToken ct)
        => Ok(await _compras.PagarAsync(id, ct));

    [HttpPost("{id:guid}/anular")]
    [ProducesResponseType(typeof(CompraDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CompraDto>> Anular(Guid id, CancellationToken ct)
        => Ok(await _compras.AnularAsync(id, ct));
}
