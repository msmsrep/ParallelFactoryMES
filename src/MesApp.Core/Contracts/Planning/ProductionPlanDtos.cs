using System.ComponentModel.DataAnnotations;

namespace MesApp.Core.Contracts.Planning;

// ---- 生産計画（Spec.md 5.2 ProductionPlan。A-30-10-01）----

/// <summary>生産計画の登録・更新。キーは 製造日・品目・工程・作業区（作業区なしも1つの値として扱う）</summary>
public record ProductionPlanRequest(
    /// <summary>製造日（Spec.md 3.9）</summary>
    DateOnly BusinessDate,
    int ProductId,
    int ProcessId,
    /// <summary>作業区（任意。段は問わない）</summary>
    int? WorkCenterId,
    /// <summary>計画数量。0 は計画上の休止を表す</summary>
    [Range(0, double.MaxValue)] decimal PlannedQuantity,
    [MaxLength(500)] string? Note);

/// <summary>コード・名称は画面で表示するための付随情報（更新は Id で行う）</summary>
public record ProductionPlanResponse(
    int Id,
    DateOnly BusinessDate,
    int ProductId, string ProductCode, string ProductName,
    int ProcessId, string ProcessCode, string ProcessName,
    int? WorkCenterId, string? WorkCenterCode, string? WorkCenterName,
    decimal PlannedQuantity,
    string? Note,
    DateTimeOffset UpdatedAt);
