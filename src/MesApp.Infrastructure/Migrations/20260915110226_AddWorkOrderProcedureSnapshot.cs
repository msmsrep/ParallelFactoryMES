using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrderProcedureSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WorkProcedureId",
                table: "WorkOrders",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkProcedureVersion",
                table: "WorkOrders",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_WorkProcedureId",
                table: "WorkOrders",
                column: "WorkProcedureId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_WorkProcedures_WorkProcedureId",
                table: "WorkOrders",
                column: "WorkProcedureId",
                principalTable: "WorkProcedures",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_WorkProcedures_WorkProcedureId",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_WorkProcedureId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "WorkProcedureId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "WorkProcedureVersion",
                table: "WorkOrders");
        }
    }
}
