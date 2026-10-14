using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hermes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarVentas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "venta_id",
                schema: "hermes",
                table: "movimientos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ventas",
                schema: "hermes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tienda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    metodo_pago = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cliente = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    clave_idempotencia = table.Column<Guid>(type: "uuid", nullable: false),
                    anulada = table.Column<bool>(type: "boolean", nullable: false),
                    anulada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ventas", x => x.id);
                    table.ForeignKey(
                        name: "fk_ventas_tiendas_tienda_id",
                        column: x => x.tienda_id,
                        principalSchema: "hermes",
                        principalTable: "tiendas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "venta_lineas",
                schema: "hermes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tienda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    venta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    descripcion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cantidad = table.Column<int>(type: "integer", nullable: false),
                    precio_unitario = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    costo_unitario = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    desconto_stock = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_venta_lineas", x => x.id);
                    table.CheckConstraint("ck_venta_linea_cantidad", "cantidad > 0");
                    table.CheckConstraint("ck_venta_linea_costo", "costo_unitario >= 0");
                    table.CheckConstraint("ck_venta_linea_precio", "precio_unitario > 0");
                    table.ForeignKey(
                        name: "fk_venta_lineas_productos_producto_id",
                        column: x => x.producto_id,
                        principalSchema: "hermes",
                        principalTable: "productos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_venta_lineas_tiendas_tienda_id",
                        column: x => x.tienda_id,
                        principalSchema: "hermes",
                        principalTable: "tiendas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_venta_lineas_variantes_variante_id",
                        column: x => x.variante_id,
                        principalSchema: "hermes",
                        principalTable: "variantes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_venta_lineas_ventas_venta_id",
                        column: x => x.venta_id,
                        principalSchema: "hermes",
                        principalTable: "ventas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_venta_id",
                schema: "hermes",
                table: "movimientos",
                column: "venta_id");

            migrationBuilder.CreateIndex(
                name: "ix_venta_lineas_producto_id",
                schema: "hermes",
                table: "venta_lineas",
                column: "producto_id");

            migrationBuilder.CreateIndex(
                name: "ix_venta_lineas_tienda_id",
                schema: "hermes",
                table: "venta_lineas",
                column: "tienda_id");

            migrationBuilder.CreateIndex(
                name: "ix_venta_lineas_variante_id",
                schema: "hermes",
                table: "venta_lineas",
                column: "variante_id");

            migrationBuilder.CreateIndex(
                name: "ix_venta_lineas_venta_id",
                schema: "hermes",
                table: "venta_lineas",
                column: "venta_id");

            migrationBuilder.CreateIndex(
                name: "ix_ventas_tienda_id_clave_idempotencia",
                schema: "hermes",
                table: "ventas",
                columns: new[] { "tienda_id", "clave_idempotencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ventas_tienda_id_fecha",
                schema: "hermes",
                table: "ventas",
                columns: new[] { "tienda_id", "fecha" });

            migrationBuilder.AddForeignKey(
                name: "fk_movimientos_ventas_venta_id",
                schema: "hermes",
                table: "movimientos",
                column: "venta_id",
                principalSchema: "hermes",
                principalTable: "ventas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_movimientos_ventas_venta_id",
                schema: "hermes",
                table: "movimientos");

            migrationBuilder.DropTable(
                name: "venta_lineas",
                schema: "hermes");

            migrationBuilder.DropTable(
                name: "ventas",
                schema: "hermes");

            migrationBuilder.DropIndex(
                name: "ix_movimientos_venta_id",
                schema: "hermes",
                table: "movimientos");

            migrationBuilder.DropColumn(
                name: "venta_id",
                schema: "hermes",
                table: "movimientos");
        }
    }
}
