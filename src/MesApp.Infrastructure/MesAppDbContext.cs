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

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        base.ConfigureConventions(builder);

        // enumは可読性のため文字列で保存する
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
