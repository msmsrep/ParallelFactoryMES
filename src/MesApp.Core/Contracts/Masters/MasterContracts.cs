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
    /// <summary>標準不良率（%）。予定材料の数量を割り増す（÷(1－率)）ため100%は受け付けない</summary>
    [Range(0, 99.99)] decimal StandardDefectRate,
    /// <summary>既定の入庫先ロケーション（推奨ロケーション指示の第一候補。D-10-30-03、D-40-40-03）</summary>
    int? DefaultLocationId = null);

public record ProductResponse(
    int Id, string Code, string Name, string Unit, string? Specification,
    ProductType Type, decimal StandardDefectRate, bool IsActive,
    int? DefaultLocationId = null, string? DefaultLocationCode = null);

// ---- MBOM（BomItem）----

public record BomItemRequest(
    int ChildProductId,
    [Range(0.000001, double.MaxValue)] decimal QuantityPer,
    MakeOrBuy MakeOrBuy,
    string? AlternativeGroup,
    /// <summary>代替部品か（同一グループ内の主材料でない行。投入時に理由の記録を求める）</summary>
    bool IsAlternative = false,
    /// <summary>消費する工程の工程順序（未指定なら最終工程。バックフラッシュはこの工程で引く）</summary>
    [Range(1, int.MaxValue)] int? RoutingSequence = null);

public record BomItemResponse(
    int Id, int ChildProductId, string ChildProductCode, string ChildProductName,
    decimal QuantityPer, MakeOrBuy MakeOrBuy, string? AlternativeGroup,
    bool IsAlternative = false, int? RoutingSequence = null);

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
    [MaxLength(1000)] string? ControlItems,
    int? ChecklistId,
    int? WorkCenterId = null,
    /// <summary>候補設備（B-10-20-02）。空なら差立で設備を限定しない</summary>
    List<int>? EquipmentIds = null,
    /// <summary>作業手順書（SOP。I-30-20-12）</summary>
    int? WorkProcedureId = null);

public record RoutingStepResponse(
    int Id, int Sequence, int ProcessId, string ProcessCode, string ProcessName,
    decimal StandardWorkMinutes, decimal StandardSetupMinutes,
    int? RequiredSkillId, string? RequiredSkillName,
    int? EquipmentId, int? ToolId, string? ControlItems, int? ChecklistId,
    int? WorkCenterId = null, string? WorkCenterCode = null, string? WorkCenterName = null,
    /// <summary>候補設備の資産番号（表示用）</summary>
    List<string>? EquipmentAssetNos = null,
    List<int>? EquipmentIds = null,
    int? WorkProcedureId = null, string? WorkProcedureNo = null, string? WorkProcedureTitle = null);

// ---- 作業手順書（SOP。I-30-40、B-10-30-03）----

public record WorkProcedureRequest(
    [Required, MaxLength(50)] string ProcedureNo,
    [Required, MaxLength(200)] string Title,
    [MaxLength(4000)] string Steps,
    /// <summary>手順書の所在（別システムの文書番号・URLなど。本文をMESに置けない場合に使う）</summary>
    [MaxLength(500)] string? Reference);

public record WorkProcedureResponse(
    int Id, string ProcedureNo, string Title, string Steps, string? Reference,
    int Version, bool IsActive);

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

// ---- 設備の保全部品（EquipmentPart）----

public record EquipmentPartRequest(
    int ProductId,
    MaintenancePartCategory Category,
    [Range(0, double.MaxValue)] decimal QuantityPer,
    [MaxLength(500)] string? Note);

public record EquipmentPartResponse(
    int Id, int EquipmentId, int ProductId, string ProductCode, string ProductName, string Unit,
    MaintenancePartCategory Category, decimal QuantityPer, string? Note);

// ---- 勤務シフト（直。F-10-10-01）----

public record ShiftRequest(
    [Required, MaxLength(20)] string Code,
    [Required, MaxLength(100)] string Name,
    /// <summary>開始時刻（工場のローカル時刻）</summary>
    TimeOnly StartTime,
    /// <summary>終了時刻。開始時刻以下なら翌日にまたぐ夜勤として扱う</summary>
    TimeOnly EndTime);

