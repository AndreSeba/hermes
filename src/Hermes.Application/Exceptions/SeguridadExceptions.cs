namespace Hermes.Application.Exceptions;

// Roles y equipo. Los mensajes se muestran tal cual en el panel.

public class RolNoEncontradoException : DominioException
{
    public override int Status => 404;

    public RolNoEncontradoException(Guid id)
        : base($"No existe el rol {id}.") { }
}

public class NombreRolDuplicadoException : DominioException
{
    public override int Status => 409;

    public NombreRolDuplicadoException(string nombre)
        : base($"Ya tenés un rol llamado '{nombre}'.") { }
}

public class PermisoInvalidoException : DominioException
{
    public override int Status => 400;

    public PermisoInvalidoException(string codigo)
        : base($"El permiso '{codigo}' no existe.") { }
}

// No se desactiva un rol que todavía tienen personas activas: se les cambia el rol primero.
public class RolConPersonasException : DominioException
{
    public override int Status => 409;

    public RolConPersonasException(string nombre, int personas)
        : base($"El rol '{nombre}' lo tienen {personas} persona(s) activa(s). Cambiales el rol antes de desactivarlo.") { }
}

public class MiembroNoEncontradoException : DominioException
{
    public override int Status => 404;

    public MiembroNoEncontradoException(Guid id)
        : base("No existe esa persona en tu equipo.") { }
}

// Un permiso o un rol que la persona que lo da no tiene: nadie le puede dar a otro más de lo que tiene él.
public class SinPermisoException : DominioException
{
    public override int Status => 403;

    public SinPermisoException(string message) : base(message) { }
}

// Cada persona usa su propio correo: el que se quiere dar de alta ya tiene cuenta (en este u otro negocio).
public class CorreoDeEquipoEnUsoException : DominioException
{
    public override int Status => 409;

    public CorreoDeEquipoEnUsoException(string email)
        : base($"El correo {email} ya tiene una cuenta. Cada persona necesita un correo propio que no esté usado en otro negocio.") { }
}

// Lo que no se puede hacer con el dueño, ni con uno mismo (quedarse sin acceso).
public class OperacionNoPermitidaException : DominioException
{
    public override int Status => 409;

    public OperacionNoPermitidaException(string message) : base(message) { }
}
