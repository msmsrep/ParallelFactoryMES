using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LotGenealogyAndStatusHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LotGenealogies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ParentLotId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChildLotId = table.Column<int>(type: "INTEGER", nullable: false),
                    RelationType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    PerformedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LotGenealogies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LotGenealogies_Lots_ChildLotId",
                        column: x => x.ChildLotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LotGenealogies_Lots_ParentLotId",
                        column: x => x.ParentLotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LotStatusHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LotId = table.Column<int>(type: "INTEGER", nullable: false),
                    FromStatus = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ToStatus = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    InspectionOrderId = table.Column<int>(type: "INTEGER", nullable: true),
                    NonconformanceReportId = table.Column<int>(type: "INTEGER", nullable: true),
                    ChangedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LotStatusHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LotStatusHistories_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LotGenealogies_ChildLotId",
                table: "LotGenealogies",
                column: "ChildLotId");

            migrationBuilder.CreateIndex(
                name: "IX_LotGenealogies_ParentLotId",
                table: "LotGenealogies",
                column: "ParentLotId");

            migrationBuilder.CreateIndex(
                name: "IX_LotStatusHistories_LotId_Id",
                table: "LotStatusHistories",
                columns: new[] { "LotId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LotGenealogies");

            migrationBuilder.DropTable(
                name: "LotStatusHistories");
        }
    }
}
