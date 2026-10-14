using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hermes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarImagenes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "archivos",
                schema: "hermes",
                columns: table => new
                {
                    clave = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    datos = table.Column<byte[]>(type: "bytea", nullable: false),
                    content_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    bytes = table.Column<int>(type: "integer", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archivos", x => x.clave);
                });

            migrationBuilder.CreateTable(
                name: "imagenes",
                schema: "hermes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tienda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ancho_original = table.Column<int>(type: "integer", nullable: false),
                    alto_original = table.Column<int>(type: "integer", nullable: false),
                    bytes_guardados = table.Column<int>(type: "integer", nullable: false),
                    creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_imagenes", x => x.id);
                    table.ForeignKey(
                        name: "fk_imagenes_tiendas_tienda_id",
                        column: x => x.tienda_id,
                        principalSchema: "hermes",
                        principalTable: "tiendas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_imagenes_tienda_id",
                schema: "hermes",
                table: "imagenes",
                column: "tienda_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "archivos",
                schema: "hermes");

            migrationBuilder.DropTable(
                name: "imagenes",
                schema: "hermes");
        }
    }
}
