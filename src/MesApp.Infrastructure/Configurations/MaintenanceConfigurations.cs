using MesApp.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesApp.Infrastructure.Configurations;

// 設備保全系（Spec.md 5.1 MaintenanceProcedure、5.5）

internal sealed class ToolIssueConfiguration : IEntityTypeConfiguration<ToolIssue>
{
    public void Configure(EntityTypeBuilder<ToolIssue> e)
    {
        e.HasIndex(x => new { x.ToolId, x.Status });
        e.HasIndex(x => x.WorkOrderId);
        e.Property(x => x.Note).HasMaxLength(500);
        e.HasOne(x => x.Tool).WithMany()
            .HasForeignKey(x => x.ToolId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.WorkOrder).WithMany()
            .HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.IssuedTo).WithMany()
            .HasForeignKey(x => x.IssuedToUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MaintenanceProcedureConfiguration : IEntityTypeConfiguration<MaintenanceProcedure>
{
    public void Configure(EntityTypeBuilder<MaintenanceProcedure> e)
    {
        e.HasIndex(x => x.ProcedureNo).IsUnique();
        e.Property(x => x.ProcedureNo).HasMaxLength(50);
        e.Property(x => x.Title).HasMaxLength(200);
        e.Property(x => x.Steps).HasMaxLength(4000);
        e.HasOne(x => x.TargetEquipment).WithMany().HasForeignKey(x => x.TargetEquipmentId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.RequiredSkill).WithMany().HasForeignKey(x => x.RequiredSkillId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.TargetTool).WithMany().HasForeignKey(x => x.TargetToolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EquipmentLogConfiguration : IEntityTypeConfiguration<EquipmentLog>
{
    public void Configure(EntityTypeBuilder<EquipmentLog> e)
    {
        e.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.EquipmentId);
        e.Property(x => x.StopCause).HasMaxLength(500);
        e.Property(x => x.Note).HasMaxLength(500);
        e.HasOne(x => x.Equipment).WithMany().HasForeignKey(x => x.EquipmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MaintenancePlanConfiguration : IEntityTypeConfiguration<MaintenancePlan>
{
    public void Configure(EntityTypeBuilder<MaintenancePlan> e)
    {
        e.HasIndex(x => new { x.EquipmentId, x.PlanYear });
        e.Property(x => x.Note).HasMaxLength(1000);
        e.HasOne(x => x.Equipment).WithMany().HasForeignKey(x => x.EquipmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MaintenanceOrderConfiguration : IEntityTypeConfiguration<MaintenanceOrder>
{
    public void Configure(EntityTypeBuilder<MaintenanceOrder> e)
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
    }
}

internal sealed class MaintenanceRecordConfiguration : IEntityTypeConfiguration<MaintenanceRecord>
{
    public void Configure(EntityTypeBuilder<MaintenanceRecord> e)
    {
        e.Property(x => x.PartsUsed).HasMaxLength(1000);
        e.Property(x => x.Result).HasMaxLength(2000);
        e.Property(x => x.Note).HasMaxLength(1000);
        e.HasOne(x => x.PerformedBy).WithMany().HasForeignKey(x => x.PerformedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.Parts).WithOne().HasForeignKey(x => x.MaintenanceRecordId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MaintenanceRecordPartConfiguration : IEntityTypeConfiguration<MaintenanceRecordPart>
{
    public void Configure(EntityTypeBuilder<MaintenanceRecordPart> e)
    {
        e.HasIndex(x => x.ProductId);
        e.Property(x => x.Quantity).HasPrecision(18, 4);
        e.Property(x => x.Note).HasMaxLength(500);
        // 現品の履歴なので、参照先のマスタ・ロットは消させない
        e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ToolUsageConfiguration : IEntityTypeConfiguration<ToolUsage>
{
    public void Configure(EntityTypeBuilder<ToolUsage> e)
    {
        e.HasIndex(x => x.ToolId);
        e.HasOne(x => x.Tool).WithMany().HasForeignKey(x => x.ToolId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
