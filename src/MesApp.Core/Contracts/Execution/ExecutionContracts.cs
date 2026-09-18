using System.ComponentModel.DataAnnotations;
using MesApp.Core.Entities;

namespace MesApp.Core.Contracts.Execution;

// ---- 段取り実績（B-20-50、B-40-40）----

public record SetupRecordRequest(
    SetupType Type,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string? AbnormalityNote);

public record SetupRecordResponse(
    int Id, int WorkOrderId, SetupType Type,
    DateTimeOffset StartedAt, DateTimeOffset? EndedAt,
    string PerformedByUserId, string? PerformedByName, string? AbnormalityNote);

// ---- チェックリスト実施（B-30-10）----

public record ChecklistResultRequest(int ChecklistItemId, bool IsChecked, string? Note);

public record ChecklistRecordRequest(
    int ChecklistId,
    List<ChecklistResultRequest> Results);

public record ChecklistResultResponse(int ChecklistItemId, string Text, bool IsRequired, bool IsChecked, string? Note);

public record ChecklistRecordResponse(
    int Id, int ChecklistId, string ChecklistCode, string ChecklistName,
    int? WorkOrderId, int? EquipmentId,
    string PerformedByUserId, string? PerformedByName, DateTimeOffset PerformedAt,
    List<ChecklistResultResponse> Results);

// ---- 部材投入（B-30-20、B-40-10-08）----

public record ConsumptionRequest(
    int LotId,
    int LocationId,
    [Range(0.000001, double.MaxValue)] decimal Quantity,
    /// <summary>代替部品を投入する場合の理由（予定材料の代替行を投入するときは必須。A-40-10-04）</summary>
    [MaxLength(500)] string? SubstituteReason = null);

public record ConsumptionResponse(
    int Id, int WorkOrderId, int ProductId, string ProductCode, string ProductName,
    int LotId, string LotNumber, int? LocationId, decimal Quantity,
    DateTimeOffset ConsumedAt, ConsumptionMethod Method,
    bool IsSubstitute = false, string? SubstituteReason = null);

// ---- 生産実績（B-30-30、B-40-10）----

/// <summary>不良理由別の内訳1件（C-40-10-01。合計は不良数を超えられない）</summary>
public record ProductionDefectRequest(
    int DefectReasonId,
    [Range(0, double.MaxValue)] decimal Quantity,
    [MaxLength(500)] string? Note = null);

public record ProductionDefectResponse(
    int DefectReasonId, string DefectReasonCode, string DefectReasonName,
    decimal Quantity, string? Note);

public record ProductionRecordRequest(
    [Range(0, double.MaxValue)] decimal GoodQuantity,
    [Range(0, double.MaxValue)] decimal DefectQuantity,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    /// <summary>入庫先ロケーション（最終工程の実績で必須。在庫計上 B-40-10-02）</summary>
    int? OutputLocationId,
    /// <summary>バックフラッシュ実行（MBOM×(良品+不良)数量の部材を自動消費。B-40-10-09）</summary>
    bool Backflush,
    /// <summary>廃棄数（不良数の内訳。省略時0）</summary>
    [Range(0, double.MaxValue)] decimal ScrapQuantity = 0,
    /// <summary>再作業待ち数（不良数の内訳。省略時0）</summary>
    [Range(0, double.MaxValue)] decimal ReworkQuantity = 0,
    /// <summary>不良理由別の内訳（省略可。合計は不良数を超えられない）</summary>
    List<ProductionDefectRequest>? Defects = null);

public record ProductionRecordResponse(
    int Id, int WorkOrderId, string WorkOrderNo,
    string PerformedByUserId, string? PerformedByName,
    decimal GoodQuantity, decimal DefectQuantity,
    decimal ScrapQuantity, decimal ReworkQuantity,
    DateTimeOffset StartedAt, DateTimeOffset? EndedAt,
    int? OutputLotId, string? OutputLotNumber, int? OutputLocationId,
    string? ApprovedByUserId, DateTimeOffset? ApprovedAt,
    List<ProductionDefectResponse>? Defects = null,
    /// <summary>記録時に固定した直（ShiftCode・ShiftName は画面表示用の付随情報）</summary>
    int? ShiftId = null, string? ShiftCode = null, string? ShiftName = null);

/// <summary>製造履歴訂正（B-70-30-01。権限制御＋監査ログ。訂正理由必須）</summary>
public record ProductionRecordCorrectionRequest(
    [Range(0, double.MaxValue)] decimal GoodQuantity,
    [Range(0, double.MaxValue)] decimal DefectQuantity,
    [Required] string Reason,
    [Range(0, double.MaxValue)] decimal ScrapQuantity = 0,
    [Range(0, double.MaxValue)] decimal ReworkQuantity = 0);

// ---- 製造条件データ（B-30-30-04）----

public record DataRecordRequest(
    [Required, MaxLength(100)] string Item,
    [Required, MaxLength(500)] string Value,
    /// <summary>対応する工程管理項目の指示（作業指示のスナップショットのId）。指定すると逸脱を判定する</summary>
    int? WorkOrderControlItemId = null,
    /// <summary>判定に使う数値（指示を指定したときは必須）</summary>
    decimal? NumericValue = null);

public record DataRecordResponse(
    int Id, int WorkOrderId, string Item, string Value, DateTimeOffset RecordedAt,
    int? WorkOrderControlItemId = null, decimal? NumericValue = null,
    /// <summary>true=逸脱、false=範囲内、null=判定していない</summary>
    bool? IsDeviation = null,
    decimal? TargetValue = null, decimal? LowerLimit = null, decimal? UpperLimit = null);

// ---- 作業時間記録（B-30-30-02、F-30-20）----

public record WorkTimeRequest(
    WorkTimeType Type,
    string? IndirectCategory,
    int? WorkOrderId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string? Note);

public record WorkTimeResponse(
    int Id, string UserId, string? UserName, WorkTimeType Type, string? IndirectCategory,
    int? WorkOrderId, string? WorkOrderNo,
    DateTimeOffset StartedAt, DateTimeOffset? EndedAt, string? Note);

// ---- トラブル報告（B-40-10-06、B-60-10-02〜04）----

public record TroubleReportRequest(
    DateTimeOffset OccurredAt,
    TroubleCategory Category,
    int? WorkOrderId,
    int? EquipmentId,
    [Required, MaxLength(2000)] string Content);

/// <summary>対応履歴の追記と状態更新（B-60-10-03〜04）</summary>
public record TroubleUpdateRequest(
    string? ResponseNote,
    TroubleStatus Status);

public record TroubleReportResponse(
    int Id, DateTimeOffset OccurredAt, TroubleCategory Category,
    int? WorkOrderId, string? WorkOrderNo, int? EquipmentId, string? EquipmentName,
    string Content, string? ResponseHistory, TroubleStatus Status,
    string ReportedByUserId, string? ReportedByName, DateTimeOffset CreatedAt);

// ---- 搬送・移動指示（B-50-10）----

public record TransferOrderRequest(
    int LotId,
    [Range(0.000001, double.MaxValue)] decimal Quantity,
    int FromLocationId,
    int ToLocationId);

public record TransferOrderResponse(
    int Id, int LotId, string LotNumber, string ProductCode, decimal Quantity,
    int FromLocationId, string FromLocationCode, int ToLocationId, string ToLocationCode,
    TransferOrderStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? ExecutedAt);
