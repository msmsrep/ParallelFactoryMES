using MesApp.Core.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

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
    public DbSet<RoutingEquipment> RoutingEquipments => Set<RoutingEquipment>();
    public DbSet<Equipment> Equipments => Set<Equipment>();
    public DbSet<EquipmentPart> EquipmentParts => Set<EquipmentPart>();
    public DbSet<Tool> Tools => Set<Tool>();
    public DbSet<WorkCenter> WorkCenters => Set<WorkCenter>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<ControlItem> ControlItems => Set<ControlItem>();
    public DbSet<InspectionItem> InspectionItems => Set<InspectionItem>();
    public DbSet<Checklist> Checklists => Set<Checklist>();
    public DbSet<WorkProcedure> WorkProcedures => Set<WorkProcedure>();
    public DbSet<DefectReason> DefectReasons => Set<DefectReason>();
    public DbSet<SkillMaster> Skills => Set<SkillMaster>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<InspectionDevice> InspectionDevices => Set<InspectionDevice>();
    public DbSet<InspectionDeviceCalibration> InspectionDeviceCalibrations => Set<InspectionDeviceCalibration>();
    public DbSet<UserSkill> UserSkills => Set<UserSkill>();

    // 指図・実績系（Spec.md 5.2）／在庫系（5.3。Lotは産出ロット採番のため先行導入）
    public DbSet<ManufacturingOrder> ManufacturingOrders => Set<ManufacturingOrder>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<ManufacturingOrderMaterial> ManufacturingOrderMaterials => Set<ManufacturingOrderMaterial>();
    public DbSet<WorkOrderStatusHistory> WorkOrderStatusHistories => Set<WorkOrderStatusHistory>();
    public DbSet<WorkOrderControlItem> WorkOrderControlItems => Set<WorkOrderControlItem>();
    public DbSet<Lot> Lots => Set<Lot>();
    public DbSet<LotGenealogy> LotGenealogies => Set<LotGenealogy>();
    public DbSet<LotStatusHistory> LotStatusHistories => Set<LotStatusHistory>();

    // 製造実行系（Spec.md 5.2）
    public DbSet<SetupRecord> SetupRecords => Set<SetupRecord>();
    public DbSet<ChecklistRecord> ChecklistRecords => Set<ChecklistRecord>();
    public DbSet<MaterialConsumption> MaterialConsumptions => Set<MaterialConsumption>();
    public DbSet<ProductionRecord> ProductionRecords => Set<ProductionRecord>();
    public DbSet<ProductionRecordCorrection> ProductionRecordCorrections => Set<ProductionRecordCorrection>();
    public DbSet<ProductionDefect> ProductionDefects => Set<ProductionDefect>();
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
    public DbSet<SampleStorage> SampleStorages => Set<SampleStorage>();

    // 品質系（Spec.md 5.4）
    public DbSet<InspectionOrder> InspectionOrders => Set<InspectionOrder>();
    public DbSet<InspectionResult> InspectionResults => Set<InspectionResult>();
    public DbSet<InspectionResultCorrection> InspectionResultCorrections => Set<InspectionResultCorrection>();
    public DbSet<NonconformanceReport> NonconformanceReports => Set<NonconformanceReport>();
    public DbSet<ShipmentJudgment> ShipmentJudgments => Set<ShipmentJudgment>();

    // 設備保全系（Spec.md 5.1 MaintenanceProcedure、5.5）
    public DbSet<MaintenanceProcedure> MaintenanceProcedures => Set<MaintenanceProcedure>();
    public DbSet<EquipmentLog> EquipmentLogs => Set<EquipmentLog>();
    public DbSet<MaintenancePlan> MaintenancePlans => Set<MaintenancePlan>();
    public DbSet<MaintenanceOrder> MaintenanceOrders => Set<MaintenanceOrder>();
    public DbSet<MaintenanceRecord> MaintenanceRecords => Set<MaintenanceRecord>();
    public DbSet<MaintenanceRecordPart> MaintenanceRecordParts => Set<MaintenanceRecordPart>();
    public DbSet<ToolUsage> ToolUsages => Set<ToolUsage>();
    public DbSet<ToolIssue> ToolIssues => Set<ToolIssue>();

    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();

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
        builder.Properties<MaintenancePartCategory>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<MaintenanceCategory>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<MaintenancePlanStatus>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<MaintenanceRequestType>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<MaintenanceOrderStatus>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<ProductType>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<MakeOrBuy>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<EquipmentStatus>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<MaintenanceType>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<ToolStatus>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<WorkCenterLevel>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<LocationAreaType>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<InspectionType>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<ChecklistCategory>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<DefectReasonCategory>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<SkillType>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<ManufacturingOrderType>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<ManufacturingOrderStatus>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<WorkOrderStatus>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<LotOriginType>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<LotStockStatus>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<LotRelationType>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<LotStatusChangeSource>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<WorkOrderStatusChangeSource>().HaveConversion<string>().HaveMaxLength(30);

        // PostgreSQLの timestamptz はオフセット0（UTC）の値しか書き込めない（Npgsqlの仕様）。
        // 工場のタイムゾーン付きで作った時刻をそのまま保存できるよう、書き込み時にUTCへ直す。
        // 読み出した値はUTCになるが、同じ時点を指すので比較・表示（画面側で LocalDateTime）には影響しない
        if (Database.ProviderName == NpgsqlProviderName)
        {
            builder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
        }

        // 精度を指定していない decimal は、SQL Serverでは decimal(18,2) になり測定値・数量の小数3桁目以降が
        // 黙って切り捨てられる。SQLite以外では小数6桁まで持たせる（個別に HasPrecision した列はそちらが優先）。
        // SQLiteは精度を持たない（TEXTで保存）ため、スキーマを変えないよう対象外にする
        // 小数桁を固定した列は読み出すと 1.500000 のように末尾ゼロ付きで返り、そのままCSV・APIに出てしまうため、
        // 読み出し時に末尾ゼロを落としてSQLiteと同じ見え方にする
        if (!Database.IsSqlite())
        {
            builder.Properties<decimal>().HavePrecision(18, 6).HaveConversion<TrimmedDecimalConverter>();
        }
    }

    private sealed class TrimmedDecimalConverter() : ValueConverter<decimal, decimal>(
        v => v, v => TrimTrailingZeros(v));

    /// <summary>値を変えずに末尾ゼロ（スケール）だけを落とす。1.500000 → 1.5</summary>
    private static decimal TrimTrailingZeros(decimal value) => value / 1.0000000000000000000000000000m;

    private const string NpgsqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    private sealed class UtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(
        v => v.ToUniversalTime(), v => v);

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // エンティティごとの設定は Configurations/ に領域別に置く
        builder.ApplyConfigurationsFromAssembly(typeof(MesAppDbContext).Assembly);

        if (Database.ProviderName == SqlServerProviderName)
        {
            AvoidMultipleCascadePaths(builder);
        }
    }

    private const string SqlServerProviderName = "Microsoft.EntityFrameworkCore.SqlServer";

    /// <summary>
    /// SQL Serverは、削除の連鎖（CASCADE / SET NULL）が同じ表へ複数の経路で届く形や循環を許さない。
    /// SET NULL はDB側の動作をやめ、読み込み済みの子だけEFが null にする（ClientSetNull）。
    /// </summary>
    /// <remarks>
    /// SET NULL の親（利用者・設備・作業指示・ロット等）はアプリから物理削除しない（無効化で扱う）ため、
    /// 実運用での差は出ない。SQLite・PostgreSQLのスキーマは変えない。
    /// 残っていないことは DatabaseProviderTests が確かめる。
    /// </remarks>
    private static void AvoidMultipleCascadePaths(ModelBuilder builder)
    {
        foreach (var fk in builder.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
        {
            if (fk.DeleteBehavior == DeleteBehavior.SetNull)
            {
                fk.DeleteBehavior = DeleteBehavior.ClientSetNull;
            }
        }
    }
}
