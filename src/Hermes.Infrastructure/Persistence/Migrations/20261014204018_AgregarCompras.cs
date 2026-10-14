using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hermes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarCompras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "compra_id",
                schema: "hermes",
                table: "movimientos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "proveedores",
                schema: "hermes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tienda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    telefono = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    notas = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proveedores", x => x.id);
                    table.ForeignKey(
                        name: "fk_proveedores_tiendas_tienda_id",
                        column: x => x.tienda_id,
                        principalSchema: "hermes",
                        principalTable: "tiendas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "compras",
                schema: "hermes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tienda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    notas = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    clave_idempotencia = table.Column<Guid>(type: "uuid", nullable: false),
                    recibida = table.Column<bool>(type: "boolean", nullable: false),
                    recibida_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    pagada = table.Column<bool>(type: "boolean", nullable: false),
                    pagada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    anulada = table.Column<bool>(type: "boolean", nullable: false),
                    anulada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_compras", x => x.id);
                    table.ForeignKey(
                        name: "fk_compras_proveedores_proveedor_id",
                        column: x => x.proveedor_id,
                        principalSchema: "hermes",
                        principalTable: "proveedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_compras_tiendas_tienda_id",
                        column: x => x.tienda_id,
                        principalSchema: "hermes",
                        principalTable: "tiendas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "compra_lineas",
                schema: "hermes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tienda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    compra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    descripcion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cantidad = table.Column<int>(type: "integer", nullable: false),
                    costo_unitario = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    sumo_stock = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_compra_lineas", x => x.id);
                    table.CheckConstraint("ck_compra_linea_cantidad", "cantidad > 0");
                    table.CheckConstraint("ck_compra_linea_costo", "costo_unitario > 0");
                    table.ForeignKey(
                        name: "fk_compra_lineas_compras_compra_id",
                        column: x => x.compra_id,
                        principalSchema: "hermes",
                        principalTable: "compras",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_compra_lineas_productos_producto_id",
                        column: x => x.producto_id,
                        principalSchema: "hermes",
                        principalTable: "productos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_compra_lineas_tiendas_tienda_id",
                        column: x => x.tienda_id,
                        principalSchema: "hermes",
                        principalTable: "tiendas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_compra_lineas_variantes_variante_id",
                        column: x => x.variante_id,
                        principalSchema: "hermes",
                        principalTable: "variantes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_compra_id",
                schema: "hermes",
                table: "movimientos",
                column: "compra_id");

            migrationBuilder.CreateIndex(
                name: "ix_compra_lineas_compra_id",
                schema: "hermes",
                table: "compra_lineas",
                column: "compra_id");

            migrationBuilder.CreateIndex(
                name: "ix_compra_lineas_producto_id",
                schema: "hermes",
                table: "compra_lineas",
                column: "producto_id");

            migrationBuilder.CreateIndex(
                name: "ix_compra_lineas_tienda_id",
                schema: "hermes",
                table: "compra_lineas",
                column: "tienda_id");

            migrationBuilder.CreateIndex(
                name: "ix_compra_lineas_variante_id",
                schema: "hermes",
                table: "compra_lineas",
                column: "variante_id");

            migrationBuilder.CreateIndex(
                name: "ix_compras_proveedor_id",
                schema: "hermes",
                table: "compras",
                column: "proveedor_id");

            migrationBuilder.CreateIndex(
                name: "ix_compras_tienda_id_clave_idempotencia",
                schema: "hermes",
                table: "compras",
                columns: new[] { "tienda_id", "clave_idempotencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_compras_tienda_id_fecha",
                schema: "hermes",
                table: "compras",
                columns: new[] { "tienda_id", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_proveedores_tienda_id_nombre",
                schema: "hermes",
                table: "proveedores",
                columns: new[] { "tienda_id", "nombre" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_movimientos_compras_compra_id",
                schema: "hermes",
                table: "movimientos",
                column: "compra_id",
                principalSchema: "hermes",
                principalTable: "compras",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_movimientos_compras_compra_id",
                schema: "hermes",
                table: "movimientos");

            migrationBuilder.DropTable(
                name: "compra_lineas",
                schema: "hermes");

            migrationBuilder.DropTable(
                name: "compras",
                schema: "hermes");

            migrationBuilder.DropTable(
                name: "proveedores",
                schema: "hermes");

            migrationBuilder.DropIndex(
                name: "ix_movimientos_compra_id",
                schema: "hermes",
                table: "movimientos");

            migrationBuilder.DropColumn(
                name: "compra_id",
                schema: "hermes",
                table: "movimientos");
        }
    }
}
