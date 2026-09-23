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
    InspectionJudgment? Judgment,
    /// <summary>測定に使った検査機（任意。校正期限切れの機器は登録できない。C-20-50-03）</summary>
    int? InspectionDeviceId = null);

/// <summary>検査実績の訂正（C-20-50-07。理由必須・監査ログ記録）</summary>
public record InspectionResultCorrectionRequest(
    decimal? MeasuredValue,
    string? TextValue,
    InspectionJudgment Judgment,
    [Required] string Reason);

/// <summary>総合判定（C-20-10-04）。グレード指定で対象ロットのグレードも設定（C-60-10-01）</summary>
public record InspectionJudgeRequest(string? Grade);

/// <summary>
/// 検査指示の対象項目。規格値・項目名は**指示発行時点のスナップショット**であり、
/// マスタ改訂後もこの検査の判定根拠は変わらない（Spec.md 5.4）
/// </summary>
public record InspectionOrderItemResponse(
    int InspectionItemId, string Code, string Name,
    /// <summary>参照した検査基準の版数（C-10-10-03）</summary>
    int ItemVersion,
    decimal? LowerLimit, decimal? UpperLimit, decimal? StandardValue,
    string? Method, int? SamplingCount);

public record InspectionResultResponse(
    int Id, int InspectionItemId, string ItemCode, string ItemName, int SampleNo,
    decimal? MeasuredValue, string? TextValue, InspectionJudgment Judgment,
    string InspectedByUserId, string? InspectedByName, DateTimeOffset InspectedAt,
    string? CorrectionNote,
    /// <summary>測定に使った検査機（C-20-50-03。成績書に出す）</summary>
    int? InspectionDeviceId = null, string? InspectionDeviceCode = null,
    string? InspectionDeviceName = null);

/// <summary>
/// 検査実績の訂正履歴1件（C-20-50-07）。訂正前の記録を残したまま、
/// 誰がいつ何を根拠に直したかを示す（Spec.md 5.4 InspectionResultCorrection）
/// </summary>
public record InspectionResultCorrectionResponse(
    int InspectionResultId, string ItemCode, string ItemName, int SampleNo,
    decimal? BeforeMeasuredValue, string? BeforeTextValue, InspectionJudgment BeforeJudgment,
    decimal? AfterMeasuredValue, string? AfterTextValue, InspectionJudgment AfterJudgment,
    string Reason, string? CorrectedByName, DateTimeOffset CorrectedAt);

public record InspectionOrderResponse(
    int Id, string OrderNo, InspectionOrderType Type, InspectionOrderStatus Status,
    int? TargetLotId, string? TargetLotNumber, int? TargetWorkOrderId, string? TargetWorkOrderNo,
    InspectionJudgment? OverallJudgment, DateTimeOffset? JudgedAt,
    string? ApprovedByUserId, DateTimeOffset? ApprovedAt, string? Note, DateTimeOffset CreatedAt,
    List<InspectionOrderItemResponse> Items,
    List<InspectionResultResponse> Results,
    List<InspectionResultCorrectionResponse> Corrections);

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
    /// <summary>投入/産出数量、または系譜の関係数量</summary>
    decimal? Quantity,
    /// <summary>系譜（分割・統合・振替）による関係の場合はその区分。投入/産出の連鎖ならnull</summary>
    LotRelationType? Relation,
    List<TraceNode> Children);

public record TraceResponse(
    int LotId, string LotNumber, string ProductCode, string ProductName,
    List<TraceNode> Nodes);

/// <summary>ロット状態履歴の1件（保留・解除などの遷移。Spec.md 5.3 LotStatusHistory）</summary>
public record LotStatusHistoryEntry(
    LotStockStatus FromStatus, LotStockStatus ToStatus, LotStatusChangeSource Source,
    string? Reason, string? ChangedByName, DateTimeOffset ChangedAt);

/// <summary>生産実績の訂正履歴1件（B-70-30-01）</summary>
public record ProductionCorrectionEntry(
    string WorkOrderNo,
    decimal BeforeGoodQuantity, decimal BeforeDefectQuantity,
    decimal BeforeScrapQuantity, decimal BeforeReworkQuantity,
    decimal AfterGoodQuantity, decimal AfterDefectQuantity,
    decimal AfterScrapQuantity, decimal AfterReworkQuantity,
    string Reason, string? CorrectedByName, DateTimeOffset CorrectedAt);

/// <summary>ロットの履歴閲覧（H-30-10-03〜05：製造・検査・在庫・状態・訂正履歴）</summary>
public record LotHistoryResponse(
    int LotId, string LotNumber, string ProductCode, string ProductName,
    List<string> ProductionHistory,
    List<string> InspectionHistory,
    List<string> InventoryHistory,
    List<LotStatusHistoryEntry> StatusHistory,
    List<ProductionCorrectionEntry> CorrectionHistory,
    /// <summary>設備稼働履歴（H-30-10-04）。このロットを産出した作業指示に紐づく稼働区間</summary>
    List<string> EquipmentHistory,
    /// <summary>製造条件の逸脱（B-30-30-04）。許容範囲から外れた記録だけを出す</summary>
    List<string> ControlItemDeviations);

