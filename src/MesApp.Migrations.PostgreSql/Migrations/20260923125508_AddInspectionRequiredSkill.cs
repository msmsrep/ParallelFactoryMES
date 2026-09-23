using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddInspectionRequiredSkill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RequiredSkillId",
                table: "InspectionOrderItem",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequiredSkillId",
                table: "InspectionItems",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InspectionOrderItem_RequiredSkillId",
                table: "InspectionOrderItem",
                column: "RequiredSkillId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionItems_RequiredSkillId",
                table: "InspectionItems",
                column: "RequiredSkillId");

            migrationBuilder.AddForeignKey(
                name: "FK_InspectionItems_Skills_RequiredSkillId",
                table: "InspectionItems",
                column: "RequiredSkillId",
                principalTable: "Skills",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InspectionOrderItem_Skills_RequiredSkillId",
                table: "InspectionOrderItem",
                column: "RequiredSkillId",
                principalTable: "Skills",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InspectionItems_Skills_RequiredSkillId",
                table: "InspectionItems");

            migrationBuilder.DropForeignKey(
                name: "FK_InspectionOrderItem_Skills_RequiredSkillId",
                table: "InspectionOrderItem");

            migrationBuilder.DropIndex(
                name: "IX_InspectionOrderItem_RequiredSkillId",
                table: "InspectionOrderItem");

            migrationBuilder.DropIndex(
                name: "IX_InspectionItems_RequiredSkillId",
                table: "InspectionItems");

            migrationBuilder.DropColumn(
                name: "RequiredSkillId",
                table: "InspectionOrderItem");

            migrationBuilder.DropColumn(
                name: "RequiredSkillId",
                table: "InspectionItems");
        }
    }
}
