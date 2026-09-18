using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrderToEquipmentLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WorkOrderId",
                table: "EquipmentLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentLogs_WorkOrderId",
                table: "EquipmentLogs",
                column: "WorkOrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_EquipmentLogs_WorkOrders_WorkOrderId",
                table: "EquipmentLogs",
                column: "WorkOrderId",
                principalTable: "WorkOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EquipmentLogs_WorkOrders_WorkOrderId",
                table: "EquipmentLogs");

            migrationBuilder.DropIndex(
                name: "IX_EquipmentLogs_WorkOrderId",
                table: "EquipmentLogs");

            migrationBuilder.DropColumn(
                name: "WorkOrderId",
                table: "EquipmentLogs");
        }
    }
}
