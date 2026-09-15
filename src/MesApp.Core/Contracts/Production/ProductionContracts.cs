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
    decimal QuantityPer, decimal PlannedQuantity, string? AlternativeGroup);

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
