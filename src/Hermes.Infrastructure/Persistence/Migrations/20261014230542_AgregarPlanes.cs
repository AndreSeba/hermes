using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Hermes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarPlanes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "es_administrador",
                schema: "hermes",
                table: "usuarios",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "planes",
                schema: "hermes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    precio_mensual = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    max_productos = table.Column<int>(type: "integer", nullable: true),
                    max_almacenamiento_mb = table.Column<int>(type: "integer", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_planes", x => x.id);
                    table.CheckConstraint("ck_plan_precio", "precio_mensual >= 0");
                });

            migrationBuilder.CreateTable(
                name: "pagos_plan",
                schema: "hermes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tienda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    meses = table.Column<int>(type: "integer", nullable: false),
                    monto = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    metodo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    comprobante_imagen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    motivo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    creado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resuelto_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resuelto_por_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pagos_plan", x => x.id);
                    table.CheckConstraint("ck_pago_plan_meses", "meses > 0");
                    table.CheckConstraint("ck_pago_plan_monto", "monto > 0");
                    table.ForeignKey(
                        name: "fk_pagos_plan_imagenes_comprobante_imagen_id",
                        column: x => x.comprobante_imagen_id,
                        principalSchema: "hermes",
                        principalTable: "imagenes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_pagos_plan_planes_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "hermes",
                        principalTable: "planes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pagos_plan_tiendas_tienda_id",
                        column: x => x.tienda_id,
                        principalSchema: "hermes",
                        principalTable: "tiendas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "suscripciones",
                schema: "hermes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tienda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    desde = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    hasta = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    origen = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    pago_id = table.Column<Guid>(type: "uuid", nullable: true),
                    asignada_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    creada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suscripciones", x => x.id);
                    table.ForeignKey(
                        name: "fk_suscripciones_pagos_plan_pago_id",
                        column: x => x.pago_id,
                        principalSchema: "hermes",
                        principalTable: "pagos_plan",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_suscripciones_planes_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "hermes",
                        principalTable: "planes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_suscripciones_tiendas_tienda_id",
                        column: x => x.tienda_id,
                        principalSchema: "hermes",
                        principalTable: "tiendas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "hermes",
                table: "planes",
                columns: new[] { "id", "activo", "codigo", "max_almacenamiento_mb", "max_productos", "nombre", "orden", "precio_mensual" },
                values: new object[,]
                {
                    { new Guid("0194f000-0000-7000-8000-000000000001"), true, "gratis", 100, 15, "Gratis", 1, 0m },
                    { new Guid("0194f000-0000-7000-8000-000000000002"), true, "emprendedor", 1000, 150, "Emprendedor", 2, 39m },
                    { new Guid("0194f000-0000-7000-8000-000000000003"), true, "negocio", 5000, null, "Negocio", 3, 89m }
                });

            migrationBuilder.CreateIndex(
                name: "ix_pagos_plan_comprobante_imagen_id",
                schema: "hermes",
                table: "pagos_plan",
                column: "comprobante_imagen_id");

            migrationBuilder.CreateIndex(
                name: "ix_pagos_plan_estado_id",
                schema: "hermes",
                table: "pagos_plan",
                columns: new[] { "estado", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_pagos_plan_plan_id",
                schema: "hermes",
                table: "pagos_plan",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ux_pago_plan_pendiente",
                schema: "hermes",
                table: "pagos_plan",
                column: "tienda_id",
                unique: true,
                filter: "estado = 'Pendiente'");

            migrationBuilder.CreateIndex(
                name: "ix_planes_codigo",
                schema: "hermes",
                table: "planes",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_suscripciones_pago_id",
                schema: "hermes",
                table: "suscripciones",
                column: "pago_id");

            migrationBuilder.CreateIndex(
                name: "ix_suscripciones_plan_id",
                schema: "hermes",
                table: "suscripciones",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_suscripciones_tienda_id_desde",
                schema: "hermes",
                table: "suscripciones",
                columns: new[] { "tienda_id", "desde" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "suscripciones",
                schema: "hermes");

            migrationBuilder.DropTable(
                name: "pagos_plan",
                schema: "hermes");

            migrationBuilder.DropTable(
                name: "planes",
                schema: "hermes");

            migrationBuilder.DropColumn(
                name: "es_administrador",
                schema: "hermes",
                table: "usuarios");
        }
    }
}
