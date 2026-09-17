using MesApp.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesApp.Infrastructure.Configurations;

// 製造実行系（Spec.md 5.2）

internal sealed class SetupRecordConfiguration : IEntityTypeConfiguration<SetupRecord>
{
    public void Configure(EntityTypeBuilder<SetupRecord> e)
    {
        e.HasIndex(x => x.WorkOrderId);
        e.Property(x => x.AbnormalityNote).HasMaxLength(1000);
        e.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.PerformedBy).WithMany().HasForeignKey(x => x.PerformedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ChecklistRecordConfiguration : IEntityTypeConfiguration<ChecklistRecord>
{
    public void Configure(EntityTypeBuilder<ChecklistRecord> e)
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
    }
}

internal sealed class ChecklistResultItemConfiguration : IEntityTypeConfiguration<ChecklistResultItem>
{
    public void Configure(EntityTypeBuilder<ChecklistResultItem> e)
    {
        e.Property(x => x.Note).HasMaxLength(500);
        e.HasOne(x => x.ChecklistItem).WithMany().HasForeignKey(x => x.ChecklistItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MaterialConsumptionConfiguration : IEntityTypeConfiguration<MaterialConsumption>
{
    public void Configure(EntityTypeBuilder<MaterialConsumption> e)
    {
        e.Property(x => x.SubstituteReason).HasMaxLength(500);
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
    }
}

internal sealed class ProductionRecordConfiguration : IEntityTypeConfiguration<ProductionRecord>
{
    public void Configure(EntityTypeBuilder<ProductionRecord> e)
    {
        e.HasOne(x => x.Shift).WithMany().HasForeignKey(x => x.ShiftId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.WorkOrderId);
        e.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.PerformedBy).WithMany().HasForeignKey(x => x.PerformedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.OutputLot).WithMany().HasForeignKey(x => x.OutputLotId)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasMany(x => x.Defects).WithOne(x => x.ProductionRecord!)
            .HasForeignKey(x => x.ProductionRecordId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ProductionDefectConfiguration : IEntityTypeConfiguration<ProductionDefect>
{
    public void Configure(EntityTypeBuilder<ProductionDefect> e)
    {
        e.HasIndex(x => x.ProductionRecordId);
        e.HasIndex(x => x.DefectReasonId);
        e.Property(x => x.Note).HasMaxLength(500);
        e.HasOne(x => x.DefectReason).WithMany().HasForeignKey(x => x.DefectReasonId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductionRecordCorrectionConfiguration : IEntityTypeConfiguration<ProductionRecordCorrection>
{
    public void Configure(EntityTypeBuilder<ProductionRecordCorrection> e)
    {
        e.HasIndex(x => new { x.WorkOrderId, x.Id });
        e.HasIndex(x => x.ProductionRecordId);
        e.Property(x => x.Reason).HasMaxLength(500);
        e.Property(x => x.CorrectedByUserId).HasMaxLength(450);
        e.HasOne(x => x.ProductionRecord).WithMany().HasForeignKey(x => x.ProductionRecordId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.CorrectedBy).WithMany().HasForeignKey(x => x.CorrectedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ProductionDataRecordConfiguration : IEntityTypeConfiguration<ProductionDataRecord>
{
    public void Configure(EntityTypeBuilder<ProductionDataRecord> e)
    {
        e.HasOne(x => x.WorkOrderControlItem).WithMany().HasForeignKey(x => x.WorkOrderControlItemId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.WorkOrderId);
        e.Property(x => x.Item).HasMaxLength(100);
        e.Property(x => x.Value).HasMaxLength(500);
        e.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WorkTimeRecordConfiguration : IEntityTypeConfiguration<WorkTimeRecord>
{
    public void Configure(EntityTypeBuilder<WorkTimeRecord> e)
    {
        e.HasIndex(x => new { x.UserId, x.StartedAt });
        e.Property(x => x.IndirectCategory).HasMaxLength(100);
        e.Property(x => x.Note).HasMaxLength(500);
        e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class TroubleReportConfiguration : IEntityTypeConfiguration<TroubleReport>
{
    public void Configure(EntityTypeBuilder<TroubleReport> e)
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
    }
}

internal sealed class TransferOrderConfiguration : IEntityTypeConfiguration<TransferOrder>
{
    public void Configure(EntityTypeBuilder<TransferOrder> e)
    {
        e.HasIndex(x => x.Status);
        e.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.FromLocation).WithMany().HasForeignKey(x => x.FromLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.ToLocation).WithMany().HasForeignKey(x => x.ToLocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
