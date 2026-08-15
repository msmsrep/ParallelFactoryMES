namespace MesApp.Core.Entities;

/// <summary>保全手順書（Spec.md 5.1 MaintenanceProcedure。E-10-20）</summary>
public class MaintenanceProcedure
{
    public int Id { get; set; }

    /// <summary>手順書番号（一意）</summary>
    public string ProcedureNo { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>対象設備（設備用の手順書）</summary>
    public int? TargetEquipmentId { get; set; }
    public Equipment? TargetEquipment { get; set; }

    /// <summary>対象治工具（治工具メンテナンス用の手順書）</summary>
    public int? TargetToolId { get; set; }
    public Tool? TargetTool { get; set; }

    /// <summary>手順ステップ（テキスト。1行1ステップ等の自由書式）</summary>
    public string Steps { get; set; } = string.Empty;

    /// <summary>版数（見直しの管理。E-20-30-04〜06）</summary>
    public int Version { get; set; } = 1;

    public bool IsActive { get; set; } = true;
}

/// <summary>設備稼働履歴（Spec.md 5.5 EquipmentLog。B-40-20、E-20-10。初期は手入力/CSV）</summary>
public class EquipmentLog
{
    public int Id { get; set; }

    public int EquipmentId { get; set; }
    public Equipment? Equipment { get; set; }

    public EquipmentLogStatus Status { get; set; }

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>停止原因（停止・故障時。B-40-20-02）</summary>
    public string? StopCause { get; set; }

    public string? Note { get; set; }

    public string? RecordedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>保全計画（Spec.md 5.5 MaintenancePlan。E-30-10）</summary>
public class MaintenancePlan
{
    public int Id { get; set; }

    public int EquipmentId { get; set; }
    public Equipment? Equipment { get; set; }

    /// <summary>保全種別（定期/計画外）</summary>
    public MaintenanceCategory Category { get; set; }

    /// <summary>計画年度</summary>
    public int PlanYear { get; set; }

    /// <summary>予定日</summary>
    public DateOnly? ScheduledDate { get; set; }

    /// <summary>周期（日数。定期保全のみ）</summary>
    public int? CycleDays { get; set; }

    public MaintenancePlanStatus Status { get; set; } = MaintenancePlanStatus.Planned;

    public string? Note { get; set; }

    public string? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>保全指示（Spec.md 5.5 MaintenanceOrder。E-30-20〜30、E-60-30 治工具メンテ含む）</summary>
public class MaintenanceOrder
{
    public int Id { get; set; }

    /// <summary>指示番号（一意。MTyyyyMMdd-連番）</summary>
    public string OrderNo { get; set; } = string.Empty;

    /// <summary>対象設備（設備保全時）</summary>
    public int? EquipmentId { get; set; }
    public Equipment? Equipment { get; set; }

    /// <summary>対象治工具（治工具メンテナンス時。E-60-30）</summary>
    public int? ToolId { get; set; }
    public Tool? Tool { get; set; }

    /// <summary>元の保全計画（計画保全時）</summary>
    public int? MaintenancePlanId { get; set; }
    public MaintenancePlan? MaintenancePlan { get; set; }

    /// <summary>保全手順書</summary>
    public int? ProcedureId { get; set; }
    public MaintenanceProcedure? Procedure { get; set; }

    public DateOnly? ScheduledDate { get; set; }

    /// <summary>依頼区分（計画/突発依頼）</summary>
    public MaintenanceRequestType RequestType { get; set; }

    public MaintenanceOrderStatus Status { get; set; } = MaintenanceOrderStatus.Instructed;

    /// <summary>依頼内容・指示内容</summary>
    public string? Note { get; set; }

    public string? CreatedByUserId { get; set; }
    public AppUser? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<MaintenanceRecord> Records { get; set; } = [];
}

/// <summary>保全実績（Spec.md 5.5 MaintenanceRecord。E-40-30、E-60-30-03）</summary>
public class MaintenanceRecord
{
    public int Id { get; set; }

    public int MaintenanceOrderId { get; set; }

    public string PerformedByUserId { get; set; } = string.Empty;
    public AppUser? PerformedBy { get; set; }

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>消費部材・交換部品</summary>
    public string? PartsUsed { get; set; }

    /// <summary>結果（実施内容・所見）</summary>
    public string? Result { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>治工具利用実績（Spec.md 5.5 ToolUsage。E-60-20。寿命検知の根拠データ）</summary>
public class ToolUsage
{
    public int Id { get; set; }

    public int ToolId { get; set; }
    public Tool? Tool { get; set; }

    /// <summary>関連作業指示（E-60-20-01：作業指示との紐付け）</summary>
    public int? WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    /// <summary>使用回数（ショット数等）</summary>
    public int UsageCount { get; set; }

    /// <summary>使用時間</summary>
    public decimal? UsageHours { get; set; }

    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? RecordedByUserId { get; set; }
}
