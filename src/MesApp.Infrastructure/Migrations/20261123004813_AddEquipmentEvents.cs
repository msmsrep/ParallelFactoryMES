using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CollectsEvents",
                table: "Equipments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "EquipmentLogs",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.CreateTable(
                name: "EquipmentEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EquipmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    EventId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    AlarmCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    WorkOrderNo = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    OperationSeq = table.Column<int>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Result = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    RejectReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    EquipmentLogId = table.Column<int>(type: "INTEGER", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReceivedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentEvents_EquipmentLogs_EquipmentLogId",
                        column: x => x.EquipmentLogId,
                        principalTable: "EquipmentLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentEvents_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentEvents_EquipmentId_EventId",
                table: "EquipmentEvents",
                columns: new[] { "EquipmentId", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentEvents_EquipmentId_OccurredAt",
                table: "EquipmentEvents",
                columns: new[] { "EquipmentId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentEvents_EquipmentLogId",
                table: "EquipmentEvents",
                column: "EquipmentLogId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EquipmentEvents");

            migrationBuilder.DropColumn(
                name: "CollectsEvents",
                table: "Equipments");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "EquipmentLogs");
        }
    }
}
