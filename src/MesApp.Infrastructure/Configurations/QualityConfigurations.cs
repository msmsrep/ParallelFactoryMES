using MesApp.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesApp.Infrastructure.Configurations;

// 品質系（Spec.md 5.4）

internal sealed class InspectionResultConfiguration : IEntityTypeConfiguration<InspectionResult>
{
    public void Configure(EntityTypeBuilder<InspectionResult> e)
    {
        // 検査機は実績から参照されている間も無効化できる（マスタからの除外と記録の保持は別）
        e.HasOne(x => x.InspectionDevice).WithMany()
            .HasForeignKey(x => x.InspectionDeviceId).OnDelete(DeleteBehavior.Restrict);

        e.HasIndex(x => x.InspectionOrderId);
        e.Property(x => x.TextValue).HasMaxLength(500);
        e.Property(x => x.CorrectionNote).HasMaxLength(1000);
        e.HasOne(x => x.InspectionItem).WithMany().HasForeignKey(x => x.InspectionItemId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.InspectedBy).WithMany().HasForeignKey(x => x.InspectedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InspectionOrderConfiguration : IEntityTypeConfiguration<InspectionOrder>
{
    public void Configure(EntityTypeBuilder<InspectionOrder> e)
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
    }
}

internal sealed class InspectionOrderItemConfiguration : IEntityTypeConfiguration<InspectionOrderItem>
{
    public void Configure(EntityTypeBuilder<InspectionOrderItem> e)
    {
        e.HasIndex(x => new { x.InspectionOrderId, x.InspectionItemId }).IsUnique();
        e.Property(x => x.ItemCode).HasMaxLength(50);
        e.Property(x => x.ItemName).HasMaxLength(200);
        e.Property(x => x.Method).HasMaxLength(500);
        e.HasOne(x => x.InspectionItem).WithMany().HasForeignKey(x => x.InspectionItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InspectionResultCorrectionConfiguration : IEntityTypeConfiguration<InspectionResultCorrection>
{
    public void Configure(EntityTypeBuilder<InspectionResultCorrection> e)
    {
        e.HasIndex(x => new { x.InspectionOrderId, x.Id });
        e.HasIndex(x => x.InspectionResultId);
        e.Property(x => x.Reason).HasMaxLength(500);
        e.Property(x => x.BeforeTextValue).HasMaxLength(500);
        e.Property(x => x.AfterTextValue).HasMaxLength(500);
        e.Property(x => x.CorrectedByUserId).HasMaxLength(450);
        e.HasOne(x => x.InspectionResult).WithMany().HasForeignKey(x => x.InspectionResultId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.CorrectedBy).WithMany().HasForeignKey(x => x.CorrectedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class NonconformanceReportConfiguration : IEntityTypeConfiguration<NonconformanceReport>
{
    public void Configure(EntityTypeBuilder<NonconformanceReport> e)
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
    }
}

internal sealed class ShipmentJudgmentConfiguration : IEntityTypeConfiguration<ShipmentJudgment>
{
    public void Configure(EntityTypeBuilder<ShipmentJudgment> e)
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
    }
}
