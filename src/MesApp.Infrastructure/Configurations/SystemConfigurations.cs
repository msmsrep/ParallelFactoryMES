using MesApp.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesApp.Infrastructure.Configurations;

// 共通（採番・認証・監査ログ。Spec.md 7章）

internal sealed class NumberSequenceConfiguration : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> e)
    {
        // プレフィックス1つにつき1行。ここが重複すると同じ番号を二重に払い出すことになる
        e.HasIndex(x => x.Prefix).IsUnique();
        e.Property(x => x.Prefix).HasMaxLength(60);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> e)
    {
        e.HasIndex(x => x.TokenHash).IsUnique();
        e.HasIndex(x => x.UserId);
        e.Property(x => x.TokenHash).HasMaxLength(64);
        e.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> e)
    {
        e.HasIndex(x => x.Timestamp);
        e.HasIndex(x => x.RecordedOn);
        e.HasIndex(x => new { x.Category, x.Action });
        // 「この指図の履歴」「このユーザーの操作」を引くための索引（Spec.md 7.6の参照API）
        e.HasIndex(x => new { x.TargetType, x.TargetId });
        e.HasIndex(x => x.UserId);
        e.Property(x => x.Category).HasMaxLength(50);
        e.Property(x => x.Action).HasMaxLength(50);
        e.Property(x => x.TargetType).HasMaxLength(100);
        e.Property(x => x.TargetId).HasMaxLength(100);
    }
}
