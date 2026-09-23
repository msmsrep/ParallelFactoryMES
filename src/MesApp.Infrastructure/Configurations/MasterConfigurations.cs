using MesApp.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesApp.Infrastructure.Configurations;

// マスタ系（Spec.md 5.1）

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> e)
    {
        e.HasIndex(x => x.Code).IsUnique();
        e.Property(x => x.Code).HasMaxLength(50);
        e.Property(x => x.Name).HasMaxLength(200);
        e.Property(x => x.Unit).HasMaxLength(20);
        e.Property(x => x.Specification).HasMaxLength(500);
        // 既定ロケーションを参照している品目があるうちはロケーションを消させない
        e.HasOne(x => x.DefaultLocation).WithMany().HasForeignKey(x => x.DefaultLocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BomItemConfiguration : IEntityTypeConfiguration<BomItem>
{
    public void Configure(EntityTypeBuilder<BomItem> e)
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
    }
}

internal sealed class ProcessMasterConfiguration : IEntityTypeConfiguration<ProcessMaster>
{
    public void Configure(EntityTypeBuilder<ProcessMaster> e)
    {
        e.ToTable("Processes");
        e.HasIndex(x => x.Code).IsUnique();
        e.Property(x => x.Code).HasMaxLength(50);
        e.Property(x => x.Name).HasMaxLength(200);
    }
}

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> e)
    {
        e.Property(x => x.Department).HasMaxLength(100);
        e.HasOne(x => x.WorkCenter).WithMany().HasForeignKey(x => x.WorkCenterId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Shift).WithMany().HasForeignKey(x => x.ShiftId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RoutingEquipmentConfiguration : IEntityTypeConfiguration<RoutingEquipment>
{
    public void Configure(EntityTypeBuilder<RoutingEquipment> e)
    {
        // 同じ工順に同じ設備を二重登録させない
        e.HasIndex(x => new { x.RoutingId, x.EquipmentId }).IsUnique();
        // 工順を置き換えると候補も消えるべき。逆側のナビゲーションを明示する
        // （WithMany() だけだとEFが規約でもう一本関連を作りFK列が二重になる）
        e.HasOne(x => x.Routing).WithMany(r => r.EquipmentCandidates)
            .HasForeignKey(x => x.RoutingId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Equipment).WithMany().HasForeignKey(x => x.EquipmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RoutingControlItemConfiguration : IEntityTypeConfiguration<RoutingControlItem>
{
    public void Configure(EntityTypeBuilder<RoutingControlItem> e)
    {
        // 同じ工程に同じ項目を二重に紐付けさせない（作業指示に同じ指示が2行出る）
        e.HasIndex(x => new { x.RoutingId, x.ControlItemId }).IsUnique();
        // 候補設備と同じく、工順を置き換えると紐付けも消える。逆側のナビゲーションを明示する
        e.HasOne(x => x.Routing).WithMany(r => r.ControlItemLinks)
            .HasForeignKey(x => x.RoutingId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.ControlItem).WithMany().HasForeignKey(x => x.ControlItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class WorkProcedureConfiguration : IEntityTypeConfiguration<WorkProcedure>
{
    public void Configure(EntityTypeBuilder<WorkProcedure> e)
    {
        e.HasIndex(x => x.ProcedureNo).IsUnique();
        e.Property(x => x.ProcedureNo).HasMaxLength(50);
        e.Property(x => x.Title).HasMaxLength(200);
        e.Property(x => x.Steps).HasMaxLength(4000);
        e.Property(x => x.Reference).HasMaxLength(500);
    }
}

internal sealed class RoutingConfiguration : IEntityTypeConfiguration<Routing>
{
    public void Configure(EntityTypeBuilder<Routing> e)
    {
        e.HasOne(x => x.WorkCenter).WithMany().HasForeignKey(x => x.WorkCenterId)
            .OnDelete(DeleteBehavior.Restrict);
        // 工順から参照されている手順書を消させない（作業指示が手順を辿れなくなる）
        e.HasOne(x => x.WorkProcedure).WithMany().HasForeignKey(x => x.WorkProcedureId)
            .OnDelete(DeleteBehavior.Restrict);
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
    }
}

internal sealed class EquipmentConfiguration : IEntityTypeConfiguration<Equipment>
{
    public void Configure(EntityTypeBuilder<Equipment> e)
    {
        e.HasIndex(x => x.AssetNo).IsUnique();
        e.Property(x => x.AssetNo).HasMaxLength(50);
        e.Property(x => x.Name).HasMaxLength(200);
        e.Property(x => x.Site).HasMaxLength(200);
        e.Property(x => x.MaintenanceParts).HasMaxLength(1000);
        e.HasOne(x => x.WorkCenter).WithMany().HasForeignKey(x => x.WorkCenterId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EquipmentPartConfiguration : IEntityTypeConfiguration<EquipmentPart>
{
    public void Configure(EntityTypeBuilder<EquipmentPart> e)
    {
        // 同じ設備に同じ品目を二重登録させない
        e.HasIndex(x => new { x.EquipmentId, x.ProductId }).IsUnique();
        e.Property(x => x.QuantityPer).HasPrecision(18, 4);
        e.Property(x => x.Note).HasMaxLength(500);
        // 設備を消すと保全部品も消えるべき。逆側のナビゲーションを明示する
        e.HasOne(x => x.Equipment).WithMany(q => q.Parts).HasForeignKey(x => x.EquipmentId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> e)
    {
        e.HasIndex(x => x.Code).IsUnique();
        e.Property(x => x.Code).HasMaxLength(20);
        e.Property(x => x.Name).HasMaxLength(100);
    }
}

internal sealed class InspectionDeviceConfiguration : IEntityTypeConfiguration<InspectionDevice>
{
    public void Configure(EntityTypeBuilder<InspectionDevice> e)
    {
        e.HasIndex(x => x.Code).IsUnique();
        e.Property(x => x.Code).HasMaxLength(30);
        e.Property(x => x.Name).HasMaxLength(200);
        e.Property(x => x.SerialNo).HasMaxLength(100);
        e.Property(x => x.Location).HasMaxLength(200);
        e.Property(x => x.Note).HasMaxLength(500);
    }
}

internal sealed class InspectionDeviceCalibrationConfiguration : IEntityTypeConfiguration<InspectionDeviceCalibration>
{
    public void Configure(EntityTypeBuilder<InspectionDeviceCalibration> e)
    {
        e.HasIndex(x => new { x.InspectionDeviceId, x.CalibratedOn });
        e.Property(x => x.Result).HasMaxLength(500);
        e.HasOne(x => x.InspectionDevice).WithMany()
            .HasForeignKey(x => x.InspectionDeviceId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.PerformedBy).WithMany()
            .HasForeignKey(x => x.PerformedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ToolConfiguration : IEntityTypeConfiguration<Tool>
{
    public void Configure(EntityTypeBuilder<Tool> e)
    {
        e.HasIndex(x => x.Code).IsUnique();
        e.Property(x => x.Code).HasMaxLength(50);
        e.Property(x => x.Name).HasMaxLength(200);
        e.Property(x => x.ToolType).HasMaxLength(100);
    }
}

internal sealed class WorkCenterConfiguration : IEntityTypeConfiguration<WorkCenter>
{
    public void Configure(EntityTypeBuilder<WorkCenter> e)
    {
        e.HasIndex(x => x.Code).IsUnique();
        e.Property(x => x.Code).HasMaxLength(50);
        e.Property(x => x.Name).HasMaxLength(200);
        // 上位の資源は削除しない（無効化で運用する）ため、参照が残っている親を消せないようにする
        e.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> e)
    {
        e.HasIndex(x => x.Code).IsUnique();
        e.Property(x => x.Code).HasMaxLength(50);
        e.Property(x => x.ShelfNo).HasMaxLength(50);
        e.HasOne(x => x.WorkCenter).WithMany().HasForeignKey(x => x.WorkCenterId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ControlItemConfiguration : IEntityTypeConfiguration<ControlItem>
{
    public void Configure(EntityTypeBuilder<ControlItem> e)
    {
        e.HasIndex(x => x.Code).IsUnique();
        e.Property(x => x.Code).HasMaxLength(50);
        e.Property(x => x.Name).HasMaxLength(200);
        e.Property(x => x.Unit).HasMaxLength(30);
    }
}

internal sealed class InspectionItemConfiguration : IEntityTypeConfiguration<InspectionItem>
{
    public void Configure(EntityTypeBuilder<InspectionItem> e)
    {
        e.HasIndex(x => x.Code).IsUnique();
        e.Property(x => x.Code).HasMaxLength(50);
        e.Property(x => x.Name).HasMaxLength(200);
        e.Property(x => x.Method).HasMaxLength(500);
        e.HasOne(x => x.RequiredSkill).WithMany().HasForeignKey(x => x.RequiredSkillId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DefectReasonConfiguration : IEntityTypeConfiguration<DefectReason>
{
    public void Configure(EntityTypeBuilder<DefectReason> e)
    {
        e.HasIndex(x => x.Code).IsUnique();
        e.Property(x => x.Code).HasMaxLength(50);
        e.Property(x => x.Name).HasMaxLength(200);
    }
}

internal sealed class ChecklistConfiguration : IEntityTypeConfiguration<Checklist>
{
    public void Configure(EntityTypeBuilder<Checklist> e)
    {
        e.HasIndex(x => x.Code).IsUnique();
        e.Property(x => x.Code).HasMaxLength(50);
        e.Property(x => x.Name).HasMaxLength(200);
        e.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(x => x.ChecklistId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ChecklistItemConfiguration : IEntityTypeConfiguration<ChecklistItem>
{
    public void Configure(EntityTypeBuilder<ChecklistItem> e)
    {
        e.Property(x => x.Text).HasMaxLength(500);
    }
}

internal sealed class SkillMasterConfiguration : IEntityTypeConfiguration<SkillMaster>
{
    public void Configure(EntityTypeBuilder<SkillMaster> e)
    {
        e.ToTable("Skills");
        e.HasIndex(x => x.Code).IsUnique();
        e.Property(x => x.Code).HasMaxLength(50);
        e.Property(x => x.Name).HasMaxLength(200);
    }
}

internal sealed class UserSkillConfiguration : IEntityTypeConfiguration<UserSkill>
{
    public void Configure(EntityTypeBuilder<UserSkill> e)
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
    }
}
