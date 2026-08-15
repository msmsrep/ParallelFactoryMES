using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase2MastersAndProduction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Checklists",
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
                    table.PrimaryKey("PK_Checklists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Equipments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AssetNo = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Site = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    MaintenanceType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    MaintenanceThreshold = table.Column<decimal>(type: "TEXT", nullable: true),
                    MaintenanceParts = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Equipments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    AreaType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ShelfNo = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Processes",
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
                    table.PrimaryKey("PK_Processes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Specification = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    StandardDefectRate = table.Column<decimal>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Skills",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    RequiresExpiry = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Skills", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tools",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ToolType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    LifeThresholdCount = table.Column<int>(type: "INTEGER", nullable: true),
                    LifeThresholdHours = table.Column<decimal>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tools", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChecklistItem",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChecklistId = table.Column<int>(type: "INTEGER", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    IsRequired = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChecklistItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChecklistItem_Checklists_ChecklistId",
                        column: x => x.ChecklistId,
                        principalTable: "Checklists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BomItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ParentProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChildProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    QuantityPer = table.Column<decimal>(type: "TEXT", nullable: false),
                    MakeOrBuy = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    AlternativeGroup = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BomItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BomItems_Products_ChildProductId",
                        column: x => x.ChildProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BomItems_Products_ParentProductId",
                        column: x => x.ParentProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InspectionItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TargetProductId = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetProcessId = table.Column<int>(type: "INTEGER", nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    LowerLimit = table.Column<decimal>(type: "TEXT", nullable: true),
                    UpperLimit = table.Column<decimal>(type: "TEXT", nullable: true),
                    StandardValue = table.Column<decimal>(type: "TEXT", nullable: true),
                    Method = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SamplingCount = table.Column<int>(type: "INTEGER", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InspectionItems_Processes_TargetProcessId",
                        column: x => x.TargetProcessId,
                        principalTable: "Processes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InspectionItems_Products_TargetProductId",
                        column: x => x.TargetProductId,
                        principalTable: "Products",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserSkills",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    SkillId = table.Column<int>(type: "INTEGER", nullable: false),
                    AcquiredOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ExpiresOn = table.Column<DateOnly>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSkills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSkills_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserSkills_Skills_SkillId",
                        column: x => x.SkillId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Routings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessId = table.Column<int>(type: "INTEGER", nullable: false),
                    StandardWorkMinutes = table.Column<decimal>(type: "TEXT", nullable: false),
                    StandardSetupMinutes = table.Column<decimal>(type: "TEXT", nullable: false),
                    RequiredSkillId = table.Column<int>(type: "INTEGER", nullable: true),
                    EquipmentId = table.Column<int>(type: "INTEGER", nullable: true),
                    ToolId = table.Column<int>(type: "INTEGER", nullable: true),
                    ControlItems = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ChecklistId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Routings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Routings_Checklists_ChecklistId",
                        column: x => x.ChecklistId,
                        principalTable: "Checklists",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Routings_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Routings_Processes_ProcessId",
                        column: x => x.ProcessId,
                        principalTable: "Processes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Routings_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Routings_Skills_RequiredSkillId",
                        column: x => x.RequiredSkillId,
                        principalTable: "Skills",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Routings_Tools_ToolId",
                        column: x => x.ToolId,
                        principalTable: "Tools",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Lots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LotNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    InitialQuantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    OriginType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    SourceWorkOrderId = table.Column<int>(type: "INTEGER", nullable: true),
                    ManufacturedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ExpiresOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    StockStatus = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Grade = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    ParentLotId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Lots_Lots_ParentLotId",
                        column: x => x.ParentLotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Lots_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ManufacturingOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrderNo = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    OrderType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ApprovedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    SourceOrderId = table.Column<int>(type: "INTEGER", nullable: true),
                    OutputLotId = table.Column<int>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturingOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManufacturingOrders_Lots_OutputLotId",
                        column: x => x.OutputLotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ManufacturingOrders_ManufacturingOrders_SourceOrderId",
                        column: x => x.SourceOrderId,
                        principalTable: "ManufacturingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManufacturingOrders_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkOrderNo = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ManufacturingOrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessId = table.Column<int>(type: "INTEGER", nullable: false),
                    RoutingSequence = table.Column<int>(type: "INTEGER", nullable: false),
                    PlannedQuantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    DispatchOrder = table.Column<int>(type: "INTEGER", nullable: true),
                    AssignedUserId = table.Column<string>(type: "TEXT", nullable: true),
                    AssignedEquipmentId = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrders_AspNetUsers_AssignedUserId",
                        column: x => x.AssignedUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Equipments_AssignedEquipmentId",
                        column: x => x.AssignedEquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkOrders_ManufacturingOrders_ManufacturingOrderId",
                        column: x => x.ManufacturingOrderId,
                        principalTable: "ManufacturingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Processes_ProcessId",
                        column: x => x.ProcessId,
                        principalTable: "Processes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BomItems_ChildProductId",
                table: "BomItems",
                column: "ChildProductId");

            migrationBuilder.CreateIndex(
                name: "IX_BomItems_ParentProductId_ChildProductId",
                table: "BomItems",
                columns: new[] { "ParentProductId", "ChildProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistItem_ChecklistId",
                table: "ChecklistItem",
                column: "ChecklistId");

            migrationBuilder.CreateIndex(
                name: "IX_Checklists_Code",
                table: "Checklists",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Equipments_AssetNo",
                table: "Equipments",
                column: "AssetNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InspectionItems_Code",
                table: "InspectionItems",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InspectionItems_TargetProcessId",
                table: "InspectionItems",
                column: "TargetProcessId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionItems_TargetProductId",
                table: "InspectionItems",
                column: "TargetProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_Code",
                table: "Locations",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lots_LotNumber",
                table: "Lots",
                column: "LotNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lots_ParentLotId",
                table: "Lots",
                column: "ParentLotId");

            migrationBuilder.CreateIndex(
                name: "IX_Lots_ProductId",
                table: "Lots",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Lots_SourceWorkOrderId",
                table: "Lots",
                column: "SourceWorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingOrders_OrderNo",
                table: "ManufacturingOrders",
                column: "OrderNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingOrders_OutputLotId",
                table: "ManufacturingOrders",
                column: "OutputLotId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingOrders_ProductId",
                table: "ManufacturingOrders",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingOrders_SourceOrderId",
                table: "ManufacturingOrders",
                column: "SourceOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingOrders_Status",
                table: "ManufacturingOrders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Processes_Code",
                table: "Processes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_Code",
                table: "Products",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Routings_ChecklistId",
                table: "Routings",
                column: "ChecklistId");

            migrationBuilder.CreateIndex(
                name: "IX_Routings_EquipmentId",
                table: "Routings",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Routings_ProcessId",
                table: "Routings",
                column: "ProcessId");

            migrationBuilder.CreateIndex(
                name: "IX_Routings_ProductId_Sequence",
                table: "Routings",
                columns: new[] { "ProductId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Routings_RequiredSkillId",
                table: "Routings",
                column: "RequiredSkillId");

            migrationBuilder.CreateIndex(
                name: "IX_Routings_ToolId",
                table: "Routings",
                column: "ToolId");

            migrationBuilder.CreateIndex(
                name: "IX_Skills_Code",
                table: "Skills",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tools_Code",
                table: "Tools",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSkills_SkillId",
                table: "UserSkills",
                column: "SkillId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSkills_UserId_SkillId",
                table: "UserSkills",
                columns: new[] { "UserId", "SkillId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_AssignedEquipmentId",
                table: "WorkOrders",
                column: "AssignedEquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_AssignedUserId",
                table: "WorkOrders",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_ManufacturingOrderId",
                table: "WorkOrders",
                column: "ManufacturingOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_ProcessId",
                table: "WorkOrders",
                column: "ProcessId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_ProductId",
                table: "WorkOrders",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_Status",
                table: "WorkOrders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_WorkOrderNo",
                table: "WorkOrders",
                column: "WorkOrderNo",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Lots_WorkOrders_SourceWorkOrderId",
                table: "Lots",
                column: "SourceWorkOrderId",
                principalTable: "WorkOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Lots_Products_ProductId",
                table: "Lots");

            migrationBuilder.DropForeignKey(
                name: "FK_ManufacturingOrders_Products_ProductId",
                table: "ManufacturingOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_Products_ProductId",
                table: "WorkOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_Processes_ProcessId",
                table: "WorkOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_Lots_WorkOrders_SourceWorkOrderId",
                table: "Lots");

            migrationBuilder.DropTable(
                name: "BomItems");

            migrationBuilder.DropTable(
                name: "ChecklistItem");

            migrationBuilder.DropTable(
                name: "InspectionItems");

            migrationBuilder.DropTable(
                name: "Locations");

            migrationBuilder.DropTable(
                name: "Routings");

            migrationBuilder.DropTable(
                name: "UserSkills");

            migrationBuilder.DropTable(
                name: "Checklists");

            migrationBuilder.DropTable(
                name: "Tools");

            migrationBuilder.DropTable(
                name: "Skills");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "Processes");

            migrationBuilder.DropTable(
                name: "WorkOrders");

            migrationBuilder.DropTable(
                name: "Equipments");

            migrationBuilder.DropTable(
                name: "ManufacturingOrders");

            migrationBuilder.DropTable(
                name: "Lots");
        }
    }
}
