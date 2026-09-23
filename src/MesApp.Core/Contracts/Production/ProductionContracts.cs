using System.ComponentModel.DataAnnotations;
using MesApp.Core.Entities;

namespace MesApp.Core.Contracts.Production;

// ---- 製造指図（A-20、B-10-10-04、B-70-10）----

public record CreateManufacturingOrderRequest(
    int ProductId,
    [Range(0.000001, double.MaxValue)] decimal Quantity,
    DateOnly? DueDate,
    ManufacturingOrderType OrderType,
    /// <summary>リワーク指図の場合は必須（元指図ID）</summary>
    int? SourceOrderId,
    string? Note);

public record UpdateManufacturingOrderRequest(
    [Range(0.000001, double.MaxValue)] decimal Quantity,
    DateOnly? DueDate,
    string? Note);

public record ManufacturingOrderResponse(
    int Id, string OrderNo, int ProductId, string ProductCode, string ProductName,
    decimal Quantity, DateOnly? DueDate, ManufacturingOrderType OrderType,
    ManufacturingOrderStatus Status, string? ApprovedByUserId, DateTimeOffset? ApprovedAt,
    int? SourceOrderId, string? OutputLotNumber, string? Note, DateTimeOffset CreatedAt);

/// <summary>指図の予定材料（展開時にMBOMから固定。Spec.md 5.7）</summary>
public record OrderMaterialResponse(
    int ChildProductId, string ProductCode, string ProductName,
    decimal QuantityPer, decimal PlannedQuantity, string? AlternativeGroup,
    int? RoutingSequence = null);

public record ManufacturingOrderDetailResponse(
    ManufacturingOrderResponse Order,
    List<WorkOrderResponse> WorkOrders,
    List<OrderMaterialResponse>? Materials = null);

/// <summary>工程展開（B-10-10-01）。産出ロット番号は未指定なら自動採番（品目コード-日付-連番。B-10-10-05）</summary>
public record ExpandRequest(string? LotNumber);

// ---- 作業指示・差立（B-10-20）----

/// <summary>
/// 工程別の進捗集計（B-60-10-01）。作業指示の状態ごとの件数をDB側で数えた結果。
/// </summary>
public record ProcessProgressRow(
    int ProcessId, string ProcessCode, string ProcessName,
    int Created, int Dispatched, int Started, int Completed, int Approved);

/// <summary>
/// 作業指示の工程管理項目（展開時点のスナップショット。B-30-30-04）。
/// 実績の逸脱判定はこの指示値・許容範囲を基準にする
/// </summary>
public record WorkOrderControlItemResponse(
    int Id, int? ControlItemId, string ItemCode, string ItemName, string? Unit,
    int ItemVersion, decimal? TargetValue, decimal? LowerLimit, decimal? UpperLimit);

/// <summary>
/// 作業指示の作業手順書（SOP。B-10-30-03）。
/// <para>
/// 本文は<b>マスタの現在値</b>を返す。安全上の訂正のように改訂した手順は仕掛中の作業指示にも
/// 届くべきだからで、代わりに展開時点の版数（<c>PlannedVersion</c>）を併せて返し、
/// 計画時から改訂されたか（<c>IsRevised</c>）を画面と監査に示す。
/// </para>
/// </summary>
public record WorkOrderProcedureResponse(
    int WorkProcedureId, string ProcedureNo, string Title, string Steps, string? Reference,
    /// <summary>マスタの現在の版数（表示している手順の版数）</summary>
    int CurrentVersion,
    /// <summary>指図展開時点の版数（計画時に想定していた手順の版数）</summary>
    int? PlannedVersion,
    /// <summary>計画時から手順書が改訂されているか</summary>
    bool IsRevised,
    /// <summary>手順書が無効化されているか（改訂中・廃止の可能性がある）</summary>
    bool IsActive);

