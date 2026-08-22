using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrderRoutingAndBomSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ControlItems",
                table: "WorkOrders",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequiredSkillId",
                table: "WorkOrders",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RoutingChecklistId",
                table: "WorkOrders",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StandardSetupMinutes",
                table: "WorkOrders",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "StandardWorkMinutes",
                table: "WorkOrders",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "ManufacturingOrderMaterials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ManufacturingOrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChildProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    QuantityPer = table.Column<decimal>(type: "TEXT", nullable: false),
                    PlannedQuantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    AlternativeGroup = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturingOrderMaterials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManufacturingOrderMaterials_ManufacturingOrders_ManufacturingOrderId",
                        column: x => x.ManufacturingOrderId,
                        principalTable: "ManufacturingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ManufacturingOrderMaterials_Products_ChildProductId",
                        column: x => x.ChildProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_RequiredSkillId",
                table: "WorkOrders",
                column: "RequiredSkillId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_RoutingChecklistId",
                table: "WorkOrders",
                column: "RoutingChecklistId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingOrderMaterials_ChildProductId",
                table: "ManufacturingOrderMaterials",
                column: "ChildProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingOrderMaterials_ManufacturingOrderId_ChildProductId",
                table: "ManufacturingOrderMaterials",
                columns: new[] { "ManufacturingOrderId", "ChildProductId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_Checklists_RoutingChecklistId",
                table: "WorkOrders",
                column: "RoutingChecklistId",
                principalTable: "Checklists",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_Skills_RequiredSkillId",
                table: "WorkOrders",
                column: "RequiredSkillId",
                principalTable: "Skills",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // 既存データの補填。旧実装は工順マスタとMBOMの現在値を都度参照していたため、
            // 現在値で埋めることで従来と同じ挙動にそろえる（当時の値は残っていない）。
            // 以降に展開される指図は展開時点の値を保持する。
            migrationBuilder.Sql(@"
                UPDATE WorkOrders
                SET StandardWorkMinutes  = COALESCE((SELECT r.StandardWorkMinutes  FROM Routings r WHERE r.ProductId = WorkOrders.ProductId AND r.Sequence = WorkOrders.RoutingSequence), 0),
                    StandardSetupMinutes = COALESCE((SELECT r.StandardSetupMinutes FROM Routings r WHERE r.ProductId = WorkOrders.ProductId AND r.Sequence = WorkOrders.RoutingSequence), 0),
                    RequiredSkillId      =          (SELECT r.RequiredSkillId      FROM Routings r WHERE r.ProductId = WorkOrders.ProductId AND r.Sequence = WorkOrders.RoutingSequence),
                    ControlItems         =          (SELECT r.ControlItems         FROM Routings r WHERE r.ProductId = WorkOrders.ProductId AND r.Sequence = WorkOrders.RoutingSequence),
                    RoutingChecklistId   =          (SELECT r.ChecklistId          FROM Routings r WHERE r.ProductId = WorkOrders.ProductId AND r.Sequence = WorkOrders.RoutingSequence);");

            // 展開済みの指図には予定材料がないと部材投入が止まるため、現在のMBOMから補填する
            migrationBuilder.Sql(@"
                INSERT OR IGNORE INTO ManufacturingOrderMaterials
                    (ManufacturingOrderId, ChildProductId, QuantityPer, PlannedQuantity, AlternativeGroup)
                SELECT o.Id, b.ChildProductId, b.QuantityPer, b.QuantityPer * o.Quantity, b.AlternativeGroup
                FROM ManufacturingOrders o
                JOIN BomItems b ON b.ParentProductId = o.ProductId
                WHERE o.Status IN ('Released', 'Completed');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_Checklists_RoutingChecklistId",
                table: "WorkOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_Skills_RequiredSkillId",
                table: "WorkOrders");

            migrationBuilder.DropTable(
                name: "ManufacturingOrderMaterials");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_RequiredSkillId",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_RoutingChecklistId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "ControlItems",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "RequiredSkillId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "RoutingChecklistId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "StandardSetupMinutes",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "StandardWorkMinutes",
                table: "WorkOrders");
        }
    }
}
