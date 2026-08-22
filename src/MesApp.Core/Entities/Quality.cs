namespace MesApp.Core.Entities;

/// <summary>検査指示（Spec.md 5.4 InspectionOrder。C-20）</summary>
public class InspectionOrder
{
    public int Id { get; set; }

    /// <summary>検査指示番号（一意。INyyyyMMdd-連番）</summary>
    public string OrderNo { get; set; } = string.Empty;

    public InspectionOrderType Type { get; set; }

    /// <summary>対象ロット（受入/完成品/サンプル/再検査。工程内でも任意指定可）</summary>
    public int? TargetLotId { get; set; }
    public Lot? TargetLot { get; set; }

    /// <summary>対象作業指示（工程内検査）</summary>
    public int? TargetWorkOrderId { get; set; }
    public WorkOrder? TargetWorkOrder { get; set; }

    public InspectionOrderStatus Status { get; set; } = InspectionOrderStatus.Instructed;

    /// <summary>依頼者（C-20-10-02）</summary>
    public string? RequestedByUserId { get; set; }
    public AppUser? RequestedBy { get; set; }

    /// <summary>総合判定（判定時に設定。C-20-10-04）</summary>
    public InspectionJudgment? OverallJudgment { get; set; }
    public string? JudgedByUserId { get; set; }
    public DateTimeOffset? JudgedAt { get; set; }

    /// <summary>承認者（C-20-10-06）</summary>
    public string? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>検査項目セット（指示作成時点の基準のスナップショット）</summary>
    public List<InspectionOrderItem> Items { get; set; } = [];

    public List<InspectionResult> Results { get; set; } = [];
}

/// <summary>
/// 検査指示の対象検査項目（Spec.md 5.4）。
/// <para>
/// 検査基準（<see cref="InspectionItem"/>）は改訂され、規格値は上書きされる。最新マスタで
/// 過去ロットを判定・印字すると当時の判定根拠を再現できないため、**指示発行時点の基準を
/// ここへ写して保持**する。自動判定・画面表示・検査成績書はこのスナップショットを使い、
/// マスタは新規指示の作成時にのみ参照する（Spec.md 5.7）。
/// </para>
/// </summary>
public class InspectionOrderItem
{
    public int Id { get; set; }

    public int InspectionOrderId { get; set; }

    /// <summary>基準の参照元（マスタ側の改訂履歴を辿るための参照であり、判定には使わない）</summary>
    public int InspectionItemId { get; set; }
    public InspectionItem? InspectionItem { get; set; }

    // ---- 指示発行時点のスナップショット（以降マスタが改訂されても変わらない）----

    /// <summary>検査項目コード</summary>
    public string ItemCode { get; set; } = string.Empty;

    /// <summary>検査項目名</summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>参照した基準の版数（C-10-10-03）</summary>
    public int ItemVersion { get; set; }

    public decimal? LowerLimit { get; set; }

    public decimal? UpperLimit { get; set; }

    public decimal? StandardValue { get; set; }

    public string? Method { get; set; }

    public int? SamplingCount { get; set; }
}

/// <summary>検査実績（Spec.md 5.4 InspectionResult。C-20）</summary>
public class InspectionResult
{
    public int Id { get; set; }

    public int InspectionOrderId { get; set; }

    public int InspectionItemId { get; set; }
    public InspectionItem? InspectionItem { get; set; }

    /// <summary>サンプル番号（サンプリング検査で同一項目を複数測定する場合）</summary>
    public int SampleNo { get; set; } = 1;

    /// <summary>測定値（定量検査）</summary>
    public decimal? MeasuredValue { get; set; }

    /// <summary>測定値（定性検査の記録）</summary>
    public string? TextValue { get; set; }

    /// <summary>判定（規格値との照合による自動判定または手動判定。C-20-10-04）</summary>
    public InspectionJudgment Judgment { get; set; }

    public string InspectedByUserId { get; set; } = string.Empty;
    public AppUser? InspectedBy { get; set; }

    public DateTimeOffset InspectedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>訂正履歴メモ（C-20-50-07。訂正時に理由を追記）</summary>
    public string? CorrectionNote { get; set; }
}

/// <summary>不適合・逸脱（Spec.md 5.4 NonconformanceReport。B-40-30、C-30）</summary>
public class NonconformanceReport
{
    public int Id { get; set; }

    /// <summary>管理番号（一意。NCyyyyMMdd-連番）</summary>
    public string ReportNo { get; set; } = string.Empty;

    public NonconformanceSource Source { get; set; }

    public int? LotId { get; set; }
    public Lot? Lot { get; set; }

    public int? WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    /// <summary>発生元の検査指示（検査由来の場合）</summary>
    public int? InspectionOrderId { get; set; }
    public InspectionOrder? InspectionOrder { get; set; }

    /// <summary>逸脱・不具合内容</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>原因区分（選択式。B-40-30-02）</summary>
    public string? CauseCategory { get; set; }

    /// <summary>原因詳細（文章。写真添付は将来拡張）</summary>
    public string? CauseDetail { get; set; }

    /// <summary>対応指示（リワーク/保留/廃棄/特採。C-30-20-01）</summary>
    public NonconformanceAction? Action { get; set; }
    public string? ActionInstruction { get; set; }
    public string? ActionInstructedByUserId { get; set; }
    public DateTimeOffset? ActionInstructedAt { get; set; }

    /// <summary>リワーク対応時に発行したリワーク指図（Spec.md 5.7 不適合の連鎖）</summary>
    public int? ReworkOrderId { get; set; }
    public ManufacturingOrder? ReworkOrder { get; set; }

    /// <summary>対応実績（C-30-20-02）</summary>
    public string? ActionRecord { get; set; }
    public string? ActionCompletedByUserId { get; set; }
    public DateTimeOffset? ActionCompletedAt { get; set; }

    /// <summary>承認者（逸脱承認・特採承認。C-30-20-03）</summary>
    public string? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }

    public NonconformanceStatus Status { get; set; } = NonconformanceStatus.Open;

    public string? ReportedByUserId { get; set; }
    public AppUser? ReportedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>出荷判定（Spec.md 5.4 ShipmentJudgment。H-10-10）</summary>
public class ShipmentJudgment
{
    public int Id { get; set; }

    /// <summary>判定書番号（一意。SJyyyyMMdd-連番）</summary>
    public string JudgmentNo { get; set; } = string.Empty;

    /// <summary>対象ロット</summary>
    public int? LotId { get; set; }
    public Lot? Lot { get; set; }

    /// <summary>対象出荷指示（出荷実行のゲート判定に使用）</summary>
    public int? ShippingOrderId { get; set; }
    public ShippingOrder? ShippingOrder { get; set; }

    public ShipmentJudgmentResult Result { get; set; }

    public string JudgedByUserId { get; set; } = string.Empty;
    public AppUser? JudgedBy { get; set; }

    public DateTimeOffset JudgedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>承認者（H-10-10-03）</summary>
    public string? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }

    public string? Note { get; set; }
}
