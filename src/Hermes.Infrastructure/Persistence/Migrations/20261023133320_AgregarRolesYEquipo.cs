using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hermes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarRolesYEquipo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "usuario_id",
                schema: "hermes",
                table: "ventas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "debe_cambiar_password",
                schema: "hermes",
                table: "usuarios",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "max_usuarios",
                schema: "hermes",
                table: "planes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "hermes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tienda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                    table.ForeignKey(
                        name: "fk_roles_tiendas_tienda_id",
                        column: x => x.tienda_id,
                        principalSchema: "hermes",
                        principalTable: "tiendas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "miembros",
                schema: "hermes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tienda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rol_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_miembros", x => x.id);
                    table.ForeignKey(
                        name: "fk_miembros_roles_rol_id",
                        column: x => x.rol_id,
                        principalSchema: "hermes",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_miembros_tiendas_tienda_id",
                        column: x => x.tienda_id,
                        principalSchema: "hermes",
                        principalTable: "tiendas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_miembros_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalSchema: "hermes",
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rol_permisos",
                schema: "hermes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tienda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rol_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rol_permisos", x => x.id);
                    table.ForeignKey(
                        name: "fk_rol_permisos_roles_rol_id",
                        column: x => x.rol_id,
                        principalSchema: "hermes",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_rol_permisos_tiendas_tienda_id",
                        column: x => x.tienda_id,
                        principalSchema: "hermes",
                        principalTable: "tiendas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                schema: "hermes",
                table: "planes",
                keyColumn: "id",
                keyValue: new Guid("0194f000-0000-7000-8000-000000000001"),
                column: "max_usuarios",
                value: 1);

            migrationBuilder.UpdateData(
                schema: "hermes",
                table: "planes",
                keyColumn: "id",
                keyValue: new Guid("0194f000-0000-7000-8000-000000000002"),
                column: "max_usuarios",
                value: 3);

            migrationBuilder.UpdateData(
                schema: "hermes",
                table: "planes",
                keyColumn: "id",
                keyValue: new Guid("0194f000-0000-7000-8000-000000000003"),
                column: "max_usuarios",
                value: 10);

            migrationBuilder.CreateIndex(
                name: "ix_ventas_tienda_id_usuario_id_fecha",
                schema: "hermes",
                table: "ventas",
                columns: new[] { "tienda_id", "usuario_id", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_ventas_usuario_id",
                schema: "hermes",
                table: "ventas",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_miembros_rol_id",
                schema: "hermes",
                table: "miembros",
                column: "rol_id");

            migrationBuilder.CreateIndex(
                name: "ix_miembros_tienda_id_usuario_id",
                schema: "hermes",
                table: "miembros",
                columns: new[] { "tienda_id", "usuario_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_miembros_usuario_id",
                schema: "hermes",
                table: "miembros",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_rol_permisos_rol_id_codigo",
                schema: "hermes",
                table: "rol_permisos",
                columns: new[] { "rol_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rol_permisos_tienda_id",
                schema: "hermes",
                table: "rol_permisos",
                column: "tienda_id");

            migrationBuilder.CreateIndex(
                name: "ix_roles_tienda_id_nombre",
                schema: "hermes",
                table: "roles",
                columns: new[] { "tienda_id", "nombre" },
                unique: true,
                filter: "activo");

            migrationBuilder.AddForeignKey(
                name: "fk_ventas_usuarios_usuario_id",
                schema: "hermes",
                table: "ventas",
                column: "usuario_id",
                principalSchema: "hermes",
                principalTable: "usuarios",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Segunda barrera multi-tienda (ver Persistence/SeguridadPorFila.cs): las tablas nuevas de una tienda llevan su política
            // por_tienda, igual que las de la migración AgregarSeguridadPorFila.
            foreach (var tabla in new[] { "miembros", "rol_permisos", "roles" })
            {
                migrationBuilder.Sql($"ALTER TABLE hermes.{tabla} ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"""
                    CREATE POLICY por_tienda ON hermes.{tabla}
                        USING (hermes.tienda_permitida(tienda_id))
                        WITH CHECK (hermes.tienda_permitida(tienda_id));
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_ventas_usuarios_usuario_id",
                schema: "hermes",
                table: "ventas");

            migrationBuilder.DropTable(
                name: "miembros",
                schema: "hermes");

            migrationBuilder.DropTable(
                name: "rol_permisos",
                schema: "hermes");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "hermes");

            migrationBuilder.DropIndex(
                name: "ix_ventas_tienda_id_usuario_id_fecha",
                schema: "hermes",
                table: "ventas");

            migrationBuilder.DropIndex(
                name: "ix_ventas_usuario_id",
                schema: "hermes",
                table: "ventas");

            migrationBuilder.DropColumn(
                name: "usuario_id",
                schema: "hermes",
                table: "ventas");

            migrationBuilder.DropColumn(
                name: "debe_cambiar_password",
                schema: "hermes",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "max_usuarios",
                schema: "hermes",
                table: "planes");
        }
    }
}
