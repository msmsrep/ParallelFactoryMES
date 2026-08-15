using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase3ExecutionAndInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChecklistRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChecklistId = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkOrderId = table.Column<int>(type: "INTEGER", nullable: true),
                    EquipmentId = table.Column<int>(type: "INTEGER", nullable: true),
                    PerformedByUserId = table.Column<string>(type: "TEXT", nullable: false),
                    PerformedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChecklistRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChecklistRecords_AspNetUsers_PerformedByUserId",
                        column: x => x.PerformedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChecklistRecords_Checklists_ChecklistId",
                        column: x => x.ChecklistId,
                        principalTable: "Checklists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChecklistRecords_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChecklistRecords_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InventoryStocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    LotId = table.Column<int>(type: "INTEGER", nullable: false),
                    LocationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryStocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryStocks_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryStocks_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryStocks_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryTransactions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    LotId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    FromLocationId = table.Column<int>(type: "INTEGER", nullable: true),
                    ToLocationId = table.Column<int>(type: "INTEGER", nullable: true),
                    WorkOrderId = table.Column<int>(type: "INTEGER", nullable: true),
                    PickingOrderId = table.Column<int>(type: "INTEGER", nullable: true),
                    ShippingOrderId = table.Column<int>(type: "INTEGER", nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PerformedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_Locations_FromLocationId",
                        column: x => x.FromLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_Locations_ToLocationId",
                        column: x => x.ToLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MaterialConsumptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkOrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    LotId = table.Column<int>(type: "INTEGER", nullable: false),
                    LocationId = table.Column<int>(type: "INTEGER", nullable: true),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    RecordedByUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialConsumptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaterialConsumptions_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaterialConsumptions_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaterialConsumptions_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaterialConsumptions_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductionDataRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkOrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    Item = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RecordedByUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionDataRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionDataRecords_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductionRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkOrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    PerformedByUserId = table.Column<string>(type: "TEXT", nullable: false),
                    GoodQuantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    DefectQuantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    OutputLotId = table.Column<int>(type: "INTEGER", nullable: true),
                    OutputLocationId = table.Column<int>(type: "INTEGER", nullable: true),
                    ApprovedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionRecords_AspNetUsers_PerformedByUserId",
                        column: x => x.PerformedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionRecords_Lots_OutputLotId",
                        column: x => x.OutputLotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ProductionRecords_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SetupRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkOrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PerformedByUserId = table.Column<string>(type: "TEXT", nullable: false),
                    AbnormalityNote = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetupRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SetupRecords_AspNetUsers_PerformedByUserId",
                        column: x => x.PerformedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SetupRecords_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShippingOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ShippingNo = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Destination = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PlannedDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ShippedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ShippedByUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Stocktakes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StocktakeNo = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    TargetLocationId = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    FinalizedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FinalizedByUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stocktakes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stocktakes_Locations_TargetLocationId",
                        column: x => x.TargetLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransferOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LotId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    FromLocationId = table.Column<int>(type: "INTEGER", nullable: false),
                    ToLocationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExecutedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransferOrders_Locations_FromLocationId",
                        column: x => x.FromLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferOrders_Locations_ToLocationId",
                        column: x => x.ToLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferOrders_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TroubleReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    WorkOrderId = table.Column<int>(type: "INTEGER", nullable: true),
                    EquipmentId = table.Column<int>(type: "INTEGER", nullable: true),
                    Content = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    ResponseHistory = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ReportedByUserId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TroubleReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TroubleReports_AspNetUsers_ReportedByUserId",
                        column: x => x.ReportedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TroubleReports_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TroubleReports_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "WorkTimeRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    IndirectCategory = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    WorkOrderId = table.Column<int>(type: "INTEGER", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTimeRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkTimeRecords_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkTimeRecords_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ChecklistResultItem",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChecklistRecordId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChecklistItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsChecked = table.Column<bool>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChecklistResultItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChecklistResultItem_ChecklistItem_ChecklistItemId",
                        column: x => x.ChecklistItemId,
                        principalTable: "ChecklistItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChecklistResultItem_ChecklistRecords_ChecklistRecordId",
                        column: x => x.ChecklistRecordId,
                        principalTable: "ChecklistRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PickingOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrderNo = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    WorkOrderId = table.Column<int>(type: "INTEGER", nullable: true),
                    ShippingOrderId = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExecutedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickingOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PickingOrders_ShippingOrders_ShippingOrderId",
                        column: x => x.ShippingOrderId,
                        principalTable: "ShippingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PickingOrders_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ShippingLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ShippingOrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    ShippedQuantity = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShippingLine_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShippingLine_ShippingOrders_ShippingOrderId",
                        column: x => x.ShippingOrderId,
                        principalTable: "ShippingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StocktakeLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StocktakeId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    LotId = table.Column<int>(type: "INTEGER", nullable: false),
                    LocationId = table.Column<int>(type: "INTEGER", nullable: false),
                    TheoreticalQuantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    CountedQuantity = table.Column<decimal>(type: "TEXT", nullable: true),
                    IsAdjusted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StocktakeLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StocktakeLine_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StocktakeLine_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StocktakeLine_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StocktakeLine_Stocktakes_StocktakeId",
                        column: x => x.StocktakeId,
                        principalTable: "Stocktakes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PickingLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PickingOrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    LotId = table.Column<int>(type: "INTEGER", nullable: false),
                    LocationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickingLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PickingLine_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingLine_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingLine_PickingOrders_PickingOrderId",
                        column: x => x.PickingOrderId,
                        principalTable: "PickingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PickingLine_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistRecords_ChecklistId",
                table: "ChecklistRecords",
                column: "ChecklistId");

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistRecords_EquipmentId",
                table: "ChecklistRecords",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistRecords_PerformedByUserId",
                table: "ChecklistRecords",
                column: "PerformedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistRecords_WorkOrderId",
                table: "ChecklistRecords",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistResultItem_ChecklistItemId",
                table: "ChecklistResultItem",
                column: "ChecklistItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistResultItem_ChecklistRecordId",
                table: "ChecklistResultItem",
                column: "ChecklistRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryStocks_LocationId",
                table: "InventoryStocks",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryStocks_LotId_LocationId",
                table: "InventoryStocks",
                columns: new[] { "LotId", "LocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryStocks_ProductId_LocationId",
                table: "InventoryStocks",
                columns: new[] { "ProductId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_FromLocationId",
                table: "InventoryTransactions",
                column: "FromLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_LotId",
                table: "InventoryTransactions",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_ProductId",
                table: "InventoryTransactions",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_Timestamp",
                table: "InventoryTransactions",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_ToLocationId",
                table: "InventoryTransactions",
                column: "ToLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_WorkOrderId",
                table: "InventoryTransactions",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialConsumptions_LocationId",
                table: "MaterialConsumptions",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialConsumptions_LotId",
                table: "MaterialConsumptions",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialConsumptions_ProductId",
                table: "MaterialConsumptions",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialConsumptions_WorkOrderId",
                table: "MaterialConsumptions",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingLine_LocationId",
                table: "PickingLine",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingLine_LotId",
                table: "PickingLine",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingLine_PickingOrderId",
                table: "PickingLine",
                column: "PickingOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingLine_ProductId",
                table: "PickingLine",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingOrders_OrderNo",
                table: "PickingOrders",
                column: "OrderNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PickingOrders_ShippingOrderId",
                table: "PickingOrders",
                column: "ShippingOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingOrders_WorkOrderId",
                table: "PickingOrders",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionDataRecords_WorkOrderId",
                table: "ProductionDataRecords",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRecords_OutputLotId",
                table: "ProductionRecords",
                column: "OutputLotId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRecords_PerformedByUserId",
                table: "ProductionRecords",
                column: "PerformedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRecords_WorkOrderId",
                table: "ProductionRecords",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SetupRecords_PerformedByUserId",
                table: "SetupRecords",
                column: "PerformedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SetupRecords_WorkOrderId",
                table: "SetupRecords",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingLine_ProductId",
                table: "ShippingLine",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingLine_ShippingOrderId",
                table: "ShippingLine",
                column: "ShippingOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingOrders_ShippingNo",
                table: "ShippingOrders",
                column: "ShippingNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeLine_LocationId",
                table: "StocktakeLine",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeLine_LotId",
                table: "StocktakeLine",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeLine_ProductId",
                table: "StocktakeLine",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeLine_StocktakeId",
                table: "StocktakeLine",
                column: "StocktakeId");

            migrationBuilder.CreateIndex(
                name: "IX_Stocktakes_StocktakeNo",
                table: "Stocktakes",
                column: "StocktakeNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stocktakes_TargetLocationId",
                table: "Stocktakes",
                column: "TargetLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferOrders_FromLocationId",
                table: "TransferOrders",
                column: "FromLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferOrders_LotId",
                table: "TransferOrders",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferOrders_Status",
                table: "TransferOrders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_TransferOrders_ToLocationId",
                table: "TransferOrders",
                column: "ToLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_TroubleReports_EquipmentId",
                table: "TroubleReports",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_TroubleReports_ReportedByUserId",
                table: "TroubleReports",
                column: "ReportedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TroubleReports_Status",
                table: "TroubleReports",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_TroubleReports_WorkOrderId",
                table: "TroubleReports",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTimeRecords_UserId_StartedAt",
                table: "WorkTimeRecords",
                columns: new[] { "UserId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkTimeRecords_WorkOrderId",
                table: "WorkTimeRecords",
                column: "WorkOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChecklistResultItem");

            migrationBuilder.DropTable(
                name: "InventoryStocks");

            migrationBuilder.DropTable(
                name: "InventoryTransactions");

            migrationBuilder.DropTable(
                name: "MaterialConsumptions");

            migrationBuilder.DropTable(
                name: "PickingLine");

            migrationBuilder.DropTable(
                name: "ProductionDataRecords");

            migrationBuilder.DropTable(
                name: "ProductionRecords");

            migrationBuilder.DropTable(
                name: "SetupRecords");

            migrationBuilder.DropTable(
                name: "ShippingLine");

            migrationBuilder.DropTable(
                name: "StocktakeLine");

            migrationBuilder.DropTable(
                name: "TransferOrders");

            migrationBuilder.DropTable(
                name: "TroubleReports");

            migrationBuilder.DropTable(
                name: "WorkTimeRecords");

            migrationBuilder.DropTable(
                name: "ChecklistRecords");

            migrationBuilder.DropTable(
                name: "PickingOrders");

            migrationBuilder.DropTable(
                name: "Stocktakes");

            migrationBuilder.DropTable(
                name: "ShippingOrders");
        }
    }
}
