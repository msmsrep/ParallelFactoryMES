using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDefectReasonMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DefectReasons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DefectReasons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductionDefects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductionRecordId = table.Column<int>(type: "INTEGER", nullable: false),
                    DefectReasonId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionDefects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionDefects_DefectReasons_DefectReasonId",
                        column: x => x.DefectReasonId,
                        principalTable: "DefectReasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionDefects_ProductionRecords_ProductionRecordId",
                        column: x => x.ProductionRecordId,
                        principalTable: "ProductionRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DefectReasons_Code",
                table: "DefectReasons",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionDefects_DefectReasonId",
                table: "ProductionDefects",
                column: "DefectReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionDefects_ProductionRecordId",
                table: "ProductionDefects",
                column: "ProductionRecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductionDefects");

            migrationBuilder.DropTable(
                name: "DefectReasons");
        }
    }
}
