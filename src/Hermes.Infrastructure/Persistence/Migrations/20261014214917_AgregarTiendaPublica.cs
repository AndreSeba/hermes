using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hermes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarTiendaPublica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "fondo",
                schema: "hermes",
                table: "tiendas",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Crema"); // las tiendas que ya existían quedan con el fondo de siempre (EF generó "")

            migrationBuilder.AddColumn<bool>(
                name: "mostrar_disponibilidad",
                schema: "hermes",
                table: "tiendas",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "mostrar_precios",
                schema: "hermes",
                table: "tiendas",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "pedidos_por_whatsapp",
                schema: "hermes",
                table: "tiendas",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fondo",
                schema: "hermes",
                table: "tiendas");

            migrationBuilder.DropColumn(
                name: "mostrar_disponibilidad",
                schema: "hermes",
                table: "tiendas");

            migrationBuilder.DropColumn(
                name: "mostrar_precios",
                schema: "hermes",
                table: "tiendas");

            migrationBuilder.DropColumn(
                name: "pedidos_por_whatsapp",
                schema: "hermes",
                table: "tiendas");
        }
    }
}
