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
