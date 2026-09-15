using System.ComponentModel.DataAnnotations;
using MesApp.Core.Entities;

namespace MesApp.Core.Contracts.Maintenance;

// ---- 保全手順書（E-10-20）----

public record MaintenanceProcedureRequest(
    [Required, MaxLength(50)] string ProcedureNo,
    [Required, MaxLength(200)] string Title,
    int? TargetEquipmentId,
    int? TargetToolId,
    [Required, MaxLength(4000)] string Steps);

public record MaintenanceProcedureResponse(
    int Id, string ProcedureNo, string Title,
    int? TargetEquipmentId, string? TargetEquipmentName,
    int? TargetToolId, string? TargetToolName,
    string Steps, int Version, bool IsActive);

// ---- 設備稼働履歴（B-40-20、E-20-10）----

public record EquipmentLogRequest(
    int EquipmentId,
    EquipmentLogStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    /// <summary>停止原因（停止・故障時。B-40-20-02）</summary>
    string? StopCause,
    string? Note,
    /// <summary>この稼働区間で処理していた作業指示（任意。PQC×EQCの紐付け）</summary>
    int? WorkOrderId = null);

public record EquipmentLogResponse(
    int Id, int EquipmentId, string EquipmentName, EquipmentLogStatus Status,
    DateTimeOffset StartedAt, DateTimeOffset? EndedAt, string? StopCause, string? Note,
    int? WorkOrderId = null, string? WorkOrderNo = null);

/// <summary>設備別の稼働サマリ（E-20-10-03 稼働・停止実績、E-20-30-03 パフォーマンス確認）</summary>
public record EquipmentUtilizationRow(
    int EquipmentId, string AssetNo, string EquipmentName,
    decimal RunningHours, decimal StoppedHours, decimal SetupHours, decimal FailureHours,
    decimal IdleHours,
    int FailureCount,
    /// <summary>時間稼働率（%。稼働時間 ÷ 記録済み総時間）</summary>
    decimal UtilizationRate);

// ---- 保全計画（E-30-10）----

public record MaintenancePlanRequest(
    int EquipmentId,
    MaintenanceCategory Category,
    int PlanYear,
    DateOnly? ScheduledDate,
    int? CycleDays,
    string? Note);

public record MaintenancePlanResponse(
    int Id, int EquipmentId, string AssetNo, string EquipmentName,
    MaintenanceCategory Category, int PlanYear, DateOnly? ScheduledDate, int? CycleDays,
    MaintenancePlanStatus Status, string? Note, DateTimeOffset CreatedAt);

// ---- 保全指示・実績（E-30-20〜30、E-40、E-60-30）----

public record MaintenanceOrderCreateRequest(
    /// <summary>対象設備（設備保全時。治工具メンテ時はtoolIdを指定）</summary>
    int? EquipmentId,
    /// <summary>対象治工具（E-60-30）</summary>
    int? ToolId,
    /// <summary>元の保全計画（計画保全時。指定すると計画は指示発行済みになる）</summary>
    int? MaintenancePlanId,
    int? ProcedureId,
    DateOnly? ScheduledDate,
    MaintenanceRequestType RequestType,
    string? Note);

/// <summary>
/// 保全で消費した部材1行（E-40-30-01）。指定した現品を在庫から引き落とす。
/// 品目はロットから導けるので指定しない。
/// </summary>
public record MaintenanceRecordPartRequest(
    int LotId,
    int LocationId,
    [Range(0.0001, double.MaxValue)] decimal Quantity,
    [MaxLength(500)] string? Note);

public record MaintenanceRecordPartResponse(
    int Id, int ProductId, string ProductCode, string ProductName, string Unit,
    int LotId, string LotNumber, int LocationId, string LocationCode,
    decimal Quantity, string? Note);

/// <summary>保全実績登録（E-40-30-01。登録と同時に指示は完了になる）</summary>
public record MaintenanceRecordRequest(
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    /// <summary>消費部材・交換部品の自由記述（補足。在庫を動かす部材は Parts に入れる）</summary>
    string? PartsUsed,
    string? Result,
    string? Note,
    /// <summary>治工具メンテ完了時に寿命カウンタをリセットするか（E-60-30）</summary>
    bool ResetToolLife = false,
    /// <summary>消費した部材（在庫から引き落とす。未指定なら在庫は動かさない）</summary>
    List<MaintenanceRecordPartRequest>? Parts = null);

public record MaintenanceRecordResponse(
    int Id, string PerformedByUserId, string? PerformedByName,
    DateTimeOffset StartedAt, DateTimeOffset? EndedAt,
    string? PartsUsed, string? Result, string? Note,
    List<MaintenanceRecordPartResponse> Parts);

/// <summary>
/// 消耗材の消費実績サマリ（E-20-10-04 消耗材モニタリング）。
/// 期間内の保全実績で引き落とした部材を品目ごとに集計する。
/// </summary>
public record MaintenancePartConsumptionRow(
    int ProductId, string ProductCode, string ProductName, string Unit,
    decimal Quantity,
    /// <summary>消費した保全実績の件数</summary>
    int RecordCount,
    /// <summary>現在の在庫合計（発注・補充の判断に使う）</summary>
    decimal StockOnHand);

public record MaintenanceOrderResponse(
    int Id, string OrderNo,
    int? EquipmentId, string? EquipmentName, int? ToolId, string? ToolName,
    int? MaintenancePlanId, int? ProcedureId, string? ProcedureNo,
    DateOnly? ScheduledDate, MaintenanceRequestType RequestType, MaintenanceOrderStatus Status,
    string? Note, DateTimeOffset CreatedAt,
    List<MaintenanceRecordResponse> Records);

// ---- 治工具利用実績・寿命管理（E-60-20）----

public record ToolUsageRequest(
    int ToolId,
    int? WorkOrderId,
    [Range(0, int.MaxValue)] int UsageCount,
    decimal? UsageHours);

public record ToolUsageResponse(
    int Id, int ToolId, string ToolCode, int? WorkOrderId, string? WorkOrderNo,
    int UsageCount, decimal? UsageHours, DateTimeOffset RecordedAt);

/// <summary>治工具の寿命ステータス（E-60-20-02：閾値到達前の交換・廃棄通知）</summary>
public record ToolLifeStatusRow(
    int ToolId, string ToolCode, string ToolName, ToolStatus Status,
    int? LifeThresholdCount, int CumulativeCount,
    decimal? LifeThresholdHours, decimal CumulativeHours,
    /// <summary>寿命消化率（%。回数・時間のうち高い方。閾値未設定はnull）</summary>
    decimal? LifeUsageRate,
    /// <summary>閾値の80%到達（交換時期接近の警告）</summary>
    bool IsWarning,
    /// <summary>閾値到達（要交換・廃棄）</summary>
    bool IsLifeReached,
    DateTimeOffset? LifeResetAt);
