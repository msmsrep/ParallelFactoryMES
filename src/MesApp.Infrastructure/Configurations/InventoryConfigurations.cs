using MesApp.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesApp.Infrastructure.Configurations;

// 在庫・物流系（Spec.md 5.3）

internal sealed class SampleStorageConfiguration : IEntityTypeConfiguration<SampleStorage>
{
    public void Configure(EntityTypeBuilder<SampleStorage> e)
    {
        e.HasIndex(x => x.SampleNo).IsUnique();
        e.Property(x => x.SampleNo).HasMaxLength(50);
        e.Property(x => x.Note).HasMaxLength(500);
        e.Property(x => x.Quantity).HasPrecision(18, 4);
        // 採取元ロット・保管場所・品目は履歴として残すため消させない
        e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.StorageLocation).WithMany().HasForeignKey(x => x.StorageLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.InspectionOrder).WithMany().HasForeignKey(x => x.InspectionOrderId)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(x => new { x.Status, x.RetainUntil });
    }
}

internal sealed class InventoryStockConfiguration : IEntityTypeConfiguration<InventoryStock>
{
    public void Configure(EntityTypeBuilder<InventoryStock> e)
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
    }
}

internal sealed class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> e)
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
    }
}

internal sealed class PickingOrderConfiguration : IEntityTypeConfiguration<PickingOrder>
{
    public void Configure(EntityTypeBuilder<PickingOrder> e)
    {
        e.HasIndex(x => x.OrderNo).IsUnique();
        e.Property(x => x.OrderNo).HasMaxLength(50);
        e.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasOne(x => x.ShippingOrder).WithMany().HasForeignKey(x => x.ShippingOrderId)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PickingOrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PickingLineConfiguration : IEntityTypeConfiguration<PickingLine>
{
    public void Configure(EntityTypeBuilder<PickingLine> e)
    {
        e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ShippingOrderConfiguration : IEntityTypeConfiguration<ShippingOrder>
{
    public void Configure(EntityTypeBuilder<ShippingOrder> e)
    {
        e.HasIndex(x => x.ShippingNo).IsUnique();
        e.Property(x => x.ShippingNo).HasMaxLength(50);
        e.Property(x => x.Destination).HasMaxLength(200);
        e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ShippingOrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ShippingLineConfiguration : IEntityTypeConfiguration<ShippingLine>
{
    public void Configure(EntityTypeBuilder<ShippingLine> e)
    {
        e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StocktakeConfiguration : IEntityTypeConfiguration<Stocktake>
{
    public void Configure(EntityTypeBuilder<Stocktake> e)
    {
        e.HasIndex(x => x.StocktakeNo).IsUnique();
        e.Property(x => x.StocktakeNo).HasMaxLength(50);
        e.HasOne(x => x.TargetLocation).WithMany().HasForeignKey(x => x.TargetLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.StocktakeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class StocktakeLineConfiguration : IEntityTypeConfiguration<StocktakeLine>
{
    public void Configure(EntityTypeBuilder<StocktakeLine> e)
    {
        e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
