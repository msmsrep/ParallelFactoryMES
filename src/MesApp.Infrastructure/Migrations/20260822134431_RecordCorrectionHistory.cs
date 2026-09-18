using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RecordCorrectionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InspectionResultCorrections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InspectionResultId = table.Column<int>(type: "INTEGER", nullable: false),
                    InspectionOrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    BeforeMeasuredValue = table.Column<decimal>(type: "TEXT", nullable: true),
                    BeforeTextValue = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    BeforeJudgment = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    AfterMeasuredValue = table.Column<decimal>(type: "TEXT", nullable: true),
                    AfterTextValue = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AfterJudgment = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CorrectedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CorrectedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionResultCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InspectionResultCorrections_AspNetUsers_CorrectedByUserId",
                        column: x => x.CorrectedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_InspectionResultCorrections_InspectionResults_InspectionResultId",
                        column: x => x.InspectionResultId,
                        principalTable: "InspectionResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductionRecordCorrections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductionRecordId = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkOrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    BeforeGoodQuantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    BeforeDefectQuantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    AfterGoodQuantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    AfterDefectQuantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CorrectedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CorrectedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionRecordCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionRecordCorrections_AspNetUsers_CorrectedByUserId",
                        column: x => x.CorrectedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ProductionRecordCorrections_ProductionRecords_ProductionRecordId",
                        column: x => x.ProductionRecordId,
                        principalTable: "ProductionRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductionRecordCorrections_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionResultCorrections_CorrectedByUserId",
                table: "InspectionResultCorrections",
                column: "CorrectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionResultCorrections_InspectionOrderId_Id",
                table: "InspectionResultCorrections",
                columns: new[] { "InspectionOrderId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionResultCorrections_InspectionResultId",
                table: "InspectionResultCorrections",
                column: "InspectionResultId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRecordCorrections_CorrectedByUserId",
                table: "ProductionRecordCorrections",
                column: "CorrectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRecordCorrections_ProductionRecordId",
                table: "ProductionRecordCorrections",
                column: "ProductionRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRecordCorrections_WorkOrderId_Id",
                table: "ProductionRecordCorrections",
                columns: new[] { "WorkOrderId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InspectionResultCorrections");

            migrationBuilder.DropTable(
                name: "ProductionRecordCorrections");
        }
    }
}
