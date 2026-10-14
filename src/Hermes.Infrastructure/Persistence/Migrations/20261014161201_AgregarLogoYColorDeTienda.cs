using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hermes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarLogoYColorDeTienda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "logo_imagen_id",
                schema: "hermes",
                table: "tiendas",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tiendas_logo_imagen_id",
                schema: "hermes",
                table: "tiendas",
                column: "logo_imagen_id");

            migrationBuilder.AddForeignKey(
                name: "fk_tiendas_imagenes_logo_imagen_id",
                schema: "hermes",
                table: "tiendas",
                column: "logo_imagen_id",
                principalSchema: "hermes",
                principalTable: "imagenes",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tiendas_imagenes_logo_imagen_id",
                schema: "hermes",
                table: "tiendas");

            migrationBuilder.DropIndex(
                name: "ix_tiendas_logo_imagen_id",
                schema: "hermes",
                table: "tiendas");

            migrationBuilder.DropColumn(
                name: "logo_imagen_id",
                schema: "hermes",
                table: "tiendas");
        }
    }
}
