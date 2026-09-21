namespace MesApp.Core.Entities;

/// <summary>
/// 監査ログ（Spec.md 7.6：誰が・いつ・何を変更したか。実績訂正・検査訂正・マスタ変更・シート端末操作は必須記録）
/// </summary>
public class AuditLog
{
    public long Id { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 記録日。<see cref="Timestamp"/> と重複するが、**期間で絞り込むために必要**。
    /// SQLiteは DateTimeOffset の比較・並べ替えをSQLへ変換できず、
    /// 「この期間の監査ログ」を全件読み出さずに引くにはこの列が要る（Spec.md 7.6の参照API）。
    /// <para>
    /// UTCではなく<b>工場のローカル日付</b>（<c>BusinessDay:TimeZone</c>。未設定ならサーバーの
    /// ローカルタイム）で持つ。画面は記録時刻をローカル時刻で表示するため、UTCの日付で絞ると
    /// 「9月3日の朝の操作が9月2日で引っかかる」ことになり、見え方と食い違う。
    /// <para>
    /// 製造日（3.9節の6時境界）ではなく<b>暦日</b>。監査ログは業務の1日ではなく法定の保存年数で
    /// 数えるので、夜勤を前日へ寄せる境界は当てない（保持期間の判定も同じ暦日で行う）。
    /// </para>
    /// </para>
    /// </summary>
    public DateOnly RecordedOn { get; set; }

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
