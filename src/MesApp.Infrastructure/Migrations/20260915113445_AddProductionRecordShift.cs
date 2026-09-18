using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionRecordShift : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ShiftId",
                table: "ProductionRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRecords_ShiftId",
                table: "ProductionRecords",
                column: "ShiftId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductionRecords_Shifts_ShiftId",
                table: "ProductionRecords",
                column: "ShiftId",
                principalTable: "Shifts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductionRecords_Shifts_ShiftId",
                table: "ProductionRecords");

            migrationBuilder.DropIndex(
                name: "IX_ProductionRecords_ShiftId",
                table: "ProductionRecords");

            migrationBuilder.DropColumn(
                name: "ShiftId",
                table: "ProductionRecords");
        }
    }
}
