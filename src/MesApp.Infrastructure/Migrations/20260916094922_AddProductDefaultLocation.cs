using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductDefaultLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultLocationId",
                table: "Products",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_DefaultLocationId",
                table: "Products",
                column: "DefaultLocationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Locations_DefaultLocationId",
                table: "Products",
                column: "DefaultLocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_Locations_DefaultLocationId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_DefaultLocationId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "DefaultLocationId",
                table: "Products");
        }
    }
}
