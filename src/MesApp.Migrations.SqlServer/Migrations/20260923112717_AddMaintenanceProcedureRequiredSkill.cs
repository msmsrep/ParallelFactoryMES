using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceProcedureRequiredSkill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RequiredSkillId",
                table: "MaintenanceProcedures",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceProcedures_RequiredSkillId",
                table: "MaintenanceProcedures",
                column: "RequiredSkillId");

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceProcedures_Skills_RequiredSkillId",
                table: "MaintenanceProcedures",
                column: "RequiredSkillId",
                principalTable: "Skills",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceProcedures_Skills_RequiredSkillId",
                table: "MaintenanceProcedures");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceProcedures_RequiredSkillId",
                table: "MaintenanceProcedures");

            migrationBuilder.DropColumn(
                name: "RequiredSkillId",
                table: "MaintenanceProcedures");
        }
    }
}
