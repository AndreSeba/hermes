using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hermes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarPedidos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pedidos",
                schema: "hermes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tienda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    clave_idempotencia = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    cliente_nombre = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    cliente_celular = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    entrega = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    direccion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    referencia = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    nota = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    estado = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    resuelto_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    motivo_cancelacion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    venta_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pedidos", x => x.id);
                    table.CheckConstraint("ck_pedido_numero", "numero > 0");
                    table.ForeignKey(
                        name: "fk_pedidos_tiendas_tienda_id",
                        column: x => x.tienda_id,
                        principalSchema: "hermes",
                        principalTable: "tiendas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pedidos_ventas_venta_id",
                        column: x => x.venta_id,
                        principalSchema: "hermes",
                        principalTable: "ventas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pedido_lineas",
                schema: "hermes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tienda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    descripcion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cantidad = table.Column<int>(type: "integer", nullable: false),
                    precio_unitario = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pedido_lineas", x => x.id);
                    table.CheckConstraint("ck_pedido_linea_cantidad", "cantidad > 0");
                    table.CheckConstraint("ck_pedido_linea_precio", "precio_unitario > 0");
                    table.ForeignKey(
                        name: "fk_pedido_lineas_pedidos_pedido_id",
                        column: x => x.pedido_id,
                        principalSchema: "hermes",
                        principalTable: "pedidos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pedido_lineas_productos_producto_id",
                        column: x => x.producto_id,
                        principalSchema: "hermes",
                        principalTable: "productos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pedido_lineas_tiendas_tienda_id",
                        column: x => x.tienda_id,
                        principalSchema: "hermes",
                        principalTable: "tiendas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pedido_lineas_variantes_variante_id",
                        column: x => x.variante_id,
                        principalSchema: "hermes",
                        principalTable: "variantes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pedido_lineas_pedido_id",
                schema: "hermes",
                table: "pedido_lineas",
                column: "pedido_id");

            migrationBuilder.CreateIndex(
                name: "ix_pedido_lineas_producto_id",
                schema: "hermes",
                table: "pedido_lineas",
                column: "producto_id");

            migrationBuilder.CreateIndex(
                name: "ix_pedido_lineas_tienda_id",
                schema: "hermes",
                table: "pedido_lineas",
                column: "tienda_id");

            migrationBuilder.CreateIndex(
                name: "ix_pedido_lineas_variante_id",
                schema: "hermes",
                table: "pedido_lineas",
                column: "variante_id");

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_tienda_id_clave_idempotencia",
                schema: "hermes",
                table: "pedidos",
                columns: new[] { "tienda_id", "clave_idempotencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_tienda_id_estado",
                schema: "hermes",
                table: "pedidos",
                columns: new[] { "tienda_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_tienda_id_numero",
                schema: "hermes",
                table: "pedidos",
                columns: new[] { "tienda_id", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_venta_id",
                schema: "hermes",
                table: "pedidos",
                column: "venta_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pedido_lineas",
                schema: "hermes");

            migrationBuilder.DropTable(
                name: "pedidos",
                schema: "hermes");
        }
    }
}
