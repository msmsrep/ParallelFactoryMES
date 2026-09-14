namespace MesApp.Core.Entities;

/// <summary>品目マスタ（Spec.md 5.1 Product）</summary>
public class Product
{
    public int Id { get; set; }

    /// <summary>品目コード（一意）</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>単位（個・kg・m など）</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>規格</summary>
    public string? Specification { get; set; }

    public ProductType Type { get; set; }

    /// <summary>標準不良率（%。A-40-10-04）</summary>
    public decimal StandardDefectRate { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>MBOM明細（Spec.md 5.1 BomItem。A-40-10）</summary>
public class BomItem
{
    public int Id { get; set; }

    public int ParentProductId { get; set; }
    public Product? ParentProduct { get; set; }

    /// <summary>子品目（部材・中間品）</summary>
    public int ChildProductId { get; set; }
    public Product? ChildProduct { get; set; }

    /// <summary>必要数量（親1単位あたり）</summary>
    public decimal QuantityPer { get; set; }

    /// <summary>内外製区分（A-40-10-03）</summary>
    public MakeOrBuy MakeOrBuy { get; set; }

    /// <summary>代替部品グループ（同一グループ内で代替可。A-40-10-04）</summary>
    public string? AlternativeGroup { get; set; }

    /// <summary>
    /// 代替部品か（A-40-10-04）。同一グループ内の「主材料でない行」を示す。
    /// 代替部品の投入には理由の記録を求める（Spec.md 3.9 部材投入の照合）
    /// </summary>
    public bool IsAlternative { get; set; }
}

/// <summary>工程マスタ（Spec.md 5.1 Process）</summary>
public class ProcessMaster
{
    public int Id { get; set; }

    /// <summary>工程コード（一意）</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>工程区分（内製/外注）</summary>
    public MakeOrBuy Category { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 工順/BOP（Spec.md 5.1 Routing。1レコード＝品目の1工程ステップ。A-40-20、I-30-20）
/// </summary>
public class Routing
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>工程順序（品目内で一意）</summary>
    public int Sequence { get; set; }

    public int ProcessId { get; set; }
    public ProcessMaster? Process { get; set; }

    /// <summary>標準作業時間（分）</summary>
    public decimal StandardWorkMinutes { get; set; }

    /// <summary>標準段取り時間（分）</summary>
    public decimal StandardSetupMinutes { get; set; }

    /// <summary>必要スキル（差立時のスキル照合 F-20-30-01 に使用）</summary>
    public int? RequiredSkillId { get; set; }
    public SkillMaster? RequiredSkill { get; set; }

    /// <summary>使用設備（BOE）</summary>
    public int? EquipmentId { get; set; }
    public Equipment? Equipment { get; set; }

    /// <summary>使用治工具</summary>
    public int? ToolId { get; set; }
    public Tool? Tool { get; set; }

    /// <summary>工程管理項目（温度・回転数など記録すべき製造条件の定義）</summary>
    public string? ControlItems { get; set; }

    /// <summary>工程・段取りで実施するチェックリスト</summary>
    public int? ChecklistId { get; set; }
    public Checklist? Checklist { get; set; }
}

/// <summary>設備台帳/BOE（Spec.md 5.1 Equipment。E-10-10、I-10-20）</summary>
public class Equipment
{
    public int Id { get; set; }

    /// <summary>資産番号（一意）</summary>
    public string AssetNo { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 設置場所（作業区。設備は最下段の作業区にだけ紐付ける。Spec.md 5.1 WorkCenter）
    /// </summary>
    public int? WorkCenterId { get; set; }
    public WorkCenter? WorkCenter { get; set; }

    /// <summary>
    /// 設置場所の自由記述（作業区を整備するまでの旧項目。設置場所の正は <see cref="WorkCenterId"/>）
    /// </summary>
    public string? Site { get; set; }

    public EquipmentStatus Status { get; set; } = EquipmentStatus.Available;

    /// <summary>保全タイプ（カレンダ/時間/回数ベース）</summary>
    public MaintenanceType MaintenanceType { get; set; } = MaintenanceType.None;

    /// <summary>保全閾値（保全タイプに応じて日数・時間・回数）</summary>
    public decimal? MaintenanceThreshold { get; set; }

    /// <summary>保全部品（交換部品リスト等の自由記述）</summary>
    public string? MaintenanceParts { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>治工具マスタ（Spec.md 5.1 Tool。E-60）</summary>
public class Tool
{
    public int Id { get; set; }

    /// <summary>治工具コード（一意）</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>種別（型/切削工具/検査工具等）</summary>
    public string? ToolType { get; set; }

    /// <summary>寿命閾値（使用回数）</summary>
    public int? LifeThresholdCount { get; set; }

    /// <summary>寿命閾値（使用時間）</summary>
    public decimal? LifeThresholdHours { get; set; }

    /// <summary>寿命カウンタのリセット日時（治工具メンテナンス完了時。以降の利用実績のみ寿命累計に算入）</summary>
    public DateTimeOffset? LifeResetAt { get; set; }

    public ToolStatus Status { get; set; } = ToolStatus.Available;

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 作業区／資源階層（Spec.md 5.1 WorkCenter。I-10-20-02）
/// 工場・ライン・エリア・作業区を1つの自己参照ツリーで表す（資源構成全体＝BOR）。
/// 最下段の作業区が作業の管理単位になる。
/// </summary>
public class WorkCenter
{
    public int Id { get; set; }

    /// <summary>作業区コード（一意）</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>階層の段（工場/ライン/エリア/作業区）</summary>
    public WorkCenterLevel Level { get; set; }

    /// <summary>上位の資源（工場は親なし。親は自分より1つ上の段でなければならない）</summary>
    public int? ParentId { get; set; }
    public WorkCenter? Parent { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>ロケーション（Spec.md 5.1 Location。D-50-20-01）</summary>
public class Location
{
    public int Id { get; set; }

    /// <summary>ロケーションコード（一意）</summary>
    public string Code { get; set; } = string.Empty;

    public LocationAreaType AreaType { get; set; }

    /// <summary>
    /// 所属する資源（作業区。倉庫は工場直下に置かれることがあるため段を問わない。Spec.md 5.1 WorkCenter）
    /// </summary>
    public int? WorkCenterId { get; set; }
    public WorkCenter? WorkCenter { get; set; }

    /// <summary>棚番</summary>
    public string? ShelfNo { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>検査項目・基準（Spec.md 5.1 InspectionItem。C-10-10）</summary>
public class InspectionItem
{
    public int Id { get; set; }

    /// <summary>検査項目コード（一意）</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>対象品目（品目単位の基準の場合）</summary>
    public int? TargetProductId { get; set; }
    public Product? TargetProduct { get; set; }

    /// <summary>対象工程（工程単位の基準の場合）</summary>
    public int? TargetProcessId { get; set; }
    public ProcessMaster? TargetProcess { get; set; }

    public InspectionType Type { get; set; }

    /// <summary>規格値下限</summary>
    public decimal? LowerLimit { get; set; }

    /// <summary>規格値上限</summary>
    public decimal? UpperLimit { get; set; }

    /// <summary>基準値</summary>
    public decimal? StandardValue { get; set; }

    /// <summary>検査方法</summary>
    public string? Method { get; set; }

    /// <summary>サンプリング数</summary>
    public int? SamplingCount { get; set; }

    /// <summary>版数（基準改訂の管理）</summary>
    public int Version { get; set; } = 1;

    public bool IsActive { get; set; } = true;
}

/// <summary>チェックリストマスタ（Spec.md 5.1 Checklist。B-30-10、G-20/G-30）</summary>
public class Checklist
{
    public int Id { get; set; }

    /// <summary>チェックリストコード（一意）</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public ChecklistCategory Category { get; set; }

    public bool IsActive { get; set; } = true;

    public List<ChecklistItem> Items { get; set; } = [];
}

/// <summary>チェックリスト項目</summary>
public class ChecklistItem
{
    public int Id { get; set; }

    public int ChecklistId { get; set; }

    /// <summary>表示順</summary>
    public int Sequence { get; set; }

    /// <summary>チェック内容</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>必須項目か（未実施のままでは完了不可）</summary>
    public bool IsRequired { get; set; } = true;
}

/// <summary>
/// 不良理由マスタ（Spec.md 5.1 DefectReason。C-40-10-01）。
/// 生産実績の不良数の内訳（<see cref="ProductionDefect"/>）と不良項目別分析の集計軸に使う。
/// 粒度は「現場が迷わず選べて、集計にも使える」範囲で定義する
/// </summary>
public class DefectReason
{
    public int Id { get; set; }

    /// <summary>不良理由コード（一意）</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DefectReasonCategory Category { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>スキル・資格マスタ（Spec.md 5.1 SkillMaster。F-20-10-01）</summary>
public class SkillMaster
{
    public int Id { get; set; }

    /// <summary>スキル・資格コード（一意）</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public SkillType Type { get; set; }

    /// <summary>有効期限管理が必要か（資格更新など）</summary>
    public bool RequiresExpiry { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>従業員スキル・資格（Spec.md 5.1 UserSkill。F-20-10-02〜05）</summary>
public class UserSkill
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public AppUser? User { get; set; }

    public int SkillId { get; set; }
    public SkillMaster? Skill { get; set; }

    /// <summary>取得日</summary>
    public DateOnly? AcquiredOn { get; set; }

    /// <summary>有効期限（RequiresExpiryなスキルのみ。期限切れは照合で不合格）</summary>
    public DateOnly? ExpiresOn { get; set; }
}
