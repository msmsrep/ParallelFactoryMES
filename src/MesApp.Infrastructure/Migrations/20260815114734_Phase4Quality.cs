using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase4Quality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InspectionOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrderNo = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    TargetLotId = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetWorkOrderId = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    RequestedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    OverallJudgment = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    JudgedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    JudgedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ApprovedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InspectionOrders_AspNetUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_InspectionOrders_Lots_TargetLotId",
                        column: x => x.TargetLotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InspectionOrders_WorkOrders_TargetWorkOrderId",
                        column: x => x.TargetWorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShipmentJudgments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JudgmentNo = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    LotId = table.Column<int>(type: "INTEGER", nullable: true),
                    ShippingOrderId = table.Column<int>(type: "INTEGER", nullable: true),
                    Result = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    JudgedByUserId = table.Column<string>(type: "TEXT", nullable: false),
                    JudgedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ApprovedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentJudgments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentJudgments_AspNetUsers_JudgedByUserId",
                        column: x => x.JudgedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentJudgments_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentJudgments_ShippingOrders_ShippingOrderId",
                        column: x => x.ShippingOrderId,
                        principalTable: "ShippingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InspectionOrderItem",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InspectionOrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    InspectionItemId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionOrderItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InspectionOrderItem_InspectionItems_InspectionItemId",
                        column: x => x.InspectionItemId,
                        principalTable: "InspectionItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InspectionOrderItem_InspectionOrders_InspectionOrderId",
                        column: x => x.InspectionOrderId,
                        principalTable: "InspectionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InspectionResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InspectionOrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    InspectionItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    SampleNo = table.Column<int>(type: "INTEGER", nullable: false),
                    MeasuredValue = table.Column<decimal>(type: "TEXT", nullable: true),
                    TextValue = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Judgment = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    InspectedByUserId = table.Column<string>(type: "TEXT", nullable: false),
                    InspectedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CorrectionNote = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InspectionResults_AspNetUsers_InspectedByUserId",
                        column: x => x.InspectedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InspectionResults_InspectionItems_InspectionItemId",
                        column: x => x.InspectionItemId,
                        principalTable: "InspectionItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InspectionResults_InspectionOrders_InspectionOrderId",
                        column: x => x.InspectionOrderId,
                        principalTable: "InspectionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NonconformanceReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReportNo = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    LotId = table.Column<int>(type: "INTEGER", nullable: true),
                    WorkOrderId = table.Column<int>(type: "INTEGER", nullable: true),
                    InspectionOrderId = table.Column<int>(type: "INTEGER", nullable: true),
                    Content = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    CauseCategory = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CauseDetail = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    ActionInstruction = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ActionInstructedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    ActionInstructedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReworkOrderId = table.Column<int>(type: "INTEGER", nullable: true),
                    ActionRecord = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ActionCompletedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    ActionCompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ApprovedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ReportedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NonconformanceReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NonconformanceReports_AspNetUsers_ReportedByUserId",
                        column: x => x.ReportedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_NonconformanceReports_InspectionOrders_InspectionOrderId",
                        column: x => x.InspectionOrderId,
                        principalTable: "InspectionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_NonconformanceReports_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NonconformanceReports_ManufacturingOrders_ReworkOrderId",
                        column: x => x.ReworkOrderId,
                        principalTable: "ManufacturingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_NonconformanceReports_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionOrderItem_InspectionItemId",
                table: "InspectionOrderItem",
                column: "InspectionItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionOrderItem_InspectionOrderId_InspectionItemId",
                table: "InspectionOrderItem",
                columns: new[] { "InspectionOrderId", "InspectionItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InspectionOrders_OrderNo",
                table: "InspectionOrders",
                column: "OrderNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InspectionOrders_RequestedByUserId",
                table: "InspectionOrders",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionOrders_Status",
                table: "InspectionOrders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionOrders_TargetLotId",
                table: "InspectionOrders",
                column: "TargetLotId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionOrders_TargetWorkOrderId",
                table: "InspectionOrders",
                column: "TargetWorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionResults_InspectedByUserId",
                table: "InspectionResults",
                column: "InspectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionResults_InspectionItemId",
                table: "InspectionResults",
                column: "InspectionItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionResults_InspectionOrderId",
                table: "InspectionResults",
                column: "InspectionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_NonconformanceReports_InspectionOrderId",
                table: "NonconformanceReports",
                column: "InspectionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_NonconformanceReports_LotId",
                table: "NonconformanceReports",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_NonconformanceReports_ReportedByUserId",
                table: "NonconformanceReports",
                column: "ReportedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_NonconformanceReports_ReportNo",
                table: "NonconformanceReports",
                column: "ReportNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NonconformanceReports_ReworkOrderId",
                table: "NonconformanceReports",
                column: "ReworkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_NonconformanceReports_Status",
                table: "NonconformanceReports",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_NonconformanceReports_WorkOrderId",
                table: "NonconformanceReports",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentJudgments_JudgedByUserId",
                table: "ShipmentJudgments",
                column: "JudgedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentJudgments_JudgmentNo",
                table: "ShipmentJudgments",
                column: "JudgmentNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentJudgments_LotId",
                table: "ShipmentJudgments",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentJudgments_ShippingOrderId",
                table: "ShipmentJudgments",
                column: "ShippingOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InspectionOrderItem");

            migrationBuilder.DropTable(
                name: "InspectionResults");

            migrationBuilder.DropTable(
                name: "NonconformanceReports");

            migrationBuilder.DropTable(
                name: "ShipmentJudgments");

            migrationBuilder.DropTable(
                name: "InspectionOrders");
        }
    }
}