public record ShiftResponse(
    int Id, string Code, string Name, TimeOnly StartTime, TimeOnly EndTime,
    /// <summary>翌日にまたぐ直か（夜勤）</summary>
    bool CrossesMidnight,
    /// <summary>時間帯の表示（夜勤は「22:00〜翌06:00」）</summary>
    string ScheduleLabel,
    bool IsActive,
    /// <summary>
    /// 製造日の境界時刻（Spec.md 3.9）をまたぐ直への警告（またがないなら null）。
    /// 登録を拒否はしないので、一覧でも出し続けて運用中に気づけるようにする
    /// （境界時刻は設定値なので、設定を変えて初めてまたぐようになることもある）
    /// </summary>
    string? BoundaryWarning = null);

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

/// <summary>
/// 推奨ロケーション（D-10-30-03、D-40-40-03）。優先度順に並ぶ。
/// <see cref="Reason"/> は「なぜそこか」を現場に見せるためのもので、根拠を伏せると従われない
/// </summary>
public record LocationRecommendationResponse(
    int LocationId,
    string Code,
    LocationAreaType AreaType,
    string? ShelfNo,
    string Reason,
    /// <summary>そのロケーションにあるこの品目の現在庫（まとめるかどうかの判断材料）</summary>
    decimal CurrentQuantity);

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

// ---- 検査機・測定器（C-20-50-03）----

public record InspectionDeviceRequest(
    [Required, MaxLength(30)] string Code,
    [Required, MaxLength(200)] string Name,
    [MaxLength(100)] string? SerialNo,
    [MaxLength(200)] string? Location,
    DateOnly? CalibratedOn,
    DateOnly? CalibrationDueOn,
    [Range(1, 3650)] int? CalibrationCycleDays,
    [MaxLength(500)] string? Note);

public record InspectionDeviceResponse(
    int Id, string Code, string Name, string? SerialNo, string? Location,
    DateOnly? CalibratedOn, DateOnly? CalibrationDueOn, int? CalibrationCycleDays,
    string? Note, bool IsActive,
    /// <summary>業務日付時点で校正期限が切れているか（検査実績には使えない）</summary>
    bool IsCalibrationExpired,
    /// <summary>期限までの残り日数（期限なしはnull。負数は超過日数）</summary>
    int? DaysUntilDue);

/// <summary>校正の実施（C-20-50-03）。次回期限は指定が無ければ校正周期から自動で置く</summary>
public record InspectionDeviceCalibrationRequest(
    DateOnly CalibratedOn,
    DateOnly? NextDueOn,
    [MaxLength(500)] string? Result);

public record InspectionDeviceCalibrationResponse(
    int Id, int InspectionDeviceId, DateOnly CalibratedOn, DateOnly? NextDueOn,
    string? Result, string? PerformedByUserName, DateTimeOffset CreatedAt);

// ---- 設計変更の影響確認（J-40-40-01/03）----

/// <summary>
/// 設計変更（MBOM・工順の改訂）の影響範囲（J-40-40-01/03）。
/// 指図展開時のスナップショット方式（Spec.md 5.7）を採っているため、
/// マスタを直しても展開済みの指図は変わらない。その事実を改訂者に見せるための集計
/// </summary>
public record DesignChangeImpactResponse(
    int ProductId,
    string ProductCode,
    string ProductName,
    /// <summary>進行中（未完了・未取消）の製造指図</summary>
    List<DesignChangeOrderRow> Orders,
    /// <summary>現行MBOMの部材と、進行中指図が必要としている部材の和集合</summary>
    List<DesignChangeMaterialRow> Materials);

public record DesignChangeOrderRow(
    int OrderId,
    string OrderNo,
    ManufacturingOrderStatus Status,
    decimal Quantity,
    DateOnly? DueDate,
    int WorkOrderCount,
    /// <summary>着手済み（未完了でない）作業指示の件数</summary>
    int StartedWorkOrderCount,
    /// <summary>
    /// 展開済みでスナップショットが固定されているか。
    /// true＝この改訂は届かない（作り直すなら指図の取消と再作成が要る）、
    /// false＝未展開のため展開時に改訂後のMBOM・工順が使われる
    /// </summary>
    bool IsSnapshotFixed);

public record DesignChangeMaterialRow(
    int ProductId,
    string Code,
    string Name,
    string Unit,
    /// <summary>現行MBOMに載っているか（false＝進行中指図だけが使っている＝改訂で外した部材）</summary>
    bool InCurrentBom,
    /// <summary>現行MBOMの原単位（載っていなければnull）</summary>
    decimal? QuantityPer,
    /// <summary>進行中指図の予定数量の合計（スナップショット側の値）</summary>
    decimal PlannedQuantityInProgress,
    /// <summary>現在庫（全ロケーション合計）</summary>
    decimal StockQuantity);
