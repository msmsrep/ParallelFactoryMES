using MesApp.Core.Abstractions;

namespace MesApp.Core.Entities;

/// <summary>品目マスタ（Spec.md 5.1 Product）</summary>
public class Product : IDeactivatableMaster
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

    /// <summary>
    /// 既定の入庫先ロケーション（推奨ロケーション指示の第一候補。D-10-30-03、D-40-40-03）。
    /// 固定ロケーション運用のための項目で、未設定なら在庫実績とエリア種別から推奨を導く
    /// </summary>
    public int? DefaultLocationId { get; set; }
    public Location? DefaultLocation { get; set; }

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

    /// <summary>
    /// この部材を消費する工程（親品目の工順の工程順序 <see cref="Routing.Sequence"/>。B-40-10-09）。
    /// 未設定なら最終工程で消費する。工順は一括置換で行が作り直されるため、IDではなく工程順序で指す。
    /// 工順に無い工程順序は指図の展開時に拒否する（MBOMと工順はどちらを先に登録してもよいため、登録時には照合しない）
    /// </summary>
    public int? RoutingSequence { get; set; }
}

/// <summary>工程マスタ（Spec.md 5.1 Process）</summary>
public class ProcessMaster : IDeactivatableMaster
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

    /// <summary>
    /// この工程を行う作業区（計画上の場所。差立前でも決まるため、進捗の集計軸に使える。
    /// 設備は差立で割り当てるため、作業区は設備から導かずここで持つ）
    /// </summary>
    public int? WorkCenterId { get; set; }
    public WorkCenter? WorkCenter { get; set; }

    /// <summary>
    /// 代表の使用設備（BOE）。候補が1台だけの工順のための項目で、
    /// 候補設備（<see cref="EquipmentCandidates"/>）の1つとして扱う
    /// </summary>
    public int? EquipmentId { get; set; }
    public Equipment? Equipment { get; set; }

    /// <summary>
    /// 候補設備（B-10-20-02）。同じ工程を複数の装置で実行できる場合に列挙し、
    /// 差立ではこの中から1台を選ぶ。空なら設備を限定しない
    /// </summary>
    public List<RoutingEquipment> EquipmentCandidates { get; set; } = [];

    /// <summary>使用治工具</summary>
    public int? ToolId { get; set; }
    public Tool? Tool { get; set; }

    /// <summary>工程管理項目（温度・回転数など記録すべき製造条件の定義）</summary>
    public string? ControlItems { get; set; }

    /// <summary>工程・段取りで実施するチェックリスト</summary>
    public int? ChecklistId { get; set; }
    public Checklist? Checklist { get; set; }

    /// <summary>
    /// この工程の作業手順書（SOP。I-30-20-12「BOPに登録された作業に作業手順書/SOPを紐づける」）。
    /// 作業者は作業指示からこれを辿って手順を確認する（B-10-30-03）
    /// </summary>
    public int? WorkProcedureId { get; set; }
    public WorkProcedure? WorkProcedure { get; set; }
}

/// <summary>
/// 作業手順書（SOP。Spec.md 5.1 WorkProcedure。I-30-40-01〜02、B-10-30-03）。
/// <para>
/// 保全手順書（<see cref="MaintenanceProcedure"/>）の製造版。保全側にだけ手順書があり、
/// 製造の作業者が参照する手順の置き場が無かった非対称を解消する。
/// </para>
/// <para>
/// 対象品目・対象工程は持たない。紐付けは工順（BOP）側から行う（<see cref="Routing.WorkProcedureId"/>）。
/// 手順書側にも対象を持たせると、同じ手順書をどちらで紐付けたかで運用が割れる。
/// </para>
/// </summary>
public class WorkProcedure : IDeactivatableMaster
{
    public int Id { get; set; }

