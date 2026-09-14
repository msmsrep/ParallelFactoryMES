using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkCenterToEquipmentAndLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WorkCenterId",
                table: "Locations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkCenterId",
                table: "Equipments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Locations_WorkCenterId",
                table: "Locations",
                column: "WorkCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_Equipments_WorkCenterId",
                table: "Equipments",
                column: "WorkCenterId");

            migrationBuilder.AddForeignKey(
                name: "FK_Equipments_WorkCenters_WorkCenterId",
                table: "Equipments",
                column: "WorkCenterId",
                principalTable: "WorkCenters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Locations_WorkCenters_WorkCenterId",
                table: "Locations",
                column: "WorkCenterId",
                principalTable: "WorkCenters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Equipments_WorkCenters_WorkCenterId",
                table: "Equipments");

            migrationBuilder.DropForeignKey(
                name: "FK_Locations_WorkCenters_WorkCenterId",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_Locations_WorkCenterId",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_Equipments_WorkCenterId",
                table: "Equipments");

            migrationBuilder.DropColumn(
                name: "WorkCenterId",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "WorkCenterId",
                table: "Equipments");
        }
    }
}
