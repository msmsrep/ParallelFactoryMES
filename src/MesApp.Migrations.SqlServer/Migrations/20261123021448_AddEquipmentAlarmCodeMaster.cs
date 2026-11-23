using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentAlarmCodeMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EquipmentAlarmCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentId = table.Column<int>(type: "int", nullable: true),
                    AlarmCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StopCause = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentAlarmCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentAlarmCodes_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentAlarmCodes_EquipmentId_AlarmCode",
                table: "EquipmentAlarmCodes",
                columns: new[] { "EquipmentId", "AlarmCode" },
                unique: true,
                filter: "[EquipmentId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EquipmentAlarmCodes");
        }
    }
}
