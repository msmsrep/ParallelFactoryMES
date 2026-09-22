using MesApp.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesApp.Infrastructure.Configurations;

// 生産計画系（Spec.md 5.2 ProductionPlan。A-30-10-01）

internal sealed class ProductionPlanConfiguration : IEntityTypeConfiguration<ProductionPlan>
{
    public void Configure(EntityTypeBuilder<ProductionPlan> e)
    {
        // 作業区は NULL を許すため、作業区なしの行どうしの重複はDBの一意索引では止まらない
        // （SQLite・PostgreSQL は NULL を互いに別の値とみなし、SQL Server は NULL の行を索引の対象から外す）。
        // 重複の判定はAPI側で NULL も同じ値として行い、この索引は作業区ありの行の最後の砦とする
        e.HasIndex(x => new { x.BusinessDate, x.ProductId, x.ProcessId, x.WorkCenterId }).IsUnique();
        e.Property(x => x.Note).HasMaxLength(500);
        // 計画が残っているうちは品目・工程・作業区を消させない（マスタは無効化で扱う）
        e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Process).WithMany().HasForeignKey(x => x.ProcessId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.WorkCenter).WithMany().HasForeignKey(x => x.WorkCenterId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
