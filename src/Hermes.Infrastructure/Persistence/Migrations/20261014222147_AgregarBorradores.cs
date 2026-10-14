using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hermes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarBorradores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "borradores",
                schema: "hermes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tienda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    origen = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    texto = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    precio = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    precio_anterior = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    colores = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    tallas = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    stock = table.Column<int>(type: "integer", nullable: true),
                    categoria = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    descripcion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    avisos = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resuelto_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_borradores", x => x.id);
                    table.ForeignKey(
                        name: "fk_borradores_productos_producto_id",
                        column: x => x.producto_id,
                        principalSchema: "hermes",
                        principalTable: "productos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_borradores_tiendas_tienda_id",
                        column: x => x.tienda_id,
                        principalSchema: "hermes",
                        principalTable: "tiendas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_borradores_producto_id",
                schema: "hermes",
                table: "borradores",
                column: "producto_id");

            migrationBuilder.CreateIndex(
                name: "ix_borradores_tienda_id_estado_id",
                schema: "hermes",
                table: "borradores",
                columns: new[] { "tienda_id", "estado", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "borradores",
                schema: "hermes");
        }
    }
}