    /// <summary>手順書番号（一意）</summary>
    public string ProcedureNo { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>手順ステップ（テキスト。1行1ステップ等の自由書式）</summary>
    public string Steps { get; set; } = string.Empty;

    /// <summary>
    /// 手順書の所在（別システムの文書番号・URL など）。
    /// 手順書の作成自体はMESの対象外（I-30-30-01）で、3Dデータや図面のように
    /// MESに本文を置けない形式もあるため、外部を指す手段を用意する
    /// </summary>
    public string? Reference { get; set; }

    /// <summary>版数（改訂のたびに上がる。I-30-40-02 の承認対象）</summary>
    public int Version { get; set; } = 1;

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 工順の候補設備（Spec.md 5.1 RoutingEquipment。B-10-20-02）。
/// 同じ工程を複数の装置で実行できる場合に列挙する。差立ではこの中から1台を選ぶ。
/// </summary>
public class RoutingEquipment
{
    public int Id { get; set; }

    public int RoutingId { get; set; }
    public Routing? Routing { get; set; }

    public int EquipmentId { get; set; }
    public Equipment? Equipment { get; set; }
}

/// <summary>設備台帳/BOE（Spec.md 5.1 Equipment。E-10-10、I-10-20）</summary>
public class Equipment : IDeactivatableMaster
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

    /// <summary>
    /// 保全部品（交換部品リスト等の自由記述。品目マスタ参照の
    /// <see cref="Parts"/> を整備するまでの旧項目）
    /// </summary>
    public string? MaintenanceParts { get; set; }

    /// <summary>保全部品（品目マスタ参照。E-10-10-01、E-20-10-04）</summary>
    public List<EquipmentPart> Parts { get; set; } = [];

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 設備の保全部品（Spec.md 5.1 EquipmentPart。E-10-10-01、E-20-10-04）。
/// <para>
/// 自由記述では消耗材の使用状況（E-20-10-04）を集計できないため、品目マスタを参照する。
/// 資産管理部品（金型）と消耗品（Oリング）は管理形態が違うので区分を持つ。
/// </para>
/// </summary>
public class EquipmentPart
{
    public int Id { get; set; }

    public int EquipmentId { get; set; }
    public Equipment? Equipment { get; set; }

    /// <summary>部品の品目（在庫を持つため品目マスタで管理する）</summary>
    public int ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>管理区分（資産管理部品/消耗品）</summary>
    public MaintenancePartCategory Category { get; set; }

    /// <summary>1回の保全で使う標準数量（消耗品の所要量の目安。E-40-30-01）</summary>
    public decimal QuantityPer { get; set; }

    public string? Note { get; set; }
}

/// <summary>
/// 勤務シフト（直。Spec.md 5.1 Shift。F-10-10-01）。
/// <para>
/// 3.9節の製造日（業務日付）は夜勤の日跨ぎ実績を同じ製造日へ集めるための仕組みだが、
/// 「その実績がどの直のものか」を表す定義が無かった。ここで直の時間帯を定義する。
/// </para>
/// <para>
/// 誰がいつどの直に入るかの勤務計画は持たない（勤怠管理 F-30-10 はMESの対象外寄り）。
/// 従業員には所属する直を既定として持たせ、実績の直は記録時刻から引く。
/// </para>
/// </summary>
public class Shift : IDeactivatableMaster
{
    public int Id { get; set; }

    /// <summary>シフトコード（一意。例: D／N）</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>名称（昼勤・夜勤・準夜勤など）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>開始時刻（工場のローカル時刻）</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>
    /// 終了時刻（工場のローカル時刻）。
    /// 開始時刻以下のときは翌日にまたぐ夜勤として扱う（22:00〜06:00 など）
    /// </summary>
    public TimeOnly EndTime { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 検査機・測定器マスタ（Spec.md 5.1 InspectionDevice。C-20-50-03 検査機の校正管理・有効期限確認）。
/// <para>
/// 校正期限を過ぎた機器で測った結果は、規格に合っていても品質保証の根拠にならない。
/// そのため検査実績の記録時に期限を判定し、期限切れの機器は使わせない。
/// </para>
/// <para>
/// 現在の校正状態（<see cref="CalibratedOn"/>／<see cref="CalibrationDueOn"/>）はこのマスタが持ち、
/// 過去の校正の経緯は <see cref="InspectionDeviceCalibration"/> に残す。
/// 治工具（<see cref="Tool"/>）と分けているのは、治工具が寿命（使用回数・時間）で管理されるのに対し、
/// 検査機は日付で管理され、期限切れの影響が「使えない」ではなく「測定結果を信用できない」だからである。
/// </para>
/// </summary>
public class InspectionDevice : IDeactivatableMaster
{
    public int Id { get; set; }

    /// <summary>検査機コード（一意）</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>製造番号・管理番号</summary>
    public string? SerialNo { get; set; }

    /// <summary>設置場所（自由記述）</summary>
    public string? Location { get; set; }

    /// <summary>最終校正日</summary>
    public DateOnly? CalibratedOn { get; set; }

    /// <summary>次回校正期限（この日を過ぎると検査実績に使えない）</summary>
    public DateOnly? CalibrationDueOn { get; set; }

    /// <summary>校正周期（日数。校正実施時に次回期限を自動で置くために使う）</summary>
    public int? CalibrationCycleDays { get; set; }

    public string? Note { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 検査機の校正実施記録（Spec.md 5.1 InspectionDeviceCalibration。C-20-50-03）。
/// マスタの現在値を上書きするだけでは「いつ誰がどの結果で校正したか」が残らないため、
/// 実施のたびに1レコードを追加する（保全実績と同じ考え方）
/// </summary>
public class InspectionDeviceCalibration
{
    public int Id { get; set; }

    public int InspectionDeviceId { get; set; }
    public InspectionDevice? InspectionDevice { get; set; }

    /// <summary>校正日</summary>
    public DateOnly CalibratedOn { get; set; }

    /// <summary>この校正で設定した次回校正期限</summary>
    public DateOnly? NextDueOn { get; set; }

    /// <summary>校正の結果・所見（合格／調整の内容など）</summary>
    public string? Result { get; set; }

    /// <summary>実施者（社内校正の場合。外部委託なら委託先を Result に書く）</summary>
    public string? PerformedByUserId { get; set; }
    public AppUser? PerformedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>治工具マスタ（Spec.md 5.1 Tool。E-60）</summary>
public class Tool : IDeactivatableMaster
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
public class WorkCenter : IDeactivatableMaster
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
public class Location : IDeactivatableMaster
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
public class InspectionItem : IDeactivatableMaster
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

/// <summary>
/// 工程管理項目（Spec.md 5.1 ControlItem。B-30-30-04）。
/// 温度・回転数など、製造時に記録すべき条件の定義と指示値・上下限。
/// <para>
/// 検査項目（<see cref="InspectionItem"/>）と同じ形にしている。ガイドの言う「指示値と実績値」の
/// 関係は「検査パラメータと検査結果」と同じ構造であり、判定の考え方も揃うため。
/// 違いは、検査が結果を測るのに対し、こちらは作る前に与える条件だという点。
/// </para>
/// </summary>
public class ControlItem : IDeactivatableMaster
{
    public int Id { get; set; }

    /// <summary>工程管理項目コード（一意）</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>単位（℃・rpm など）</summary>
    public string? Unit { get; set; }

    /// <summary>対象品目（品目単位の条件の場合）</summary>
    public int? TargetProductId { get; set; }
    public Product? TargetProduct { get; set; }

    /// <summary>対象工程（工程単位の条件の場合）</summary>
    public int? TargetProcessId { get; set; }
    public ProcessMaster? TargetProcess { get; set; }

    /// <summary>指示値（レシピ上の狙い値。例「600W」）</summary>
    public decimal? TargetValue { get; set; }

    /// <summary>許容下限</summary>
    public decimal? LowerLimit { get; set; }

    /// <summary>許容上限</summary>
    public decimal? UpperLimit { get; set; }

    /// <summary>版数（条件改訂の管理）</summary>
    public int Version { get; set; } = 1;

    public bool IsActive { get; set; } = true;
}

/// <summary>チェックリストマスタ（Spec.md 5.1 Checklist。B-30-10、G-20/G-30）</summary>
public class Checklist : IDeactivatableMaster
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
public class DefectReason : IDeactivatableMaster
{
    public int Id { get; set; }

    /// <summary>不良理由コード（一意）</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DefectReasonCategory Category { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>スキル・資格マスタ（Spec.md 5.1 SkillMaster。F-20-10-01）</summary>
public class SkillMaster : IDeactivatableMaster
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
