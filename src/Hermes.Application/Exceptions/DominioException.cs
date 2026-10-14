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

// Mismo mensaje para email inexistente y contraseña incorrecta: no revelar qué cuentas existen.
public class CredencialesInvalidasException : DominioException
{
    public override int Status => 401;

    public CredencialesInvalidasException()
        : base("Email o contraseña incorrectos.") { }
}

public class LoginBloqueadoException : DominioException
{
    public override int Status => 429;

    public LoginBloqueadoException(int minutos)
        : base($"Demasiados intentos fallidos. Probá de nuevo en {minutos} minutos.") { }
}

public class SesionInvalidaException : DominioException
{
    public override int Status => 401;

    public SesionInvalidaException()
        : base("Tu sesión venció. Iniciá sesión de nuevo.") { }
}

public class EmailYaRegistradoException : DominioException
{
    public override int Status => 409;

    public EmailYaRegistradoException()
        : base("Ya existe una cuenta con ese email. ¿Querés iniciar sesión?") { }
}

public class SlugNoDisponibleException : DominioException
{
    public override int Status => 409;

    public SlugNoDisponibleException(string slug)
        : base($"La dirección '{slug}' ya está en uso. Probá con otra.") { }
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
