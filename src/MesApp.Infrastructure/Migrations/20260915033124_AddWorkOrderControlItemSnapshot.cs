using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrderControlItemSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkOrderControlItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkOrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    ControlItemId = table.Column<int>(type: "INTEGER", nullable: true),
                    ItemCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ItemName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    ItemVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetValue = table.Column<decimal>(type: "TEXT", nullable: true),
                    LowerLimit = table.Column<decimal>(type: "TEXT", nullable: true),
                    UpperLimit = table.Column<decimal>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderControlItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderControlItems_ControlItems_ControlItemId",
                        column: x => x.ControlItemId,
                        principalTable: "ControlItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderControlItems_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderControlItems_ControlItemId",
                table: "WorkOrderControlItems",
                column: "ControlItemId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderControlItems_WorkOrderId",
                table: "WorkOrderControlItems",
                column: "WorkOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkOrderControlItems");
        }
    }
}
