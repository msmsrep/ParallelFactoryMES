using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkCenterToRoutingWorkOrderAndUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WorkCenterId",
                table: "WorkOrders",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkCenterId",
                table: "Routings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkCenterId",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_WorkCenterId",
                table: "WorkOrders",
                column: "WorkCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_Routings_WorkCenterId",
                table: "Routings",
                column: "WorkCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_WorkCenterId",
                table: "AspNetUsers",
                column: "WorkCenterId");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_WorkCenters_WorkCenterId",
                table: "AspNetUsers",
                column: "WorkCenterId",
                principalTable: "WorkCenters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Routings_WorkCenters_WorkCenterId",
                table: "Routings",
                column: "WorkCenterId",
                principalTable: "WorkCenters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_WorkCenters_WorkCenterId",
                table: "WorkOrders",
                column: "WorkCenterId",
                principalTable: "WorkCenters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_WorkCenters_WorkCenterId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Routings_WorkCenters_WorkCenterId",
                table: "Routings");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_WorkCenters_WorkCenterId",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_WorkCenterId",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_Routings_WorkCenterId",
                table: "Routings");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_WorkCenterId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "WorkCenterId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "WorkCenterId",
                table: "Routings");

            migrationBuilder.DropColumn(
                name: "WorkCenterId",
                table: "AspNetUsers");
        }
    }
}