// ---- 品質分析（C-40-10）----

/// <summary>
/// 品目別・工程別の出来高集計（C-40-10-03）。廃棄・再作業待ちは不良数の内訳であり、
/// 不良率は従来どおり 不良数 ÷ (良品数＋不良数) で算出する
/// </summary>
public record DefectSummaryRow(
    string Key, decimal GoodQuantity, decimal DefectQuantity,
    decimal ScrapQuantity, decimal ReworkQuantity, decimal DefectRate);

/// <summary>
/// 不良理由別の集計（C-40-10-01 不良項目別分析）。
/// 数量の多い順に並べ、累積構成比を持たせることでパレート図として読める
/// （ガイド 4.4.3 のQC七つ道具。上位いくつで全体の何割かが分かると改善対象を選べる）
/// </summary>
public record DefectReasonSummaryRow(
    string Code, string Name, DefectReasonCategory Category, decimal Quantity, decimal Share,
    /// <summary>累積構成比（%）。この行までの構成比の合計</summary>
    decimal CumulativeShare = 0);

public record QualitySummaryResponse(
    /// <summary>品目別の良品・不良集計（C-40-10-03）</summary>
    List<DefectSummaryRow> ByProduct,
    /// <summary>工程別の良品・不良集計</summary>
    List<DefectSummaryRow> ByProcess,
    /// <summary>
    /// 直別の良品・不良集計（C-40-10-03。夜勤と昼勤の不良率を比べる）。
    /// 直は実績の記録時に固定した値を使う。直を登録していない実績は「（直なし）」にまとまる
    /// </summary>
    List<DefectSummaryRow> ByShift,
    /// <summary>不良理由別の集計（C-40-10-01。数量の多い順）</summary>
    List<DefectReasonSummaryRow> ByDefectReason,
    /// <summary>不適合の原因区分別件数</summary>
    Dictionary<string, int> NonconformanceByCause,
    /// <summary>検査の合否件数（判定済みのみ）</summary>
    int InspectionPassCount,
    int InspectionFailCount,
    /// <summary>未クローズの不適合件数（C-40-10-05）</summary>
    int OpenNonconformanceCount);

// ---- 管理図・工程能力（C-50-10-01〜02）----

/// <summary>
/// 管理図の1点（1群）。群は「1つの検査指示で同じ検査項目を測った測定値の集まり」。
/// 個別値管理図（群の大きさ1）では <see cref="Range"/> は直前の点との移動範囲で、先頭の点は null
/// </summary>
public record ControlChartPoint(
    string OrderNo,
    /// <summary>群の最初の測定日時</summary>
    DateTimeOffset InspectedAt,
    /// <summary>測定日時が属する製造日</summary>
    DateOnly BusinessDate,
    int SampleCount,
    /// <summary>群の平均（個別値管理図では測定値そのもの）</summary>
    decimal Mean,
    /// <summary>範囲 R（個別値管理図では移動範囲 Rs）</summary>
    decimal? Range,
    /// <summary>平均が管理限界の外</summary>
    bool MeanOutOfControl,
    /// <summary>範囲が管理限界の外</summary>
    bool RangeOutOfControl,
    /// <summary>中心線の同じ側に9点以上続く並びの中にある（JIS Z 9020-2 のルール2）</summary>
    bool InRun);

/// <summary>
/// 管理図と工程能力指数（C-50-10-01〜02。Spec.md 3.3）。
/// 群の数が2未満のときは管理限界・工程能力を null で返す（点だけ描く）。
/// 規格の片側しか無い項目は Cp を null にし、Cpk は片側で求める
/// </summary>
public record ControlChartResponse(
    int InspectionItemId, string ItemCode, string ItemName,
    /// <summary>個別値－移動範囲管理図（X-Rs）か。false なら X̄-R 管理図</summary>
    bool IsIndividuals,
    /// <summary>群の大きさ（使った群の大きさ。最も多い大きさに揃え、違う大きさの群は除外する）</summary>
    int SubgroupSize,
    decimal? CenterLine, decimal? UpperControlLimit, decimal? LowerControlLimit,
    decimal? RangeCenterLine, decimal? RangeUpperControlLimit, decimal? RangeLowerControlLimit,
    /// <summary>規格の下限・上限（最も新しい群の検査指示が発行時に写した値）</summary>
    decimal? LowerSpecLimit, decimal? UpperSpecLimit,
    /// <summary>期間内で規格値が改訂されている（工程能力は最新の規格で求めている）</summary>
    bool SpecLimitsChanged,
    /// <summary>群内のばらつきから推定した標準偏差（R̄/d2）</summary>
    decimal? Sigma,
    decimal? Cp, decimal? Cpk,
    /// <summary>群の大きさが揃わないため除外した群の数</summary>
    int ExcludedSubgroupCount,
    List<ControlChartPoint> Points);
