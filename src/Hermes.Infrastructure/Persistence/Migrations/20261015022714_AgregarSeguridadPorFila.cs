using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hermes.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Segunda barrera multi-tienda: Row Level Security en cada tabla de una tienda (ver Persistence/SeguridadPorFila.cs).
    /// La política deja ver y escribir solo las filas cuya tienda_id coincide con la variable de sesión `hermes.tienda`
    /// ("*" = todas, vacía = ninguna). No es FORCE: el dueño de las tablas (el que corre las migraciones) no queda limitado;
    /// la app en producción entra con un rol sin privilegios. TODA tabla nueva de una tienda tiene que sumarse acá (hay una
    /// prueba con Postgres que lo verifica contra el modelo).
    /// </summary>
    public partial class AgregarSeguridadPorFila : Migration
    {
        // Las tablas de las entidades que implementan IPerteneceATienda.
        internal static readonly string[] Tablas =
        [
            "borradores", "categorias", "compra_lineas", "compras", "costeos", "gastos", "imagenes", "movimientos",
            "pedido_lineas", "pedidos", "producto_fotos", "productos", "proveedores", "variantes", "venta_lineas", "ventas",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION hermes.tienda_permitida(tienda uuid) RETURNS boolean
                LANGUAGE sql STABLE AS $$
                    SELECT CASE coalesce(current_setting('hermes.tienda', true), '')
                        WHEN '*' THEN true
                        WHEN '' THEN false
                        ELSE tienda = current_setting('hermes.tienda', true)::uuid
                    END
                $$;
                """);
            foreach (var tabla in Tablas)
            {
                migrationBuilder.Sql($"ALTER TABLE hermes.{tabla} ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"""
                    CREATE POLICY por_tienda ON hermes.{tabla}
                        USING (hermes.tienda_permitida(tienda_id))
                        WITH CHECK (hermes.tienda_permitida(tienda_id));
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var tabla in Tablas)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS por_tienda ON hermes.{tabla};");
                migrationBuilder.Sql($"ALTER TABLE hermes.{tabla} DISABLE ROW LEVEL SECURITY;");
            }
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS hermes.tienda_permitida(uuid);");
        }
    }
}
