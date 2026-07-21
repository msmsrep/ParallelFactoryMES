namespace MesApp.Core.Entities;

/// <summary>
/// リフレッシュトークン（Spec.md 7.4：アクセストークン短命＋リフレッシュトークン。DBにはハッシュのみ保存）
/// </summary>
public class RefreshToken
{
    public long Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public AppUser? User { get; set; }

    /// <summary>トークン本体のSHA-256ハッシュ（Base64）。平文は保存しない。</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>失効日時（ログアウト・ローテーションで設定）</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsActive => RevokedAt is null && DateTimeOffset.UtcNow < ExpiresAt;
}
