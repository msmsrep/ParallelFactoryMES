using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInspectionResultDevice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InspectionDeviceId",
                table: "InspectionResults",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InspectionResults_InspectionDeviceId",
                table: "InspectionResults",
                column: "InspectionDeviceId");

            migrationBuilder.AddForeignKey(
                name: "FK_InspectionResults_InspectionDevices_InspectionDeviceId",
                table: "InspectionResults",
                column: "InspectionDeviceId",
                principalTable: "InspectionDevices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InspectionResults_InspectionDevices_InspectionDeviceId",
                table: "InspectionResults");

            migrationBuilder.DropIndex(
                name: "IX_InspectionResults_InspectionDeviceId",
                table: "InspectionResults");

            migrationBuilder.DropColumn(
                name: "InspectionDeviceId",
                table: "InspectionResults");
        }
    }
}
