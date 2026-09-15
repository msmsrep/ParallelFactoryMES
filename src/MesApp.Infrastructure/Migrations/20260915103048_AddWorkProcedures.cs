using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkProcedures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WorkProcedureId",
                table: "Routings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkProcedures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProcedureNo = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Steps = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkProcedures", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Routings_WorkProcedureId",
                table: "Routings",
                column: "WorkProcedureId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkProcedures_ProcedureNo",
                table: "WorkProcedures",
                column: "ProcedureNo",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Routings_WorkProcedures_WorkProcedureId",
                table: "Routings",
                column: "WorkProcedureId",
                principalTable: "WorkProcedures",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Routings_WorkProcedures_WorkProcedureId",
                table: "Routings");

            migrationBuilder.DropTable(
                name: "WorkProcedures");

            migrationBuilder.DropIndex(
                name: "IX_Routings_WorkProcedureId",
                table: "Routings");

            migrationBuilder.DropColumn(
                name: "WorkProcedureId",
                table: "Routings");
        }
    }
}
