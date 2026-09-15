using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddControlItemDeviationToDataRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDeviation",
                table: "ProductionDataRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NumericValue",
                table: "ProductionDataRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkOrderControlItemId",
                table: "ProductionDataRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionDataRecords_WorkOrderControlItemId",
                table: "ProductionDataRecords",
                column: "WorkOrderControlItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductionDataRecords_WorkOrderControlItems_WorkOrderControlItemId",
                table: "ProductionDataRecords",
                column: "WorkOrderControlItemId",
                principalTable: "WorkOrderControlItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductionDataRecords_WorkOrderControlItems_WorkOrderControlItemId",
                table: "ProductionDataRecords");

            migrationBuilder.DropIndex(
                name: "IX_ProductionDataRecords_WorkOrderControlItemId",
                table: "ProductionDataRecords");

            migrationBuilder.DropColumn(
                name: "IsDeviation",
                table: "ProductionDataRecords");

            migrationBuilder.DropColumn(
                name: "NumericValue",
                table: "ProductionDataRecords");

            migrationBuilder.DropColumn(
                name: "WorkOrderControlItemId",
                table: "ProductionDataRecords");
        }
    }
}
