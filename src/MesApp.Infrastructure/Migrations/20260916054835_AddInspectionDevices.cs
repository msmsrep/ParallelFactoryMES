using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInspectionDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InspectionDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SerialNo = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Location = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CalibratedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CalibrationDueOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CalibrationCycleDays = table.Column<int>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InspectionDeviceCalibrations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InspectionDeviceId = table.Column<int>(type: "INTEGER", nullable: false),
                    CalibratedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    NextDueOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Result = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PerformedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionDeviceCalibrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InspectionDeviceCalibrations_AspNetUsers_PerformedByUserId",
                        column: x => x.PerformedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InspectionDeviceCalibrations_InspectionDevices_InspectionDeviceId",
                        column: x => x.InspectionDeviceId,
                        principalTable: "InspectionDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionDeviceCalibrations_InspectionDeviceId_CalibratedOn",
                table: "InspectionDeviceCalibrations",
                columns: new[] { "InspectionDeviceId", "CalibratedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionDeviceCalibrations_PerformedByUserId",
                table: "InspectionDeviceCalibrations",
                column: "PerformedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionDevices_Code",
                table: "InspectionDevices",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InspectionDeviceCalibrations");

            migrationBuilder.DropTable(
                name: "InspectionDevices");
        }
    }
}
