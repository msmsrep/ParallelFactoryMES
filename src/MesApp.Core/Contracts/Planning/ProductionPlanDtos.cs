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

/// <summary>
/// 工程別の予実の1行（製造日×品目×工程。作業区違いの計画は合算する）。
/// 実績は工程ごとの出来高＝生産実績の良品数（開始時刻の製造日で振り分け、リワーク指図の産出は数えない）
/// </summary>
public record ProductionPlanActualRow(
    DateOnly BusinessDate,
    int ProductId, string ProductCode, string ProductName,
    int ProcessId, string ProcessCode, string ProcessName,
    /// <summary>計画数量（計画の無い日は 0）</summary>
    decimal PlannedQuantity,
    /// <summary>実績数量（実績の無い日は 0）</summary>
    decimal ActualQuantity,
    /// <summary>達成率（%）。計画が 0 なら null</summary>
    decimal? AchievementRate);
