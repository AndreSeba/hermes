namespace Hermes.Application.Exceptions;

// Errores de negocio esperables: el ManejadorGlobalDeExcepciones los traduce a un
// ProblemDetails con este Status y el mensaje real. Cualquier otra excepción sale como
// 500 genérico, nunca con su .Message crudo.
public abstract class DominioException : Exception
{
    public abstract int Status { get; }

    protected DominioException(string message) : base(message) { }
}

public class ValidacionException : DominioException
{
    public override int Status => 400;

    public ValidacionException(string message) : base(message) { }
}

public class SinTiendaEnSesionException : DominioException
{
    public override int Status => 403;

    public SinTiendaEnSesionException()
        : base("La sesión no tiene una tienda asociada.") { }
}

public class CategoriaNoEncontradaException : DominioException
{
    public override int Status => 404;

    public CategoriaNoEncontradaException(Guid id)
        : base($"No existe la categoría {id}.") { }
}

public class CategoriaDuplicadaException : DominioException
{
    public override int Status => 409;

    public CategoriaDuplicadaException(string nombre)
        : base($"Ya tenés una categoría llamada '{nombre}'.") { }
}
