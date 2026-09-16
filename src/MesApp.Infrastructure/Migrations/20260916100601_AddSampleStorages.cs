using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSampleStorages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SampleStorages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SampleNo = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    LotId = table.Column<int>(type: "INTEGER", nullable: false),
                    InspectionOrderId = table.Column<int>(type: "INTEGER", nullable: true),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    StorageLocationId = table.Column<int>(type: "INTEGER", nullable: false),
                    CollectedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    RetainUntil = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CollectedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    ClosedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ClosedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SampleStorages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SampleStorages_InspectionOrders_InspectionOrderId",
                        column: x => x.InspectionOrderId,
                        principalTable: "InspectionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SampleStorages_Locations_StorageLocationId",
                        column: x => x.StorageLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SampleStorages_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SampleStorages_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SampleStorages_InspectionOrderId",
                table: "SampleStorages",
                column: "InspectionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SampleStorages_LotId",
                table: "SampleStorages",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_SampleStorages_ProductId",
                table: "SampleStorages",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SampleStorages_SampleNo",
                table: "SampleStorages",
                column: "SampleNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SampleStorages_Status_RetainUntil",
                table: "SampleStorages",
                columns: new[] { "Status", "RetainUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_SampleStorages_StorageLocationId",
                table: "SampleStorages",
                column: "StorageLocationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SampleStorages");
        }
    }
}
