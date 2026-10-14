using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hermes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarProductos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "productos",
                schema: "hermes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tienda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    precio = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    costo = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    categoria_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visible_en_tienda = table.Column<bool>(type: "boolean", nullable: false),
                    archivado = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_productos", x => x.id);
                    table.CheckConstraint("ck_producto_costo", "costo >= 0");
                    table.CheckConstraint("ck_producto_precio", "precio > 0");
                    table.ForeignKey(
                        name: "fk_productos_categorias_categoria_id",
                        column: x => x.categoria_id,
                        principalSchema: "hermes",
                        principalTable: "categorias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_productos_tiendas_tienda_id",
                        column: x => x.tienda_id,
                        principalSchema: "hermes",
                        principalTable: "tiendas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "producto_fotos",
                schema: "hermes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tienda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    imagen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_producto_fotos", x => x.id);
                    table.ForeignKey(
                        name: "fk_producto_fotos_imagenes_imagen_id",
                        column: x => x.imagen_id,
                        principalSchema: "hermes",
                        principalTable: "imagenes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_producto_fotos_productos_producto_id",
                        column: x => x.producto_id,
                        principalSchema: "hermes",
                        principalTable: "productos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_producto_fotos_tiendas_tienda_id",
                        column: x => x.tienda_id,
                        principalSchema: "hermes",
                        principalTable: "tiendas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "variantes",
                schema: "hermes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tienda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    color = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    talla = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_variantes", x => x.id);
                    table.ForeignKey(
                        name: "fk_variantes_productos_producto_id",
                        column: x => x.producto_id,
                        principalSchema: "hermes",
                        principalTable: "productos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_variantes_tiendas_tienda_id",
                        column: x => x.tienda_id,
                        principalSchema: "hermes",
                        principalTable: "tiendas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_producto_fotos_imagen_id",
                schema: "hermes",
                table: "producto_fotos",
                column: "imagen_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_producto_fotos_producto_id_orden",
                schema: "hermes",
                table: "producto_fotos",
                columns: new[] { "producto_id", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_producto_fotos_tienda_id",
                schema: "hermes",
                table: "producto_fotos",
                column: "tienda_id");

            migrationBuilder.CreateIndex(
                name: "ix_productos_categoria_id",
                schema: "hermes",
                table: "productos",
                column: "categoria_id");

            migrationBuilder.CreateIndex(
                name: "ix_productos_tienda_id_archivado",
                schema: "hermes",
                table: "productos",
                columns: new[] { "tienda_id", "archivado" });

            migrationBuilder.CreateIndex(
                name: "ix_variantes_producto_id_color_talla",
                schema: "hermes",
                table: "variantes",
                columns: new[] { "producto_id", "color", "talla" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_variantes_tienda_id",
                schema: "hermes",
                table: "variantes",
                column: "tienda_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "producto_fotos",
                schema: "hermes");

            migrationBuilder.DropTable(
                name: "variantes",
                schema: "hermes");

            migrationBuilder.DropTable(
                name: "productos",
                schema: "hermes");
        }
    }
}
