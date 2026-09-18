using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddControlItemMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ControlItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    TargetProductId = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetProcessId = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetValue = table.Column<decimal>(type: "TEXT", nullable: true),
                    LowerLimit = table.Column<decimal>(type: "TEXT", nullable: true),
                    UpperLimit = table.Column<decimal>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ControlItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ControlItems_Processes_TargetProcessId",
                        column: x => x.TargetProcessId,
                        principalTable: "Processes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ControlItems_Products_TargetProductId",
                        column: x => x.TargetProductId,
                        principalTable: "Products",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ControlItems_Code",
                table: "ControlItems",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ControlItems_TargetProcessId",
                table: "ControlItems",
                column: "TargetProcessId");

            migrationBuilder.CreateIndex(
                name: "IX_ControlItems_TargetProductId",
                table: "ControlItems",
                column: "TargetProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ControlItems");
        }
    }
}
