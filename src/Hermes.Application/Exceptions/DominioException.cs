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

public class VentaNoEncontradaException : DominioException
{
    public override int Status => 404;

    public VentaNoEncontradaException(Guid id)
        : base($"No existe la venta {id}.") { }
}

public class VentaYaAnuladaException : DominioException
{
    public override int Status => 409;

    public VentaYaAnuladaException()
        : base("Esa venta ya estaba anulada.") { }
}

public class ProveedorNoEncontradoException : DominioException
{
    public override int Status => 404;

    public ProveedorNoEncontradoException(Guid id)
        : base($"No existe el proveedor {id}.") { }
}

public class ProveedorDuplicadoException : DominioException
{
    public override int Status => 409;

    public ProveedorDuplicadoException(string nombre)
        : base($"Ya tenés un proveedor llamado '{nombre}'.") { }
}

public class CompraNoEncontradaException : DominioException
{
    public override int Status => 404;

    public CompraNoEncontradaException(Guid id)
        : base($"No existe la compra {id}.") { }
}

// Una compra anulada ya no se puede recibir ni pagar; una recibida/pagada no se vuelve a recibir/pagar.
public class CompraEstadoInvalidoException : DominioException
{
    public override int Status => 409;

    public CompraEstadoInvalidoException(string message) : base(message) { }
}

public class GastoNoEncontradoException : DominioException
{
    public override int Status => 404;

    public GastoNoEncontradoException(Guid id)
        : base($"No existe el gasto {id}.") { }
}

public class GastoYaAnuladoException : DominioException
{
    public override int Status => 409;

    public GastoYaAnuladoException()
        : base("Ese gasto ya estaba anulado.") { }
}

// Para el público no se distingue "no existe" de "está apagada" ni de "ese producto no se muestra": todo es 404.
public class TiendaPublicaNoEncontradaException : DominioException
{
    public override int Status => 404;

    public TiendaPublicaNoEncontradaException()
        : base("No encontramos esta tienda.") { }
}

public class BorradorNoEncontradoException : DominioException
{
    public override int Status => 404;

    public BorradorNoEncontradoException(Guid id)
        : base($"No existe ese producto en la bandeja ({id}).") { }
}

public class BorradorYaResueltoException : DominioException
{
    public override int Status => 409;

    public BorradorYaResueltoException()
        : base("Ese producto ya se publicó o se descartó.") { }
}

public class StockInsuficienteException : DominioException
{
    public override int Status => 409;

    public StockInsuficienteException(string producto, int disponible, int pedido)
        : base($"No alcanza el stock de {producto}: hay {disponible} y se necesitan {pedido}.") { }
}

public class ProductoSinControlDeStockException : DominioException
{
    public override int Status => 409;

    public ProductoSinControlDeStockException(string producto)
        : base($"{producto} no lleva control de stock. Activalo en el producto para contar las unidades.") { }
}

public class VarianteNoEncontradaException : DominioException
{
    public override int Status => 404;

    public VarianteNoEncontradaException(Guid id)
        : base($"No existe la variante {id}.") { }
}

public class ImagenInvalidaException : DominioException
{
    public override int Status => 400;

    public ImagenInvalidaException(string message) : base(message) { }
}

public class ImagenNoEncontradaException : DominioException
{
    public override int Status => 404;

    public ImagenNoEncontradaException(Guid id)
        : base($"No existe la imagen {id}.") { }
}

public class ProductoNoEncontradoException : DominioException
{
    public override int Status => 404;

    public ProductoNoEncontradoException(Guid id)
        : base($"No existe el producto {id}.") { }
}

public class ImagenEnUsoException : DominioException
{
    public override int Status => 409;

    public ImagenEnUsoException()
        : base("Esa foto ya se usa en otro lado. Subila de nuevo para usarla acá.") { }
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
