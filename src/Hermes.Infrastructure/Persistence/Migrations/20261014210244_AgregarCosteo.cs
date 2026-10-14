using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hermes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarCosteo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "costeos",
                schema: "hermes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tienda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    empaque = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    mano_de_obra = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    costo_fijo = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    margen_objetivo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_costeos", x => x.id);
                    table.CheckConstraint("ck_costeo_componentes", "material >= 0 and empaque >= 0 and mano_de_obra >= 0 and costo_fijo >= 0");
                    table.CheckConstraint("ck_costeo_margen", "margen_objetivo between 1 and 90");
                    table.ForeignKey(
                        name: "fk_costeos_productos_producto_id",
                        column: x => x.producto_id,
                        principalSchema: "hermes",
                        principalTable: "productos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_costeos_tiendas_tienda_id",
                        column: x => x.tienda_id,
                        principalSchema: "hermes",
                        principalTable: "tiendas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_costeos_producto_id",
                schema: "hermes",
                table: "costeos",
                column: "producto_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_costeos_tienda_id",
                schema: "hermes",
                table: "costeos",
                column: "tienda_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "costeos",
                schema: "hermes");
        }
    }
}