public record WorkOrderResponse(
    int Id, string WorkOrderNo, int ManufacturingOrderId, string OrderNo,
    int ProductId, string ProductCode, string ProductName,
    int ProcessId, string ProcessCode, string ProcessName,
    int RoutingSequence, decimal PlannedQuantity, int? DispatchOrder,
    string? AssignedUserId, string? AssignedUserName,
    int? AssignedEquipmentId, string? AssignedEquipmentName,
    WorkOrderStatus Status,
    /// <summary>展開時点の工順スナップショット（標準作業時間・分）</summary>
    decimal StandardWorkMinutes = 0,
    /// <summary>展開時点の工順スナップショット（標準段取り時間・分）</summary>
    decimal StandardSetupMinutes = 0,
    /// <summary>展開時点の工順スナップショット（工程管理項目）</summary>
    string? ControlItems = null);

/// <summary>作業指示の状態履歴1件（Spec.md 5.2 WorkOrderStatusHistory）</summary>
public record WorkOrderStatusHistoryEntry(
    WorkOrderStatus FromStatus, WorkOrderStatus ToStatus, WorkOrderStatusChangeSource Source,
    string? Note, string? ChangedByName, DateTimeOffset ChangedAt);

/// <summary>
/// 差立で選べる候補設備（B-10-20-02）。工順に候補が登録されていない工程では空を返し、
/// 画面はその場合に設備マスタ全件から選ばせる
/// </summary>
public record WorkOrderEquipmentCandidate(int EquipmentId, string AssetNo, string Name, bool IsActive);

/// <summary>差立：作業員割当（スキル照合 F-20-30-01）・設備割当・着手順（B-10-20-01〜03）</summary>
public record DispatchRequest(
    string? AssignedUserId,
    int? AssignedEquipmentId,
    int? DispatchOrder);

// ---- 進捗モニタリング（A-30-10-01、A-30-20-01）----

public record OrderProgressResponse(
    int Id, string OrderNo, string ProductCode, string ProductName,
    decimal Quantity, DateOnly? DueDate, ManufacturingOrderType OrderType,
    ManufacturingOrderStatus Status,
    int WorkOrderCount, int CompletedWorkOrderCount,
    /// <summary>納期超過（未完了かつ納期が業務日付を過ぎている）</summary>
    bool IsOverdue);

// ---- 生産性モニタリング（B-60-10-05）----

/// <summary>
/// 品目別・工程別の生産性（歩留まり・直行率）。
/// <para>
/// 直行率は「手直しを経ずに一度で良品になった割合」、歩留まりは「リワークでの救済を含めた
/// 最終的な良品の割合」。リワークは別の指図（<see cref="ManufacturingOrderType.Rework"/>）として
/// 実績が付くため、通常・突発の産出を分母に、リワークの良品を歩留まりの分子にだけ足す。
/// </para>
/// </summary>
public record ProductivityRow(
    string Key,
    /// <summary>通常・突発指図の良品数</summary>
    decimal GoodQuantity,
    /// <summary>通常・突発指図の不良数</summary>
    decimal DefectQuantity,
    /// <summary>リワーク指図で良品になった数（歩留まりの分子にだけ入る）</summary>
    decimal ReworkGoodQuantity,
    /// <summary>直行率（%）＝ 良品数 ÷ (良品数＋不良数)</summary>
    decimal FirstPassRate,
    /// <summary>歩留まり（%）＝ (良品数＋リワーク良品数) ÷ (良品数＋不良数)</summary>
    decimal YieldRate);

/// <summary>
/// 標準時間の予実（作業指示単位）。予定＝標準段取り時間＋標準作業時間×計画数量で、
/// いずれも指図展開時に固定した工順の値を使う（マスタの現在値で引き直さない）
/// </summary>
public record StandardTimeVarianceRow(
    int WorkOrderId, string WorkOrderNo, string ProductCode, string ProcessCode,
    decimal PlannedQuantity,
    decimal PlannedMinutes,
    /// <summary>実績時間（分）＝ 直接作業時間＋段取り実績時間（いずれも終了済みの記録のみ）</summary>
    decimal ActualMinutes,
    /// <summary>予定に対する超過率（%）。予定が0分の工程では判定できないためnull</summary>
    decimal? VarianceRate);

