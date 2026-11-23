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

internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> e)
    {
        e.HasIndex(x => x.KeyHash).IsUnique();
        e.HasIndex(x => x.UserId);
        e.Property(x => x.KeyHash).HasMaxLength(64);
        e.Property(x => x.Prefix).HasMaxLength(20);
        e.Property(x => x.Name).HasMaxLength(100);
        // 利用者は削除せず無効化する運用なので、カスケードで監査の手がかりを消さない
        e.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IntegrationRequestConfiguration : IEntityTypeConfiguration<IntegrationRequest>
{
    public void Configure(EntityTypeBuilder<IntegrationRequest> e)
    {
        // 同じ呼び出し元・同じキーは1行だけ（同時に送られた2回目はここで止まる）
        e.HasIndex(x => new { x.Caller, x.IdempotencyKey }).IsUnique();
        e.HasIndex(x => x.ExpiresAtUnixSeconds);
        e.Property(x => x.Caller).HasMaxLength(100);
        e.Property(x => x.IdempotencyKey).HasMaxLength(100);
        e.Property(x => x.RequestHash).HasMaxLength(64);
    }
}

internal sealed class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
{
    public void Configure(EntityTypeBuilder<WebhookSubscription> e)
    {
        e.Property(x => x.Name).HasMaxLength(100);
        e.Property(x => x.Url).HasMaxLength(2000);
        e.Property(x => x.EventTypes).HasMaxLength(1000);
        e.Property(x => x.SecretProtected).HasMaxLength(1000);
        e.Property(x => x.SecretPrefix).HasMaxLength(20);
        // 送信先を消したら送信の記録も消す（運用ログ。消したことは監査ログに残る）
        e.HasMany(x => x.Deliveries)
            .WithOne(x => x.Subscription)
            .HasForeignKey(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WebhookOutboxConfiguration : IEntityTypeConfiguration<WebhookOutbox>
{
    public void Configure(EntityTypeBuilder<WebhookOutbox> e)
    {
        e.HasIndex(x => x.EventId).IsUnique();
        e.Property(x => x.EventType).HasMaxLength(100);
        e.Property(x => x.Summary).HasMaxLength(500);
        e.HasMany(x => x.Deliveries)
            .WithOne(x => x.Outbox)
            .HasForeignKey(x => x.OutboxId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> e)
    {
        // 常駐処理が「送信待ちで、送ってよい時刻を過ぎたもの」を引く
        e.HasIndex(x => new { x.State, x.NextAttemptAtUnixSeconds });
        e.Property(x => x.LastError).HasMaxLength(1000);
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
