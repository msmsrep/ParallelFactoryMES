using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InspectionCriteriaSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ItemCode",
                table: "InspectionOrderItem",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ItemName",
                table: "InspectionOrderItem",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ItemVersion",
                table: "InspectionOrderItem",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "LowerLimit",
                table: "InspectionOrderItem",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Method",
                table: "InspectionOrderItem",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SamplingCount",
                table: "InspectionOrderItem",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StandardValue",
                table: "InspectionOrderItem",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UpperLimit",
                table: "InspectionOrderItem",
                type: "TEXT",
                nullable: true);

            // 既存の検査指示にスナップショットを補填する。旧スキーマは基準の参照しか持たず、
            // 当時の値は残っていないため、マスタの現在値で埋める（旧実装の表示内容と等価）。
            // 以降に発行される検査指示は発行時点の値を保持する。
            migrationBuilder.Sql(@"
                UPDATE InspectionOrderItem
                SET ItemCode      = COALESCE((SELECT Code          FROM InspectionItems i WHERE i.Id = InspectionOrderItem.InspectionItemId), ''),
                    ItemName      = COALESCE((SELECT Name          FROM InspectionItems i WHERE i.Id = InspectionOrderItem.InspectionItemId), ''),
                    ItemVersion   = COALESCE((SELECT Version       FROM InspectionItems i WHERE i.Id = InspectionOrderItem.InspectionItemId), 1),
                    LowerLimit    =          (SELECT LowerLimit    FROM InspectionItems i WHERE i.Id = InspectionOrderItem.InspectionItemId),
                    UpperLimit    =          (SELECT UpperLimit    FROM InspectionItems i WHERE i.Id = InspectionOrderItem.InspectionItemId),
                    StandardValue =          (SELECT StandardValue FROM InspectionItems i WHERE i.Id = InspectionOrderItem.InspectionItemId),
                    Method        =          (SELECT Method        FROM InspectionItems i WHERE i.Id = InspectionOrderItem.InspectionItemId),
                    SamplingCount =          (SELECT SamplingCount FROM InspectionItems i WHERE i.Id = InspectionOrderItem.InspectionItemId);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ItemCode",
                table: "InspectionOrderItem");

            migrationBuilder.DropColumn(
                name: "ItemName",
                table: "InspectionOrderItem");

            migrationBuilder.DropColumn(
                name: "ItemVersion",
                table: "InspectionOrderItem");

            migrationBuilder.DropColumn(
                name: "LowerLimit",
                table: "InspectionOrderItem");

            migrationBuilder.DropColumn(
                name: "Method",
                table: "InspectionOrderItem");

            migrationBuilder.DropColumn(
                name: "SamplingCount",
                table: "InspectionOrderItem");

            migrationBuilder.DropColumn(
                name: "StandardValue",
                table: "InspectionOrderItem");

            migrationBuilder.DropColumn(
                name: "UpperLimit",
                table: "InspectionOrderItem");
        }
    }
}