public record ProductivitySummaryResponse(
    ProductivityRow Total,
    List<ProductivityRow> ByProduct,
    List<ProductivityRow> ByProcess,
    /// <summary>期間内に実績のあった作業指示の予実（超過率の大きい順）</summary>
    List<StandardTimeVarianceRow> TimeVariances);

// ---- 製造リードタイム（B-60-10-05。ガイド 8.3.2 の納期実績の見える化）----

/// <summary>
/// 完了した製造指図1件のリードタイム。着手は最初の作業記録（段取り・生産実績の開始時刻）、
/// 完了は最後の生産実績の終了時刻。日数は製造日の差（同じ製造日に着手・完了すれば0日）
/// </summary>
public record LeadTimeOrderRow(
    int ManufacturingOrderId, string OrderNo, string ProductCode, string ProductName, decimal Quantity,
    DateTimeOffset StartedAt, DateTimeOffset CompletedAt,
    DateOnly StartedOn, DateOnly CompletedOn,
    int LeadTimeDays,
    /// <summary>着手から完了までの経過時間（時間）</summary>
    decimal LeadTimeHours,
    DateOnly? DueDate,
    /// <summary>納期に対する遅れ（日）。完了の製造日−納期。0以下は納期内。納期なしはnull</summary>
    int? DelayDays,
    /// <summary>異常値（第3四分位＋1.5×四分位範囲を超える）。個別に原因を調べる対象</summary>
    bool IsOutlier);

/// <summary>リードタイムの度数分布の1区切り（日数ごとの完了件数。うち納期遅れの件数）</summary>
public record LeadTimeBucket(int Days, int Count, int LateCount);

/// <summary>
/// 製造リードタイムの分布（期間は完了の製造日。リワーク指図は含めない）。
/// 平均だけではばらつきと異常値が見えないため、分布と四分位から見た異常値を返す
/// </summary>
public record LeadTimeResponse(
    int OrderCount,
    decimal? AverageDays, decimal? MedianDays, int? Percentile90Days, int? MaxDays,
    /// <summary>異常値とみなすしきい値（日）。4件未満では求めない</summary>
    decimal? OutlierThresholdDays,
    int OnTimeCount, int LateCount, int NoDueDateCount,
    List<LeadTimeBucket> Distribution,
    /// <summary>リードタイムの長い順</summary>
    List<LeadTimeOrderRow> Orders);

// ---- 遅延検知（A-30-20-01）----

/// <summary>遅れの種類</summary>
public enum WorkOrderDelayKind
{
    /// <summary>指図の納期を過ぎているのに完了していない</summary>
    OverdueDueDate,

    /// <summary>着手済みだが、経過時間が予定時間をしきい値以上超えている</summary>
    OverrunStandardTime,
}

/// <summary>
/// 遅れている作業指示（A-30-20-01 納期遅延・トラブル発生時の状況把握）。
/// <para>
/// 通知の仕組み（メール・プッシュ）は持たないため、画面に出すところまでを担う。
/// 予定時間は指図展開時に固定した工順の値（標準段取り時間＋標準作業時間×計画数量）。
/// </para>
/// </summary>
public record WorkOrderDelayRow(
    int WorkOrderId, string WorkOrderNo, string OrderNo,
    string ProductCode, string ProcessCode,
    WorkOrderStatus Status,
    WorkOrderDelayKind Kind,
    DateOnly? DueDate,
    /// <summary>納期からの超過日数（納期超過のみ）</summary>
    int? OverdueDays,
    /// <summary>着手時刻（標準時間超過のみ）</summary>
    DateTimeOffset? StartedAt,
    decimal PlannedMinutes,
    decimal ElapsedMinutes,
    /// <summary>予定に対する超過率（%。標準時間超過のみ）</summary>
    decimal? OverrunPercent);
