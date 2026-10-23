namespace Hermes.Domain.Seguridad;

// Códigos de permiso. Viajan como claim "permiso" en el JWT y se usan directo como nombre de policy en los Controllers
// (`[Authorize(Policy = Permisos.VentasRegistrar)]`): no hace falta registrar la policy a mano, la resuelve
// PermissionPolicyProvider. Varios códigos separados por «|» alcanzan con tener alguno.
//
// Agregar uno nuevo: la constante + una línea en `Catalogo` (y, si corresponde, en los roles por defecto). Los roles que ya
// existen NO lo reciben solos: el dueño se lo da desde "Roles" (el dueño siempre tiene TODO lo que haya en el catálogo).
public static class Permisos
{
    public const string InicioVer = "inicio.ver";

    public const string ProductosVer = "productos.ver";
    public const string ProductosCrear = "productos.crear";
    public const string ProductosEditar = "productos.editar";
    public const string ProductosArchivar = "productos.archivar";

    // Lo que le cuesta cada cosa y lo que se gana: costo, margen, ganancia, costeo y precio sugerido. Sin este permiso la API
    // no manda esos números (el front además no los muestra).
    public const string CostosVer = "costos.ver";
    public const string CostosEditar = "costos.editar";

    public const string CategoriasVer = "categorias.ver";
    public const string CategoriasGestionar = "categorias.gestionar";

    public const string StockVer = "stock.ver";
    public const string StockAjustar = "stock.ajustar";

    // Sin VentasVer una persona ve SOLO las ventas que registró ella.
    public const string VentasVer = "ventas.ver";
    public const string VentasRegistrar = "ventas.registrar";
    public const string VentasAnular = "ventas.anular";

    public const string ComprasVer = "compras.ver";
    public const string ComprasGestionar = "compras.gestionar";
    public const string ProveedoresVer = "proveedores.ver";
    public const string ProveedoresGestionar = "proveedores.gestionar";

    public const string GastosVer = "gastos.ver";
    public const string GastosGestionar = "gastos.gestionar";

    public const string PedidosVer = "pedidos.ver";
    public const string PedidosGestionar = "pedidos.gestionar";

    public const string InstagramGestionar = "instagram.gestionar";

    public const string TiendaGestionar = "tienda.gestionar";
    public const string PlanGestionar = "plan.gestionar";

    public const string EquipoGestionar = "equipo.gestionar";
    public const string RolesGestionar = "roles.gestionar";

    public sealed record Permiso(string Codigo, string Modulo, string Descripcion);

    public static readonly IReadOnlyList<Permiso> Catalogo =
    [
        new(InicioVer, "Inicio", "Ver el Inicio con los números del negocio"),

        new(ProductosVer, "Productos", "Ver el catálogo de productos"),
        new(ProductosCrear, "Productos", "Crear productos nuevos"),
        new(ProductosEditar, "Productos", "Editar productos y sus fotos"),
        new(ProductosArchivar, "Productos", "Archivar y desarchivar productos"),

        new(CostosVer, "Costos", "Ver costos, márgenes y ganancias"),
        new(CostosEditar, "Costos", "Cambiar costos y calcular precios (costeo)"),

        new(CategoriasVer, "Categorías", "Ver las categorías"),
        new(CategoriasGestionar, "Categorías", "Crear y editar categorías"),

        new(StockVer, "Stock", "Ver el stock"),
        new(StockAjustar, "Stock", "Anotar lo que llegó y corregir el stock"),

        new(VentasVer, "Ventas", "Ver todas las ventas (sin esto, solo las propias)"),
        new(VentasRegistrar, "Ventas", "Registrar ventas"),
        new(VentasAnular, "Ventas", "Anular ventas"),

        new(ComprasVer, "Compras", "Ver las compras"),
        new(ComprasGestionar, "Compras", "Registrar, recibir, pagar y anular compras"),
        new(ProveedoresVer, "Proveedores", "Ver los proveedores"),
        new(ProveedoresGestionar, "Proveedores", "Crear y editar proveedores"),

        new(GastosVer, "Gastos", "Ver los gastos"),
        new(GastosGestionar, "Gastos", "Anotar y anular gastos"),

        new(PedidosVer, "Pedidos", "Ver los pedidos de la tienda"),
        new(PedidosGestionar, "Pedidos", "Confirmar, entregar y cancelar pedidos"),

        new(InstagramGestionar, "Instagram", "Conectar Instagram y revisar sus publicaciones"),

        new(TiendaGestionar, "Mi tienda", "Cambiar los datos, el diseño y la dirección de la tienda"),
        new(PlanGestionar, "Mi plan", "Ver y cambiar el plan"),

        new(EquipoGestionar, "Equipo", "Agregar personas al negocio y cambiarles el rol"),
        new(RolesGestionar, "Equipo", "Crear roles y elegir sus permisos"),
    ];

    private static readonly HashSet<string> Conocidos = Catalogo.Select(p => p.Codigo).ToHashSet(StringComparer.Ordinal);

    public static IReadOnlyList<string> Todos { get; } = Catalogo.Select(p => p.Codigo).ToList();

    public static bool Existe(string codigo) => Conocidos.Contains(codigo);
}

// Los roles con los que arranca cada negocio la primera vez que se abre "Equipo" (después el dueño los edita, los desactiva o
// crea otros). El DUEÑO no es un rol guardado: es quien figura como dueño de la tienda y tiene siempre todo.
public static class RolesPorDefecto
{
    public sealed record Definicion(string Nombre, string Descripcion, IReadOnlyList<string> Permisos);

    public static readonly IReadOnlyList<Definicion> Todos =
    [
        new("Encargado", "Maneja el día a día: productos, stock, ventas, compras y gastos. No cambia la tienda, el plan ni el equipo.",
        [
            Permisos.InicioVer,
            Permisos.ProductosVer, Permisos.ProductosCrear, Permisos.ProductosEditar, Permisos.ProductosArchivar,
            Permisos.CostosVer, Permisos.CostosEditar,
            Permisos.CategoriasVer, Permisos.CategoriasGestionar,
            Permisos.StockVer, Permisos.StockAjustar,
            Permisos.VentasVer, Permisos.VentasRegistrar, Permisos.VentasAnular,
            Permisos.ComprasVer, Permisos.ComprasGestionar, Permisos.ProveedoresVer, Permisos.ProveedoresGestionar,
            Permisos.GastosVer, Permisos.GastosGestionar,
            Permisos.PedidosVer, Permisos.PedidosGestionar,
            Permisos.InstagramGestionar,
        ]),
        new("Vendedor", "Registra ventas y mira productos y stock. No ve costos ni ganancias, y solo ve sus propias ventas.",
        [
            Permisos.ProductosVer, Permisos.CategoriasVer, Permisos.StockVer,
            Permisos.VentasRegistrar, Permisos.PedidosVer,
        ]),
        new("Solo ver", "Mira cómo va el negocio (incluidas las ganancias) sin poder cambiar nada.",
        [
            Permisos.InicioVer, Permisos.ProductosVer, Permisos.CostosVer, Permisos.CategoriasVer, Permisos.StockVer,
            Permisos.VentasVer, Permisos.ComprasVer, Permisos.ProveedoresVer, Permisos.GastosVer, Permisos.PedidosVer,
        ]),
    ];
}
