using System.ComponentModel.DataAnnotations;
using MesApp.Core.Entities;

namespace MesApp.Core.Contracts.Masters;

// ---- 品目（Product）----

public record ProductRequest(
    [Required, MaxLength(50)] string Code,
    [Required, MaxLength(200)] string Name,
    [Required, MaxLength(20)] string Unit,
    string? Specification,
    ProductType Type,
    [Range(0, 100)] decimal StandardDefectRate);

public record ProductResponse(
    int Id, string Code, string Name, string Unit, string? Specification,
    ProductType Type, decimal StandardDefectRate, bool IsActive);

// ---- MBOM（BomItem）----

public record BomItemRequest(
    int ChildProductId,
    [Range(0.000001, double.MaxValue)] decimal QuantityPer,
    MakeOrBuy MakeOrBuy,
    string? AlternativeGroup,
    /// <summary>代替部品か（同一グループ内の主材料でない行。投入時に理由の記録を求める）</summary>
    bool IsAlternative = false);

public record BomItemResponse(
    int Id, int ChildProductId, string ChildProductCode, string ChildProductName,
    decimal QuantityPer, MakeOrBuy MakeOrBuy, string? AlternativeGroup,
    bool IsAlternative = false);

// ---- 工程（Process）----

public record ProcessRequest(
    [Required, MaxLength(50)] string Code,
    [Required, MaxLength(200)] string Name,
    MakeOrBuy Category);

public record ProcessResponse(int Id, string Code, string Name, MakeOrBuy Category, bool IsActive);

// ---- 工順/BOP（Routing）----

public record RoutingStepRequest(
    [Range(1, int.MaxValue)] int Sequence,
    int ProcessId,
    [Range(0, double.MaxValue)] decimal StandardWorkMinutes,
    [Range(0, double.MaxValue)] decimal StandardSetupMinutes,
    int? RequiredSkillId,
    int? EquipmentId,
    int? ToolId,
    string? ControlItems,
    int? ChecklistId,
    int? WorkCenterId = null);

public record RoutingStepResponse(
    int Id, int Sequence, int ProcessId, string ProcessCode, string ProcessName,
    decimal StandardWorkMinutes, decimal StandardSetupMinutes,
    int? RequiredSkillId, string? RequiredSkillName,
    int? EquipmentId, int? ToolId, string? ControlItems, int? ChecklistId,
    int? WorkCenterId = null, string? WorkCenterCode = null, string? WorkCenterName = null);

// ---- 設備（Equipment）----

public record EquipmentRequest(
    [Required, MaxLength(50)] string AssetNo,
    [Required, MaxLength(200)] string Name,
    string? Site,
    EquipmentStatus Status,
    MaintenanceType MaintenanceType,
    decimal? MaintenanceThreshold,
    string? MaintenanceParts,
    int? WorkCenterId = null);

/// <summary>WorkCenterCode・WorkCenterName は画面表示用の付随情報（更新は WorkCenterId で行う）</summary>
public record EquipmentResponse(
    int Id, string AssetNo, string Name, string? Site, EquipmentStatus Status,
    MaintenanceType MaintenanceType, decimal? MaintenanceThreshold, string? MaintenanceParts, bool IsActive,
    int? WorkCenterId = null, string? WorkCenterCode = null, string? WorkCenterName = null);

// ---- 治工具（Tool）----

public record ToolRequest(
    [Required, MaxLength(50)] string Code,
    [Required, MaxLength(200)] string Name,
    string? ToolType,
    int? LifeThresholdCount,
    decimal? LifeThresholdHours,
    ToolStatus Status);

public record ToolResponse(
    int Id, string Code, string Name, string? ToolType,
    int? LifeThresholdCount, decimal? LifeThresholdHours, ToolStatus Status, bool IsActive);

// ---- 作業区／資源階層（WorkCenter）----

public record WorkCenterRequest(
    [Required, MaxLength(50)] string Code,
    [Required, MaxLength(200)] string Name,
    WorkCenterLevel Level,
    int? ParentId);

/// <summary>ParentCode・ParentName は画面で親を表示するための付随情報（更新は ParentId で行う）</summary>
public record WorkCenterResponse(
    int Id, string Code, string Name, WorkCenterLevel Level,
    int? ParentId, string? ParentCode, string? ParentName, bool IsActive);

// ---- ロケーション（Location）----

public record LocationRequest(
    [Required, MaxLength(50)] string Code,
    LocationAreaType AreaType,
    string? ShelfNo,
    int? WorkCenterId = null);

/// <summary>WorkCenterCode・WorkCenterName は画面表示用の付随情報（更新は WorkCenterId で行う）</summary>
public record LocationResponse(
    int Id, string Code, LocationAreaType AreaType, string? ShelfNo, bool IsActive,
    int? WorkCenterId = null, string? WorkCenterCode = null, string? WorkCenterName = null);

// ---- 不良理由（DefectReason）----

public record DefectReasonRequest(
    [Required, MaxLength(50)] string Code,
    [Required, MaxLength(200)] string Name,
    DefectReasonCategory Category);

public record DefectReasonResponse(
    int Id, string Code, string Name, DefectReasonCategory Category, bool IsActive);

// ---- 工程管理項目（ControlItem）----

public record ControlItemRequest(
    [Required, MaxLength(50)] string Code,
    [Required, MaxLength(200)] string Name,
    [MaxLength(30)] string? Unit,
    int? TargetProductId,
    int? TargetProcessId,
    decimal? TargetValue,
    decimal? LowerLimit,
    decimal? UpperLimit);

public record ControlItemResponse(
    int Id, string Code, string Name, string? Unit,
    int? TargetProductId, string? TargetProductCode,
    int? TargetProcessId, string? TargetProcessCode,
    decimal? TargetValue, decimal? LowerLimit, decimal? UpperLimit,
    int Version, bool IsActive);

// ---- 検査項目・基準（InspectionItem）----

public record InspectionItemRequest(
    [Required, MaxLength(50)] string Code,
    [Required, MaxLength(200)] string Name,
    int? TargetProductId,
    int? TargetProcessId,
    InspectionType Type,
    decimal? LowerLimit,
    decimal? UpperLimit,
    decimal? StandardValue,
    string? Method,
    int? SamplingCount);

public record InspectionItemResponse(
    int Id, string Code, string Name,
    int? TargetProductId, string? TargetProductCode,
    int? TargetProcessId, string? TargetProcessCode,
    InspectionType Type, decimal? LowerLimit, decimal? UpperLimit, decimal? StandardValue,
    string? Method, int? SamplingCount, int Version, bool IsActive);

// ---- チェックリスト（Checklist）----

public record ChecklistItemRequest(
    [Range(1, int.MaxValue)] int Sequence,
    [Required, MaxLength(500)] string Text,
    bool IsRequired);

public record ChecklistRequest(
    [Required, MaxLength(50)] string Code,
    [Required, MaxLength(200)] string Name,
    ChecklistCategory Category,
    List<ChecklistItemRequest> Items);

public record ChecklistItemResponse(int Id, int Sequence, string Text, bool IsRequired);

public record ChecklistResponse(
    int Id, string Code, string Name, ChecklistCategory Category, bool IsActive,
    List<ChecklistItemResponse> Items);

// ---- スキル・資格（SkillMaster）----

public record SkillRequest(
    [Required, MaxLength(50)] string Code,
    [Required, MaxLength(200)] string Name,
    SkillType Type,
    bool RequiresExpiry);

public record SkillResponse(int Id, string Code, string Name, SkillType Type, bool RequiresExpiry, bool IsActive);
