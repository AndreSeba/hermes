using Hermes.Application;
using Hermes.Application.Dtos;
using Hermes.Application.Exceptions;
using Hermes.Application.Interfaces;
using Hermes.Domain.Entities;
using Hermes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hermes.Infrastructure.Services;

// Ningún método filtra por tienda a mano: lo hace el filtro global de HermesDbContext.
public class CategoriaService : ICategoriaService
{
    private readonly HermesDbContext _db;

    public CategoriaService(HermesDbContext db) => _db = db;

    public async Task<IReadOnlyList<CategoriaDto>> ListarAsync(bool incluirInactivas, CancellationToken ct)
    {
        IQueryable<Categoria> query = _db.Categorias.AsNoTracking();
        if (!incluirInactivas) query = query.Where(c => c.Activa);

        return await query.OrderBy(c => c.Nombre)
            .Select(c => new CategoriaDto(c.Id, c.Nombre, c.Activa))
            .ToListAsync(ct);
    }

    public async Task<CategoriaDto> CrearAsync(CrearCategoriaDto dto, CancellationToken ct)
    {
        var nombre = Validacion.Texto(dto.Nombre, 60, "El nombre de la categoría");
        await ValidarNombreLibreAsync(nombre, null, ct);

        var categoria = new Categoria { Nombre = nombre };
        _db.Categorias.Add(categoria);
        await _db.SaveChangesAsync(ct);

        return new CategoriaDto(categoria.Id, categoria.Nombre, categoria.Activa);
    }

    public async Task<CategoriaDto> ActualizarAsync(Guid id, ActualizarCategoriaDto dto, CancellationToken ct)
    {
        var categoria = await _db.Categorias.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new CategoriaNoEncontradaException(id);

        var nombre = Validacion.Texto(dto.Nombre, 60, "El nombre de la categoría");
        if (dto.Activa) await ValidarNombreLibreAsync(nombre, id, ct);

        categoria.Nombre = nombre;
        categoria.Activa = dto.Activa;
        await _db.SaveChangesAsync(ct);

        return new CategoriaDto(categoria.Id, categoria.Nombre, categoria.Activa);
    }

    private async Task ValidarNombreLibreAsync(string nombre, Guid? excepto, CancellationToken ct)
    {
        var normalizado = nombre.ToLower();
        var existe = await _db.Categorias.AnyAsync(
            c => c.Activa && c.Nombre.ToLower() == normalizado && c.Id != excepto, ct);
        if (existe) throw new CategoriaDuplicadaException(nombre);
    }
}
