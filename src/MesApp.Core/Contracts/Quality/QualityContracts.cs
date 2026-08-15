using System.ComponentModel.DataAnnotations;
using MesApp.Core.Entities;

namespace MesApp.Core.Contracts.Quality;

// ---- 検査指示・実績・判定（C-20）----

public record InspectionOrderCreateRequest(
    InspectionOrderType Type,
    /// <summary>対象ロット（受入/完成品/サンプル/再検査で必須）</summary>
    int? TargetLotId,
    /// <summary>対象作業指示（工程内検査で必須）</summary>
    int? TargetWorkOrderId,
    /// <summary>検査項目ID（未指定なら種別・対象品目/工程に合致する有効な検査基準を自動選択）</summary>
    List<int>? ItemIds,
    string? Note);

public record InspectionResultRequest(
    int InspectionItemId,
    int? SampleNo,
    /// <summary>測定値（定量検査。規格値と照合して自動判定）</summary>
    decimal? MeasuredValue,
    /// <summary>定性検査の記録</summary>
    string? TextValue,
    /// <summary>判定（測定値からの自動判定ができない場合は必須）</summary>
    InspectionJudgment? Judgment);

/// <summary>検査実績の訂正（C-20-50-07。理由必須・監査ログ記録）</summary>
public record InspectionResultCorrectionRequest(
    decimal? MeasuredValue,
    string? TextValue,
    InspectionJudgment Judgment,
    [Required] string Reason);

/// <summary>総合判定（C-20-10-04）。グレード指定で対象ロットのグレードも設定（C-60-10-01）</summary>
public record InspectionJudgeRequest(string? Grade);

public record InspectionOrderItemResponse(
    int InspectionItemId, string Code, string Name,
    decimal? LowerLimit, decimal? UpperLimit, decimal? StandardValue,
    string? Method, int? SamplingCount);

public record InspectionResultResponse(
    int Id, int InspectionItemId, string ItemCode, string ItemName, int SampleNo,
    decimal? MeasuredValue, string? TextValue, InspectionJudgment Judgment,
    string InspectedByUserId, string? InspectedByName, DateTimeOffset InspectedAt,
    string? CorrectionNote);

public record InspectionOrderResponse(
    int Id, string OrderNo, InspectionOrderType Type, InspectionOrderStatus Status,
    int? TargetLotId, string? TargetLotNumber, int? TargetWorkOrderId, string? TargetWorkOrderNo,
    InspectionJudgment? OverallJudgment, DateTimeOffset? JudgedAt,
    string? ApprovedByUserId, DateTimeOffset? ApprovedAt, string? Note, DateTimeOffset CreatedAt,
    List<InspectionOrderItemResponse> Items,
    List<InspectionResultResponse> Results);

// ---- 不適合・逸脱（B-40-30、C-30）----

public record NonconformanceCreateRequest(
    NonconformanceSource Source,
    int? LotId,
    int? WorkOrderId,
    int? InspectionOrderId,
    [Required, MaxLength(2000)] string Content,
    string? CauseCategory,
    string? CauseDetail);

/// <summary>対応指示（C-30-20-01。保留/廃棄はロットの在庫ステータスへ、リワークはリワーク指図へ連動）</summary>
public record NonconformanceActionRequest(
    NonconformanceAction Action,
    string? Instruction);

/// <summary>対応実行記録（C-30-20-02）</summary>
public record NonconformanceActionRecordRequest([Required, MaxLength(2000)] string Record);

public record NonconformanceResponse(
    int Id, string ReportNo, NonconformanceSource Source, NonconformanceStatus Status,
    int? LotId, string? LotNumber, int? WorkOrderId, string? WorkOrderNo,
    int? InspectionOrderId, string Content, string? CauseCategory, string? CauseDetail,
    NonconformanceAction? Action, string? ActionInstruction, DateTimeOffset? ActionInstructedAt,
    int? ReworkOrderId, string? ReworkOrderNo,
    string? ActionRecord, DateTimeOffset? ActionCompletedAt,
    string? ApprovedByUserId, DateTimeOffset? ApprovedAt,
    string? ReportedByUserId, string? ReportedByName, DateTimeOffset CreatedAt);

// ---- 出荷判定（H-10-10）----

public record ShipmentJudgmentCreateRequest(
    /// <summary>対象ロット（ロット単位の判定）</summary>
    int? LotId,
    /// <summary>対象出荷指示（出荷実行のゲートになる）</summary>
    int? ShippingOrderId,
    ShipmentJudgmentResult Result,
    string? Note);

public record ShipmentJudgmentResponse(
    int Id, string JudgmentNo, int? LotId, string? LotNumber,
    int? ShippingOrderId, string? ShippingNo,
    ShipmentJudgmentResult Result,
    string JudgedByUserId, string? JudgedByName, DateTimeOffset JudgedAt,
    string? ApprovedByUserId, DateTimeOffset? ApprovedAt, string? Note);

// ---- トレーサビリティ（H-30-10）----

/// <summary>トレースツリーのノード（ロット＋関連作業指示・数量。childrenで連鎖を表す）</summary>
public record TraceNode(
    int LotId, string LotNumber, string ProductCode, string ProductName,
    LotStockStatus StockStatus,
    /// <summary>この連鎖を作った作業指示（投入/産出）</summary>
    string? WorkOrderNo,
    /// <summary>投入/産出数量</summary>
    decimal? Quantity,
    /// <summary>分割・振替による派生ロットか</summary>
    bool IsLineage,
    List<TraceNode> Children);

public record TraceResponse(
    int LotId, string LotNumber, string ProductCode, string ProductName,
    List<TraceNode> Nodes);

/// <summary>ロットの履歴閲覧（H-30-10-03〜05：製造・検査・在庫履歴）</summary>
public record LotHistoryResponse(
    int LotId, string LotNumber, string ProductCode, string ProductName,
    List<string> ProductionHistory,
    List<string> InspectionHistory,
    List<string> InventoryHistory);

// ---- 品質分析（C-40-10）----

public record DefectSummaryRow(string Key, decimal GoodQuantity, decimal DefectQuantity, decimal DefectRate);

public record QualitySummaryResponse(
    /// <summary>品目別の良品・不良集計（C-40-10-03）</summary>
    List<DefectSummaryRow> ByProduct,
    /// <summary>工程別の良品・不良集計</summary>
    List<DefectSummaryRow> ByProcess,
    /// <summary>不適合の原因区分別件数</summary>
    Dictionary<string, int> NonconformanceByCause,
    /// <summary>検査の合否件数（判定済みのみ）</summary>
    int InspectionPassCount,
    int InspectionFailCount,
    /// <summary>未クローズの不適合件数（C-40-10-05）</summary>
    int OpenNonconformanceCount);
