using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProductionQuantityBreakdown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ReworkQuantity",
                table: "ProductionRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ScrapQuantity",
                table: "ProductionRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AfterReworkQuantity",
                table: "ProductionRecordCorrections",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AfterScrapQuantity",
                table: "ProductionRecordCorrections",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BeforeReworkQuantity",
                table: "ProductionRecordCorrections",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BeforeScrapQuantity",
                table: "ProductionRecordCorrections",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReworkQuantity",
                table: "ProductionRecords");

            migrationBuilder.DropColumn(
                name: "ScrapQuantity",
                table: "ProductionRecords");

            migrationBuilder.DropColumn(
                name: "AfterReworkQuantity",
                table: "ProductionRecordCorrections");

            migrationBuilder.DropColumn(
                name: "AfterScrapQuantity",
                table: "ProductionRecordCorrections");

            migrationBuilder.DropColumn(
                name: "BeforeReworkQuantity",
                table: "ProductionRecordCorrections");

            migrationBuilder.DropColumn(
                name: "BeforeScrapQuantity",
                table: "ProductionRecordCorrections");
        }
    }
}
