using Hermes.Application.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Hermes.Api.Middleware;

// La API nunca expone el detalle interno de una excepción no controlada: solo las
// DominioException (con .Status propio y mensaje pensado para mostrarse) devuelven su texto.
public class ManejadorGlobalDeExcepciones : IExceptionHandler
{
    private readonly ILogger<ManejadorGlobalDeExcepciones> _logger;

    public ManejadorGlobalDeExcepciones(ILogger<ManejadorGlobalDeExcepciones> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var (status, titulo, detalle) = exception switch
        {
            DominioException dominioEx => (dominioEx.Status, "Error de negocio", dominioEx.Message),
            _ when ErrorDeBaseConocido(exception) is { } conocido => conocido,
            _ => (StatusCodes.Status500InternalServerError, "Error interno", "Ocurrió un error inesperado. Si sigue pasando, avisanos."),
        };

        if (status == StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "Error no controlado en {Path}", httpContext.Request.Path);

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = titulo,
            Detail = detalle,
            Instance = httpContext.Request.Path,
        }, ct);

        return true;
    }

    // Red de seguridad: si una validación se escapó y Postgres rechazó el dato, es un problema
    // del dato enviado, no un fallo del servidor. Se identifica por el SQLSTATE.
    private static (int, string, string)? ErrorDeBaseConocido(Exception exception)
    {
        if (exception.GetBaseException() is not Npgsql.PostgresException pg) return null;

        return pg.SqlState switch
        {
            "23505" => (StatusCodes.Status409Conflict, "Error de negocio", "Ya existe un registro con esos datos."),
            "22001" => (StatusCodes.Status400BadRequest, "Solicitud inválida", "Algún texto supera el largo permitido."),
            "23503" or "23514" => (StatusCodes.Status400BadRequest, "Solicitud inválida", "Algún dato está fuera de rango o hace referencia a algo que no existe."),
            "40P01" or "40001" => (StatusCodes.Status409Conflict, "Error de negocio", "Otra persona estaba modificando lo mismo en este momento. Intentá de nuevo."),
            _ => null,
        };
    }
}
