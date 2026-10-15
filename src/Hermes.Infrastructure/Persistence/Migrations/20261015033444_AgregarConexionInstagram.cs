using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hermes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarConexionInstagram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "enlace",
                schema: "hermes",
                table: "borradores",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "instagram_media_id",
                schema: "hermes",
                table: "borradores",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "borrador_fotos",
                schema: "hermes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tienda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    borrador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    imagen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_borrador_fotos", x => x.id);
                    table.ForeignKey(
                        name: "fk_borrador_fotos_borradores_borrador_id",
                        column: x => x.borrador_id,
                        principalSchema: "hermes",
                        principalTable: "borradores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_borrador_fotos_imagenes_imagen_id",
                        column: x => x.imagen_id,
                        principalSchema: "hermes",
                        principalTable: "imagenes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_borrador_fotos_tiendas_tienda_id",
                        column: x => x.tienda_id,
                        principalSchema: "hermes",
                        principalTable: "tiendas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "instagram_conexiones",
                schema: "hermes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tienda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ig_usuario_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    usuario = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    token_cifrado = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    token_vence = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    token_renovado = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    conectada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ultima_sincronizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ultimo_error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    necesita_reconectar = table.Column<bool>(type: "boolean", nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instagram_conexiones", x => x.id);
                    table.ForeignKey(
                        name: "fk_instagram_conexiones_tiendas_tienda_id",
                        column: x => x.tienda_id,
                        principalSchema: "hermes",
                        principalTable: "tiendas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "instagram_publicaciones",
                schema: "hermes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tienda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    enlace = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    publicada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    importada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    productos = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instagram_publicaciones", x => x.id);
                    table.ForeignKey(
                        name: "fk_instagram_publicaciones_tiendas_tienda_id",
                        column: x => x.tienda_id,
                        principalSchema: "hermes",
                        principalTable: "tiendas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_borrador_fotos_borrador_id",
                schema: "hermes",
                table: "borrador_fotos",
                column: "borrador_id");

            migrationBuilder.CreateIndex(
                name: "ix_borrador_fotos_imagen_id",
                schema: "hermes",
                table: "borrador_fotos",
                column: "imagen_id");

            migrationBuilder.CreateIndex(
                name: "ix_borrador_fotos_tienda_id",
                schema: "hermes",
                table: "borrador_fotos",
                column: "tienda_id");

            migrationBuilder.CreateIndex(
                name: "ix_instagram_conexiones_tienda_id",
                schema: "hermes",
                table: "instagram_conexiones",
                column: "tienda_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_instagram_publicaciones_tienda_id_media_id",
                schema: "hermes",
                table: "instagram_publicaciones",
                columns: new[] { "tienda_id", "media_id" },
                unique: true);

            // Segunda barrera multi-tienda (Row Level Security, ver AgregarSeguridadPorFila) en las tablas nuevas de una tienda.
            foreach (var tabla in TablasDeTienda)
            {
                migrationBuilder.Sql($"ALTER TABLE hermes.{tabla} ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"""
                    CREATE POLICY por_tienda ON hermes.{tabla}
                        USING (hermes.tienda_permitida(tienda_id))
                        WITH CHECK (hermes.tienda_permitida(tienda_id));
                    """);
            }
        }

        private static readonly string[] TablasDeTienda = ["borrador_fotos", "instagram_conexiones", "instagram_publicaciones"];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var tabla in TablasDeTienda)
                migrationBuilder.Sql($"DROP POLICY IF EXISTS por_tienda ON hermes.{tabla};");
            migrationBuilder.DropTable(
                name: "borrador_fotos",
                schema: "hermes");

            migrationBuilder.DropTable(
                name: "instagram_conexiones",
                schema: "hermes");

            migrationBuilder.DropTable(
                name: "instagram_publicaciones",
                schema: "hermes");

            migrationBuilder.DropColumn(
                name: "enlace",
                schema: "hermes",
                table: "borradores");

            migrationBuilder.DropColumn(
                name: "instagram_media_id",
                schema: "hermes",
                table: "borradores");
        }
    }
}
