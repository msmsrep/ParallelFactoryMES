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

    /// <summary>
    /// 詳細。実績訂正・在庫操作など**変更前後を追跡したい操作はJSON**で格納する
    /// （例：<c>{"before":{...},"after":{...},"reason":"..."}</c>）。
    /// 作成・削除など要約で足りる操作は要約文字列を格納する
    /// </summary>
    public string? Detail { get; set; }

    public string? IpAddress { get; set; }
}
