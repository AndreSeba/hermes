using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hermes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarGastos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gastos",
                schema: "hermes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tienda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concepto = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    monto = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    clave_idempotencia = table.Column<Guid>(type: "uuid", nullable: false),
                    anulado = table.Column<bool>(type: "boolean", nullable: false),
                    anulado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gastos", x => x.id);
                    table.CheckConstraint("ck_gasto_monto", "monto > 0");
                    table.ForeignKey(
                        name: "fk_gastos_tiendas_tienda_id",
                        column: x => x.tienda_id,
                        principalSchema: "hermes",
                        principalTable: "tiendas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gastos_tienda_id_clave_idempotencia",
                schema: "hermes",
                table: "gastos",
                columns: new[] { "tienda_id", "clave_idempotencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gastos_tienda_id_fecha",
                schema: "hermes",
                table: "gastos",
                columns: new[] { "tienda_id", "fecha" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gastos",
                schema: "hermes");
        }
    }
}
