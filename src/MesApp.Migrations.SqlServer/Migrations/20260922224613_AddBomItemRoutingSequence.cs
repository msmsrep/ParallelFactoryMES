using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddBomItemRoutingSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RoutingSequence",
                table: "ManufacturingOrderMaterials",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RoutingSequence",
                table: "BomItems",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RoutingSequence",
                table: "ManufacturingOrderMaterials");

            migrationBuilder.DropColumn(
                name: "RoutingSequence",
                table: "BomItems");
        }
    }
}
