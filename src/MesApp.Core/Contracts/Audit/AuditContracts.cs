namespace MesApp.Core.Contracts.Audit;

/// <summary>
/// 監査ログ1件（Spec.md 7.6）。<paramref name="Detail"/> は要約文字列か
/// <c>{ before, after, reason }</c> のJSON文字列で、画面はそのまま表示する
/// （型を決め打ちにすると、操作ごとに違う中身を出せなくなる）。
/// </summary>
public record AuditLogResponse(
    long Id,
    DateTimeOffset Timestamp,
    string? UserId,
    string? UserName,
    string Category,
    string Action,
    string? TargetType,
    string? TargetId,
    string? Detail,
    string? IpAddress);

/// <summary>
/// 監査ログの絞り込み条件（Spec.md 7.6）。すべて任意で、指定した条件はANDで効く。
/// </summary>
/// <remarks>
/// 期間は業務日付ではなく記録時刻（UTC）の日付で受ける。監査は「いつ操作されたか」を
/// そのまま追うものであり、夜勤の日跨ぎをまとめる業務日付（3.9節）とは目的が違う。
/// </remarks>
public record AuditLogQuery
{
    /// <summary>この日以降（その日の00:00 UTCから）</summary>
    public DateOnly? From { get; init; }

    /// <summary>この日まで（その日を含む＝翌日00:00 UTCの手前まで）</summary>
    public DateOnly? To { get; init; }

    /// <summary>機能分類（Auth / Master / Inventory / Quality など）</summary>
    public string? Category { get; init; }

    /// <summary>操作内容（Create / Update / Approve / Correct など）</summary>
    public string? Action { get; init; }

    /// <summary>対象エンティティ型名（WorkOrder / Lot など）</summary>
    public string? TargetType { get; init; }

    /// <summary>対象エンティティのID</summary>
    public string? TargetId { get; init; }

    /// <summary>操作したユーザーID</summary>
    public string? UserId { get; init; }
}

/// <summary>
/// 絞り込み用の分類・操作の候補（画面のドロップダウン用）。
/// 記録がある組み合わせだけを件数付きで返す（定数を並べても、その環境で実際に出る値とは限らないため）。
/// </summary>
public record AuditCategoryOption(string Category, string Action, int Count);
