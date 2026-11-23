namespace MesApp.Core.Constants;

/// <summary>
/// Webhook のイベントの種類（Spec.md 3.8 Webhook。docs-dev/WebhookTasks.md の「v1 のイベント」）。
/// <para>
/// 名前は「対象.出来事」。<b>外部との約束</b>なので、名前は消さない・変えない（CSV の列の約束と同じ）。
/// 送信先の購読（API）と画面の選択肢で同じ一覧を使う。
/// </para>
/// </summary>
public static class WebhookEventTypes
{
    /// <summary>生産実績を記録した（B-40-10）</summary>
    public const string ProductionResultCreated = "production-result.created";

    /// <summary>生産実績を訂正した（B-70-30-01）</summary>
    public const string ProductionResultCorrected = "production-result.corrected";

    /// <summary>作業指示の状態が変わった</summary>
    public const string WorkOrderStatusChanged = "work-order.status-changed";

    /// <summary>製造指図が完了・取消になった</summary>
    public const string ManufacturingOrderStatusChanged = "manufacturing-order.status-changed";

    /// <summary>ロットの状態が変わった（保留・解除・廃棄など）</summary>
    public const string LotStatusChanged = "lot.status-changed";

    /// <summary>不適合を起票した</summary>
    public const string NonconformanceCreated = "nonconformance.created";

    /// <summary>設備が停止・故障の区間に入った</summary>
    public const string EquipmentStopped = "equipment.stopped";

    /// <summary>購読できる種類の一覧（画面に出す順）</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        ProductionResultCreated,
        ProductionResultCorrected,
        WorkOrderStatusChanged,
        ManufacturingOrderStatusChanged,
        LotStatusChanged,
        NonconformanceCreated,
        EquipmentStopped,
    ];

    /// <summary>購読の保存形式（カンマ区切り）を一覧に戻す。知らない名前は捨てない（版を戻したときに購読を失わないため）</summary>
    public static List<string> Split(string stored) =>
        stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
