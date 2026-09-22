namespace MesApp.Core.Entities;

/// <summary>
/// 生産計画（Spec.md 5.2 ProductionPlan。A-30-10-01 生産進捗管理モニタリングの「予」）。
/// 製造日×品目×工程（作業区は任意）ごとの計画数量を持つ。
/// <para>
/// MESは計画を立てない（A-10 は対象外）。外から来た計画を受け取り、実績と突き合わせるための入れ物にする。
/// 依存は計画 → 既存実績の一方向で、製造指図・作業指示・実績からは計画を参照しない
/// （指図に紐付けず、品目・工程・作業区・製造日で突き合わせる）。
/// </para>
/// </summary>
public class ProductionPlan
{
    public int Id { get; set; }

    /// <summary>製造日（Spec.md 3.9。暦日ではなく境界時刻で区切った業務日付）</summary>
    public DateOnly BusinessDate { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>工程（実績を工程ごとの出来高で数えるため、キーに含める）</summary>
    public int ProcessId { get; set; }
    public ProcessMaster? Process { get; set; }

    /// <summary>作業区（任意。段は問わない。指定したときは同じキーの一部になる）</summary>
    public int? WorkCenterId { get; set; }
    public WorkCenter? WorkCenter { get; set; }

    /// <summary>計画数量（0 は計画上の休止を表す。負の値は持たない）</summary>
    public decimal PlannedQuantity { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
