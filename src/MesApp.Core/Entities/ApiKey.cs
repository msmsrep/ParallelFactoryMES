namespace MesApp.Core.Entities;

/// <summary>
/// 連携API のAPIキー（Spec.md 7.4 連携APIの認証。docs-dev/IntegrationApiTasks.md INT-01）。
/// <para>
/// キーは MES の利用者に紐づき、権限はその利用者のロールで判定する（キー独自の権限は持たない）。
/// 平文は発行時に一度だけ返し、DBにはSHA-256のハッシュだけを保存する（<see cref="RefreshToken"/> と同じ）。
/// 削除はせず、失効（<see cref="RevokedAt"/>）で使えなくする（いつ誰のキーで呼ばれたかを監査ログから追えるように）。
/// </para>
/// </summary>
public class ApiKey
{
    public int Id { get; set; }

    /// <summary>用途（例：販売管理からの指図取込）。一覧で見分けるための名前</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>キーで呼んだときの利用者。権限・記録者・監査ログはこの利用者になる</summary>
    public string UserId { get; set; } = string.Empty;
    public AppUser? User { get; set; }

    /// <summary>キー本体のSHA-256ハッシュ（Base64）。平文は保存しない</summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>キーの先頭（例 <c>pfm_AbCd1234</c>）。一覧で手元のキーと見分けるために残す。これだけでは認証できない</summary>
    public string Prefix { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>発行した利用者（システム管理者）</summary>
    public string? CreatedByUserId { get; set; }

    /// <summary>有効期限（任意。null なら期限なし）</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>最後に使われた日時（書き込みを減らすため1分単位でしか更新しない）</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>失効日時。設定したら二度と使えない（戻す操作は持たない。必要なら新しく発行する）</summary>
    public DateTimeOffset? RevokedAt { get; set; }
}
