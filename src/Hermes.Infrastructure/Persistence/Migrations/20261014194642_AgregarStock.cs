using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hermes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "controla_stock",
                schema: "hermes",
                table: "productos",
                type: "boolean",
                nullable: false,
                // A mano: los productos que ya existían SÍ llevan control de stock (EF pone false).
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "stock_minimo",
                schema: "hermes",
                table: "productos",
                type: "integer",
                nullable: false,
                // A mano: ReglaStock.MinimoPorDefecto (EF pone 0).
                defaultValue: 3);

            migrationBuilder.CreateTable(
                name: "movimientos",
                schema: "hermes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tienda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cantidad = table.Column<int>(type: "integer", nullable: false),
                    saldo = table.Column<int>(type: "integer", nullable: false),
                    nota = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    costo_unitario = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movimientos", x => x.id);
                    table.CheckConstraint("ck_movimiento_cantidad", "cantidad <> 0");
                    table.CheckConstraint("ck_movimiento_saldo", "saldo >= 0");
                    table.ForeignKey(
                        name: "fk_movimientos_tiendas_tienda_id",
                        column: x => x.tienda_id,
                        principalSchema: "hermes",
                        principalTable: "tiendas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_movimientos_variantes_variante_id",
                        column: x => x.variante_id,
                        principalSchema: "hermes",
                        principalTable: "variantes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_producto_stock_minimo",
                schema: "hermes",
                table: "productos",
                sql: "stock_minimo >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_tienda_id",
                schema: "hermes",
                table: "movimientos",
                column: "tienda_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_variante_id_id",
                schema: "hermes",
                table: "movimientos",
                columns: new[] { "variante_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "movimientos",
                schema: "hermes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_producto_stock_minimo",
                schema: "hermes",
                table: "productos");

            migrationBuilder.DropColumn(
                name: "controla_stock",
                schema: "hermes",
                table: "productos");

            migrationBuilder.DropColumn(
                name: "stock_minimo",
                schema: "hermes",
                table: "productos");
        }
    }
}
