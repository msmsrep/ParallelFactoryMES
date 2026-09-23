using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MesApp.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class LinkControlItemsToRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ControlItems_Processes_TargetProcessId",
                table: "ControlItems");

            migrationBuilder.DropForeignKey(
                name: "FK_ControlItems_Products_TargetProductId",
                table: "ControlItems");

            migrationBuilder.DropIndex(
                name: "IX_ControlItems_TargetProcessId",
                table: "ControlItems");

            migrationBuilder.DropIndex(
                name: "IX_ControlItems_TargetProductId",
                table: "ControlItems");

            migrationBuilder.DropColumn(
                name: "TargetProcessId",
                table: "ControlItems");

            migrationBuilder.DropColumn(
                name: "TargetProductId",
                table: "ControlItems");

            migrationBuilder.CreateTable(
                name: "RoutingControlItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoutingId = table.Column<int>(type: "integer", nullable: false),
                    ControlItemId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutingControlItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoutingControlItems_ControlItems_ControlItemId",
                        column: x => x.ControlItemId,
                        principalTable: "ControlItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoutingControlItems_Routings_RoutingId",
                        column: x => x.RoutingId,
                        principalTable: "Routings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoutingControlItems_ControlItemId",
                table: "RoutingControlItems",
                column: "ControlItemId");

            migrationBuilder.CreateIndex(
                name: "IX_RoutingControlItems_RoutingId_ControlItemId",
                table: "RoutingControlItems",
                columns: new[] { "RoutingId", "ControlItemId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoutingControlItems");

            migrationBuilder.AddColumn<int>(
                name: "TargetProcessId",
                table: "ControlItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetProductId",
                table: "ControlItems",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ControlItems_TargetProcessId",
                table: "ControlItems",
                column: "TargetProcessId");

            migrationBuilder.CreateIndex(
                name: "IX_ControlItems_TargetProductId",
                table: "ControlItems",
                column: "TargetProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_ControlItems_Processes_TargetProcessId",
                table: "ControlItems",
                column: "TargetProcessId",
                principalTable: "Processes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ControlItems_Products_TargetProductId",
                table: "ControlItems",
                column: "TargetProductId",
                principalTable: "Products",
                principalColumn: "Id");
        }
    }
}
