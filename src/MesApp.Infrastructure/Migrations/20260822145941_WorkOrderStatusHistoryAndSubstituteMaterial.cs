using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WorkOrderStatusHistoryAndSubstituteMaterial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSubstitute",
                table: "MaterialConsumptions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SubstituteReason",
                table: "MaterialConsumptions",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAlternative",
                table: "ManufacturingOrderMaterials",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsAlternative",
                table: "BomItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "WorkOrderStatusHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkOrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    FromStatus = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ToStatus = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ChangedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderStatusHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderStatusHistories_AspNetUsers_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkOrderStatusHistories_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderStatusHistories_ChangedByUserId",
                table: "WorkOrderStatusHistories",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderStatusHistories_WorkOrderId_Id",
                table: "WorkOrderStatusHistories",
                columns: new[] { "WorkOrderId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkOrderStatusHistories");

            migrationBuilder.DropColumn(
                name: "IsSubstitute",
                table: "MaterialConsumptions");

            migrationBuilder.DropColumn(
                name: "SubstituteReason",
                table: "MaterialConsumptions");

            migrationBuilder.DropColumn(
                name: "IsAlternative",
                table: "ManufacturingOrderMaterials");

            migrationBuilder.DropColumn(
                name: "IsAlternative",
                table: "BomItems");
        }
    }
}
