namespace MesApp.Core.Entities;

/// <summary>
/// 監査ログ（Spec.md 7.6：誰が・いつ・何を変更したか。実績訂正・検査訂正・マスタ変更・シート端末操作は必須記録）
/// </summary>
public class AuditLog
{
    public long Id { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>操作ユーザーID（未認証操作＝初期セットアップ等はnull）</summary>
    public string? UserId { get; set; }

    /// <summary>操作時点のユーザー名（ユーザー削除後も追跡できるよう非正規化して保持）</summary>
    public string? UserName { get; set; }

    /// <summary>機能分類（Auth / Setup / Master / Production / Inventory / Quality / Equipment / License など）</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>操作内容（Login / Create / Update / Delete / Approve / Correct など）</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>対象エンティティ型名</summary>
    public string? TargetType { get; set; }

    /// <summary>対象エンティティのID</summary>
    public string? TargetId { get; set; }

    /// <summary>詳細（変更前後の値などをJSONで格納）</summary>
    public string? Detail { get; set; }

    public string? IpAddress { get; set; }
}
