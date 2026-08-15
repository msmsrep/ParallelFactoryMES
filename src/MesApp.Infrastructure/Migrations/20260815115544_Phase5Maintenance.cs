using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase5Maintenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LifeResetAt",
                table: "Tools",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EquipmentLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EquipmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    StopCause = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    RecordedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentLogs_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MaintenancePlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EquipmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    PlanYear = table.Column<int>(type: "INTEGER", nullable: false),
                    ScheduledDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CycleDays = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenancePlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenancePlans_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceProcedures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProcedureNo = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TargetEquipmentId = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetToolId = table.Column<int>(type: "INTEGER", nullable: true),
                    Steps = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceProcedures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenanceProcedures_Equipments_TargetEquipmentId",
                        column: x => x.TargetEquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceProcedures_Tools_TargetToolId",
                        column: x => x.TargetToolId,
                        principalTable: "Tools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ToolUsages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ToolId = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkOrderId = table.Column<int>(type: "INTEGER", nullable: true),
                    UsageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UsageHours = table.Column<decimal>(type: "TEXT", nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RecordedByUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ToolUsages_Tools_ToolId",
                        column: x => x.ToolId,
                        principalTable: "Tools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ToolUsages_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrderNo = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    EquipmentId = table.Column<int>(type: "INTEGER", nullable: true),
                    ToolId = table.Column<int>(type: "INTEGER", nullable: true),
                    MaintenancePlanId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProcedureId = table.Column<int>(type: "INTEGER", nullable: true),
                    ScheduledDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    RequestType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenanceOrders_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MaintenanceOrders_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceOrders_MaintenancePlans_MaintenancePlanId",
                        column: x => x.MaintenancePlanId,
                        principalTable: "MaintenancePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MaintenanceOrders_MaintenanceProcedures_ProcedureId",
                        column: x => x.ProcedureId,
                        principalTable: "MaintenanceProcedures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MaintenanceOrders_Tools_ToolId",
                        column: x => x.ToolId,
                        principalTable: "Tools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MaintenanceOrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    PerformedByUserId = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PartsUsed = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Result = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenanceRecords_AspNetUsers_PerformedByUserId",
                        column: x => x.PerformedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceRecords_MaintenanceOrders_MaintenanceOrderId",
                        column: x => x.MaintenanceOrderId,
                        principalTable: "MaintenanceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentLogs_EquipmentId",
                table: "EquipmentLogs",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceOrders_CreatedByUserId",
                table: "MaintenanceOrders",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceOrders_EquipmentId",
                table: "MaintenanceOrders",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceOrders_MaintenancePlanId",
                table: "MaintenanceOrders",
                column: "MaintenancePlanId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceOrders_OrderNo",
                table: "MaintenanceOrders",
                column: "OrderNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceOrders_ProcedureId",
                table: "MaintenanceOrders",
                column: "ProcedureId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceOrders_Status",
                table: "MaintenanceOrders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceOrders_ToolId",
                table: "MaintenanceOrders",
                column: "ToolId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenancePlans_EquipmentId_PlanYear",
                table: "MaintenancePlans",
                columns: new[] { "EquipmentId", "PlanYear" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceProcedures_ProcedureNo",
                table: "MaintenanceProcedures",
                column: "ProcedureNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceProcedures_TargetEquipmentId",
                table: "MaintenanceProcedures",
                column: "TargetEquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceProcedures_TargetToolId",
                table: "MaintenanceProcedures",
                column: "TargetToolId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRecords_MaintenanceOrderId",
                table: "MaintenanceRecords",
                column: "MaintenanceOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRecords_PerformedByUserId",
                table: "MaintenanceRecords",
                column: "PerformedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ToolUsages_ToolId",
                table: "ToolUsages",
                column: "ToolId");

            migrationBuilder.CreateIndex(
                name: "IX_ToolUsages_WorkOrderId",
                table: "ToolUsages",
                column: "WorkOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EquipmentLogs");

            migrationBuilder.DropTable(
                name: "MaintenanceRecords");

            migrationBuilder.DropTable(
                name: "ToolUsages");

            migrationBuilder.DropTable(
                name: "MaintenanceOrders");

            migrationBuilder.DropTable(
                name: "MaintenancePlans");

            migrationBuilder.DropTable(
                name: "MaintenanceProcedures");

            migrationBuilder.DropColumn(
                name: "LifeResetAt",
                table: "Tools");
        }
    }
}
