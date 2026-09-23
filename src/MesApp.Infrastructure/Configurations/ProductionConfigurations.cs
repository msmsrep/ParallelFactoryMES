using MesApp.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesApp.Infrastructure.Configurations;

// 指図・実績系とロット（Spec.md 5.2・5.3）

internal sealed class ManufacturingOrderConfiguration : IEntityTypeConfiguration<ManufacturingOrder>
{
    public void Configure(EntityTypeBuilder<ManufacturingOrder> e)
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
    }
}

internal sealed class WorkOrderStatusHistoryConfiguration : IEntityTypeConfiguration<WorkOrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<WorkOrderStatusHistory> e)
    {
        e.HasIndex(x => new { x.WorkOrderId, x.Id });
        e.Property(x => x.Note).HasMaxLength(500);
        e.Property(x => x.ChangedByUserId).HasMaxLength(450);
        e.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.ChangedBy).WithMany().HasForeignKey(x => x.ChangedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ManufacturingOrderMaterialConfiguration : IEntityTypeConfiguration<ManufacturingOrderMaterial>
{
    public void Configure(EntityTypeBuilder<ManufacturingOrderMaterial> e)
    {
        e.HasIndex(x => new { x.ManufacturingOrderId, x.ChildProductId }).IsUnique();
        e.Property(x => x.AlternativeGroup).HasMaxLength(50);
        e.HasOne(x => x.ManufacturingOrder)
            .WithMany(x => x.Materials)
            .HasForeignKey(x => x.ManufacturingOrderId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.ChildProduct)
            .WithMany()
            .HasForeignKey(x => x.ChildProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class WorkOrderControlItemConfiguration : IEntityTypeConfiguration<WorkOrderControlItem>
{
    public void Configure(EntityTypeBuilder<WorkOrderControlItem> e)
    {
        e.HasIndex(x => x.WorkOrderId);
        e.Property(x => x.ItemCode).HasMaxLength(50);
        e.Property(x => x.ItemName).HasMaxLength(200);
        e.Property(x => x.Unit).HasMaxLength(30);
        // 作業指示を消すと指示内容も一緒に消えるべき
        // 逆側のナビゲーション（WorkOrder.ControlItemSnapshots）を明示する。
        // WithMany() だけだとEFが規約で別の関連を作り、FK列が二重になる
        e.HasOne(x => x.WorkOrder).WithMany(w => w.ControlItemSnapshots)
            .HasForeignKey(x => x.WorkOrderId)
            .OnDelete(DeleteBehavior.Cascade);
        // マスタは無効化で運用するため、参照が残っている項目を消せないようにする
        e.HasOne(x => x.ControlItem).WithMany().HasForeignKey(x => x.ControlItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class WorkOrderConfiguration : IEntityTypeConfiguration<WorkOrder>
{
    public void Configure(EntityTypeBuilder<WorkOrder> e)
    {
        e.HasOne(x => x.WorkCenter).WithMany().HasForeignKey(x => x.WorkCenterId)
            .OnDelete(DeleteBehavior.Restrict);
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
        e.Property(x => x.ControlItems).HasMaxLength(1000);
        e.HasOne(x => x.RequiredSkill)
            .WithMany()
            .HasForeignKey(x => x.RequiredSkillId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.RoutingChecklist)
            .WithMany()
            .HasForeignKey(x => x.RoutingChecklistId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.WorkProcedure)
            .WithMany()
            .HasForeignKey(x => x.WorkProcedureId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.AssignedUser)
            .WithMany()
            .HasForeignKey(x => x.AssignedUserId)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasOne(x => x.AssignedEquipment)
            .WithMany()
            .HasForeignKey(x => x.AssignedEquipmentId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class LotConfiguration : IEntityTypeConfiguration<Lot>
{
    public void Configure(EntityTypeBuilder<Lot> e)
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
    }
}

internal sealed class LotGenealogyConfiguration : IEntityTypeConfiguration<LotGenealogy>
{
    public void Configure(EntityTypeBuilder<LotGenealogy> e)
    {
        e.HasIndex(x => x.ParentLotId);
        e.HasIndex(x => x.ChildLotId);
        e.Property(x => x.PerformedByUserId).HasMaxLength(450);
        e.HasOne(x => x.ParentLot)
            .WithMany()
            .HasForeignKey(x => x.ParentLotId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.ChildLot)
            .WithMany()
            .HasForeignKey(x => x.ChildLotId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LotStatusHistoryConfiguration : IEntityTypeConfiguration<LotStatusHistory>
{
    public void Configure(EntityTypeBuilder<LotStatusHistory> e)
    {
        e.HasIndex(x => new { x.LotId, x.Id });
        e.Property(x => x.Reason).HasMaxLength(500);
        e.Property(x => x.ChangedByUserId).HasMaxLength(450);
        e.HasOne(x => x.Lot)
            .WithMany()
            .HasForeignKey(x => x.LotId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
