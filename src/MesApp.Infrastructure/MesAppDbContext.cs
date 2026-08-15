using MesApp.Core.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Infrastructure;

/// <summary>
/// 単一DB（バックエンドのみ保持。Spec.md 2章）。ASP.NET Core IdentityのスキーマとMES業務エンティティを同居させる。
/// </summary>
public class MesAppDbContext(DbContextOptions<MesAppDbContext> options)
    : IdentityDbContext<AppUser>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // マスタ系（Spec.md 5.1）
    public DbSet<Product> Products => Set<Product>();
    public DbSet<BomItem> BomItems => Set<BomItem>();
    public DbSet<ProcessMaster> Processes => Set<ProcessMaster>();
    public DbSet<Routing> Routings => Set<Routing>();
    public DbSet<Equipment> Equipments => Set<Equipment>();
    public DbSet<Tool> Tools => Set<Tool>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<InspectionItem> InspectionItems => Set<InspectionItem>();
    public DbSet<Checklist> Checklists => Set<Checklist>();
    public DbSet<SkillMaster> Skills => Set<SkillMaster>();
    public DbSet<UserSkill> UserSkills => Set<UserSkill>();

    // 指図・実績系（Spec.md 5.2）／在庫系（5.3。Lotは産出ロット採番のため先行導入）
    public DbSet<ManufacturingOrder> ManufacturingOrders => Set<ManufacturingOrder>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<Lot> Lots => Set<Lot>();

    // 製造実行系（Spec.md 5.2）
    public DbSet<SetupRecord> SetupRecords => Set<SetupRecord>();
    public DbSet<ChecklistRecord> ChecklistRecords => Set<ChecklistRecord>();
    public DbSet<MaterialConsumption> MaterialConsumptions => Set<MaterialConsumption>();
    public DbSet<ProductionRecord> ProductionRecords => Set<ProductionRecord>();
    public DbSet<ProductionDataRecord> ProductionDataRecords => Set<ProductionDataRecord>();
    public DbSet<WorkTimeRecord> WorkTimeRecords => Set<WorkTimeRecord>();
    public DbSet<TroubleReport> TroubleReports => Set<TroubleReport>();
    public DbSet<TransferOrder> TransferOrders => Set<TransferOrder>();

    // 在庫・物流系（Spec.md 5.3）
    public DbSet<InventoryStock> InventoryStocks => Set<InventoryStock>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<PickingOrder> PickingOrders => Set<PickingOrder>();
    public DbSet<ShippingOrder> ShippingOrders => Set<ShippingOrder>();
    public DbSet<Stocktake> Stocktakes => Set<Stocktake>();

    // 品質系（Spec.md 5.4）
    public DbSet<InspectionOrder> InspectionOrders => Set<InspectionOrder>();
    public DbSet<InspectionResult> InspectionResults => Set<InspectionResult>();
    public DbSet<NonconformanceReport> NonconformanceReports => Set<NonconformanceReport>();
    public DbSet<ShipmentJudgment> ShipmentJudgments => Set<ShipmentJudgment>();

    // 設備保全系（Spec.md 5.1 MaintenanceProcedure、5.5）
    public DbSet<MaintenanceProcedure> MaintenanceProcedures => Set<MaintenanceProcedure>();
    public DbSet<EquipmentLog> EquipmentLogs => Set<EquipmentLog>();
    public DbSet<MaintenancePlan> MaintenancePlans => Set<MaintenancePlan>();
    public DbSet<MaintenanceOrder> MaintenanceOrders => Set<MaintenanceOrder>();
    public DbSet<MaintenanceRecord> MaintenanceRecords => Set<MaintenanceRecord>();
    public DbSet<ToolUsage> ToolUsages => Set<ToolUsage>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        base.ConfigureConventions(builder);

        // enumは可読性のため文字列で保存する
        builder.Properties<SetupType>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<ConsumptionMethod>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<WorkTimeType>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<TroubleCategory>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<TroubleStatus>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<TransferOrderStatus>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<InventoryTransactionType>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<PickingOrderType>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<PickingOrderStatus>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<ShippingOrderStatus>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<StocktakeStatus>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<InspectionOrderType>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<InspectionOrderStatus>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<InspectionJudgment>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<NonconformanceSource>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<NonconformanceAction>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<NonconformanceStatus>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<ShipmentJudgmentResult>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<EquipmentLogStatus>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<MaintenanceCategory>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<MaintenancePlanStatus>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<MaintenanceRequestType>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<MaintenanceOrderStatus>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<ProductType>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<MakeOrBuy>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<EquipmentStatus>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<MaintenanceType>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<ToolStatus>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<LocationAreaType>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<InspectionType>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<ChecklistCategory>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<SkillType>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<ManufacturingOrderType>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<ManufacturingOrderStatus>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<WorkOrderStatus>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<LotOriginType>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<LotStockStatus>().HaveConversion<string>().HaveMaxLength(30);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<RefreshToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AuditLog>(e =>
        {
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => new { x.Category, x.Action });
            e.Property(x => x.Category).HasMaxLength(50);
            e.Property(x => x.Action).HasMaxLength(50);
            e.Property(x => x.TargetType).HasMaxLength(100);
            e.Property(x => x.TargetId).HasMaxLength(100);
        });

        // ---- マスタ系 ----

        builder.Entity<Product>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(50);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Unit).HasMaxLength(20);
            e.Property(x => x.Specification).HasMaxLength(500);
        });

        builder.Entity<BomItem>(e =>
        {
            e.HasIndex(x => new { x.ParentProductId, x.ChildProductId }).IsUnique();
            e.Property(x => x.AlternativeGroup).HasMaxLength(50);
            e.HasOne(x => x.ParentProduct)
                .WithMany()
                .HasForeignKey(x => x.ParentProductId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ChildProduct)
                .WithMany()
                .HasForeignKey(x => x.ChildProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ProcessMaster>(e =>
        {
            e.ToTable("Processes");
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(50);
            e.Property(x => x.Name).HasMaxLength(200);
        });

        builder.Entity<Routing>(e =>
        {
            e.HasIndex(x => new { x.ProductId, x.Sequence }).IsUnique();
            e.Property(x => x.ControlItems).HasMaxLength(1000);
            e.HasOne(x => x.Product)
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Process)
                .WithMany()
                .HasForeignKey(x => x.ProcessId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Equipment>(e =>
        {
            e.HasIndex(x => x.AssetNo).IsUnique();
            e.Property(x => x.AssetNo).HasMaxLength(50);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Site).HasMaxLength(200);
            e.Property(x => x.MaintenanceParts).HasMaxLength(1000);
        });

        builder.Entity<Tool>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(50);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.ToolType).HasMaxLength(100);
        });

        builder.Entity<Location>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(50);
            e.Property(x => x.ShelfNo).HasMaxLength(50);
        });

        builder.Entity<InspectionItem>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(50);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Method).HasMaxLength(500);
        });

        builder.Entity<Checklist>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(50);
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasMany(x => x.Items)
                .WithOne()
                .HasForeignKey(x => x.ChecklistId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ChecklistItem>(e =>
        {
            e.Property(x => x.Text).HasMaxLength(500);
        });

        builder.Entity<SkillMaster>(e =>
        {
            e.ToTable("Skills");
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(50);
            e.Property(x => x.Name).HasMaxLength(200);
        });

        builder.Entity<UserSkill>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.SkillId }).IsUnique();
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Skill)
                .WithMany()
                .HasForeignKey(x => x.SkillId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ---- 指図・実績系 ----

        builder.Entity<ManufacturingOrder>(e =>
        {
            e.HasIndex(x => x.OrderNo).IsUnique();
            e.HasIndex(x => x.Status);
            e.Property(x => x.OrderNo).HasMaxLength(50);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasOne(x => x.Product)
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SourceOrder)
                .WithMany()
                .HasForeignKey(x => x.SourceOrderId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.OutputLot)
                .WithMany()
                .HasForeignKey(x => x.OutputLotId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<WorkOrder>(e =>
        {
            e.HasIndex(x => x.WorkOrderNo).IsUnique();
            e.HasIndex(x => x.Status);
            e.Property(x => x.WorkOrderNo).HasMaxLength(50);
            e.HasOne(x => x.ManufacturingOrder)
                .WithMany(x => x.WorkOrders)
                .HasForeignKey(x => x.ManufacturingOrderId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Product)
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Process)
                .WithMany()
                .HasForeignKey(x => x.ProcessId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.AssignedUser)
                .WithMany()
                .HasForeignKey(x => x.AssignedUserId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.AssignedEquipment)
                .WithMany()
                .HasForeignKey(x => x.AssignedEquipmentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ---- 製造実行系 ----

        builder.Entity<SetupRecord>(e =>
        {
            e.HasIndex(x => x.WorkOrderId);
            e.Property(x => x.AbnormalityNote).HasMaxLength(1000);
            e.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.PerformedBy).WithMany().HasForeignKey(x => x.PerformedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ChecklistRecord>(e =>
        {
            e.HasIndex(x => x.WorkOrderId);
            e.HasOne(x => x.Checklist).WithMany().HasForeignKey(x => x.ChecklistId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Equipment).WithMany().HasForeignKey(x => x.EquipmentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PerformedBy).WithMany().HasForeignKey(x => x.PerformedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Results).WithOne().HasForeignKey(x => x.ChecklistRecordId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ChecklistResultItem>(e =>
        {
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasOne(x => x.ChecklistItem).WithMany().HasForeignKey(x => x.ChecklistItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<MaterialConsumption>(e =>
        {
            e.HasIndex(x => x.WorkOrderId);
            e.HasIndex(x => x.LotId); // トレースフォワード（使用先特定）用
            e.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ProductionRecord>(e =>
        {
            e.HasIndex(x => x.WorkOrderId);
            e.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.PerformedBy).WithMany().HasForeignKey(x => x.PerformedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.OutputLot).WithMany().HasForeignKey(x => x.OutputLotId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ProductionDataRecord>(e =>
        {
            e.HasIndex(x => x.WorkOrderId);
            e.Property(x => x.Item).HasMaxLength(100);
            e.Property(x => x.Value).HasMaxLength(500);
            e.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WorkTimeRecord>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.StartedAt });
            e.Property(x => x.IndirectCategory).HasMaxLength(100);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TroubleReport>(e =>
        {
            e.HasIndex(x => x.Status);
            e.Property(x => x.Content).HasMaxLength(2000);
            e.Property(x => x.ResponseHistory).HasMaxLength(4000);
            e.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Equipment).WithMany().HasForeignKey(x => x.EquipmentId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ReportedBy).WithMany().HasForeignKey(x => x.ReportedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<TransferOrder>(e =>
        {
            e.HasIndex(x => x.Status);
            e.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.FromLocation).WithMany().HasForeignKey(x => x.FromLocationId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ToLocation).WithMany().HasForeignKey(x => x.ToLocationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ---- 在庫・物流系 ----

        builder.Entity<InventoryStock>(e =>
        {
            e.HasIndex(x => new { x.LotId, x.LocationId }).IsUnique();
            e.HasIndex(x => new { x.ProductId, x.LocationId });
            e.Property(x => x.ConcurrencyStamp).HasMaxLength(32).IsConcurrencyToken();
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<InventoryTransaction>(e =>
        {
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => x.LotId);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.FromLocation).WithMany().HasForeignKey(x => x.FromLocationId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ToLocation).WithMany().HasForeignKey(x => x.ToLocationId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<PickingOrder>(e =>
        {
            e.HasIndex(x => x.OrderNo).IsUnique();
            e.Property(x => x.OrderNo).HasMaxLength(50);
            e.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ShippingOrder).WithMany().HasForeignKey(x => x.ShippingOrderId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PickingOrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PickingLine>(e =>
        {
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ShippingOrder>(e =>
        {
            e.HasIndex(x => x.ShippingNo).IsUnique();
            e.Property(x => x.ShippingNo).HasMaxLength(50);
            e.Property(x => x.Destination).HasMaxLength(200);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ShippingOrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ShippingLine>(e =>
        {
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Stocktake>(e =>
        {
            e.HasIndex(x => x.StocktakeNo).IsUnique();
            e.Property(x => x.StocktakeNo).HasMaxLength(50);
            e.HasOne(x => x.TargetLocation).WithMany().HasForeignKey(x => x.TargetLocationId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.StocktakeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<StocktakeLine>(e =>
        {
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ---- 品質系 ----

        builder.Entity<InspectionOrder>(e =>
        {
            e.HasIndex(x => x.OrderNo).IsUnique();
            e.HasIndex(x => x.Status);
            e.Property(x => x.OrderNo).HasMaxLength(50);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasOne(x => x.TargetLot).WithMany().HasForeignKey(x => x.TargetLotId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.TargetWorkOrder).WithMany().HasForeignKey(x => x.TargetWorkOrderId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RequestedBy).WithMany().HasForeignKey(x => x.RequestedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.InspectionOrderId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Results).WithOne().HasForeignKey(x => x.InspectionOrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<InspectionOrderItem>(e =>
        {
            e.HasIndex(x => new { x.InspectionOrderId, x.InspectionItemId }).IsUnique();
            e.HasOne(x => x.InspectionItem).WithMany().HasForeignKey(x => x.InspectionItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<InspectionResult>(e =>
        {
            e.HasIndex(x => x.InspectionOrderId);
            e.Property(x => x.TextValue).HasMaxLength(500);
            e.Property(x => x.CorrectionNote).HasMaxLength(1000);
            e.HasOne(x => x.InspectionItem).WithMany().HasForeignKey(x => x.InspectionItemId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.InspectedBy).WithMany().HasForeignKey(x => x.InspectedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<NonconformanceReport>(e =>
        {
            e.HasIndex(x => x.ReportNo).IsUnique();
            e.HasIndex(x => x.Status);
            e.Property(x => x.ReportNo).HasMaxLength(50);
            e.Property(x => x.Content).HasMaxLength(2000);
            e.Property(x => x.CauseCategory).HasMaxLength(100);
            e.Property(x => x.CauseDetail).HasMaxLength(2000);
            e.Property(x => x.ActionInstruction).HasMaxLength(1000);
            e.Property(x => x.ActionRecord).HasMaxLength(2000);
            e.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.InspectionOrder).WithMany().HasForeignKey(x => x.InspectionOrderId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ReworkOrder).WithMany().HasForeignKey(x => x.ReworkOrderId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ReportedBy).WithMany().HasForeignKey(x => x.ReportedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ShipmentJudgment>(e =>
        {
            e.HasIndex(x => x.JudgmentNo).IsUnique();
            e.HasIndex(x => x.ShippingOrderId);
            e.Property(x => x.JudgmentNo).HasMaxLength(50);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ShippingOrder).WithMany().HasForeignKey(x => x.ShippingOrderId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.JudgedBy).WithMany().HasForeignKey(x => x.JudgedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ---- 設備保全系 ----

        builder.Entity<MaintenanceProcedure>(e =>
        {
            e.HasIndex(x => x.ProcedureNo).IsUnique();
            e.Property(x => x.ProcedureNo).HasMaxLength(50);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Steps).HasMaxLength(4000);
            e.HasOne(x => x.TargetEquipment).WithMany().HasForeignKey(x => x.TargetEquipmentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.TargetTool).WithMany().HasForeignKey(x => x.TargetToolId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<EquipmentLog>(e =>
        {
            e.HasIndex(x => x.EquipmentId);
            e.Property(x => x.StopCause).HasMaxLength(500);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasOne(x => x.Equipment).WithMany().HasForeignKey(x => x.EquipmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MaintenancePlan>(e =>
        {
            e.HasIndex(x => new { x.EquipmentId, x.PlanYear });
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasOne(x => x.Equipment).WithMany().HasForeignKey(x => x.EquipmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MaintenanceOrder>(e =>
        {
            e.HasIndex(x => x.OrderNo).IsUnique();
            e.HasIndex(x => x.Status);
            e.Property(x => x.OrderNo).HasMaxLength(50);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasOne(x => x.Equipment).WithMany().HasForeignKey(x => x.EquipmentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Tool).WithMany().HasForeignKey(x => x.ToolId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.MaintenancePlan).WithMany().HasForeignKey(x => x.MaintenancePlanId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Procedure).WithMany().HasForeignKey(x => x.ProcedureId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Records).WithOne().HasForeignKey(x => x.MaintenanceOrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MaintenanceRecord>(e =>
        {
            e.Property(x => x.PartsUsed).HasMaxLength(1000);
            e.Property(x => x.Result).HasMaxLength(2000);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasOne(x => x.PerformedBy).WithMany().HasForeignKey(x => x.PerformedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ToolUsage>(e =>
        {
            e.HasIndex(x => x.ToolId);
            e.HasOne(x => x.Tool).WithMany().HasForeignKey(x => x.ToolId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Lot>(e =>
        {
            e.HasIndex(x => x.LotNumber).IsUnique();
            e.Property(x => x.LotNumber).HasMaxLength(60);
            e.Property(x => x.Grade).HasMaxLength(30);
            e.HasOne(x => x.Product)
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SourceWorkOrder)
                .WithMany()
                .HasForeignKey(x => x.SourceWorkOrderId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ParentLot)
                .WithMany()
                .HasForeignKey(x => x.ParentLotId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
