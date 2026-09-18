using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddToolIssues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ToolIssues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ToolId = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkOrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    AllocatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    AllocatedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    IssuedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    IssuedToUserId = table.Column<string>(type: "TEXT", nullable: true),
                    ReturnedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReturnedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ToolIssues_AspNetUsers_IssuedToUserId",
                        column: x => x.IssuedToUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ToolIssues_Tools_ToolId",
                        column: x => x.ToolId,
                        principalTable: "Tools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ToolIssues_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ToolIssues_IssuedToUserId",
                table: "ToolIssues",
                column: "IssuedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ToolIssues_ToolId_Status",
                table: "ToolIssues",
                columns: new[] { "ToolId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ToolIssues_WorkOrderId",
                table: "ToolIssues",
                column: "WorkOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ToolIssues");
        }
    }
}
