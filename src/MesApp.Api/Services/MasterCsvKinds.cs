using MesApp.Core.Contracts.Masters;
using MesApp.Core.Entities;

namespace MesApp.Api.Services;

/// <summary>
/// CSV入出力に対応するマスタ種別の定義（列名・必須列・権限）。
/// 列名は英語固定で、画面・ドキュメントでは Label を案内する。Label・Note は日本語の原文で持ち、
/// 画面へ返すときに <see cref="CsvImport.Localize"/> で表示言語へ訳す（Spec.md 7.9）。
/// </summary>
public static class MasterCsvKinds
{
    public const string Products = "products";
    public const string Processes = "processes";
    public const string Equipments = "equipments";
    public const string EquipmentParts = "equipment-parts";
    public const string Tools = "tools";
    public const string WorkCenters = "work-centers";
    public const string Locations = "locations";
    public const string InspectionItems = "inspection-items";
    public const string ControlItems = "control-items";
    public const string Checklists = "checklists";
    public const string DefectReasons = "defect-reasons";
    public const string Skills = "skills";
    public const string Bom = "bom";
    public const string Routing = "routing";
    public const string WorkProcedures = "work-procedures";
    public const string Shifts = "shifts";
    public const string InspectionDevices = "inspection-devices";
    public const string Users = "users";
    public const string UserSkills = "user-skills";
    public const string ProductionPlans = "production-plans";

    public static readonly List<CsvKindInfo> All =
    [
        new(Products, "品目", false,
        [
            new("Code", "品目コード", true, "既存コードと一致すれば更新、無ければ新規登録"),
            new("Name", "品目名", true, null),
            new("Unit", "単位", true, "個・kg・m など"),
            new("Specification", "規格", false, null),
            new("Type", "品目区分", false, "Product（製品）/ SemiFinished（半製品）/ Material（部材）"),
            new("StandardDefectRate", "標準不良率(%)", false, "0〜99.99。指図の予定材料をこの率ぶん割り増す"),
            new("DefaultLocationCode", "既定ロケーション", false, "推奨ロケーションの第一候補。空欄で解除"),
            new("IsActive", "有効", false, "true / false。falseで無効化"),
        ]),
        new(Processes, "工程", false,
        [
            new("Code", "工程コード", true, null),
            new("Name", "工程名", true, null),
            new("Category", "工程区分", false, "InHouse（内製）/ Outsourced（外注）"),
            new("IsActive", "有効", false, "true / false"),
        ]),
        new(Equipments, "設備（BOE）", false,
        [
            new("AssetNo", "資産番号", true, null),
            new("Name", "設備名", true, null),
            new("WorkCenterCode", "作業区コード", false, "登録済みの作業区コード（段が作業区のもの）"),
            new("Site", "設置場所（旧項目）", false, "作業区を整備するまでの自由記述。設置場所の正は WorkCenterCode"),
            new("Status", "状態", false, "Available / Stopped / UnderMaintenance / Retired"),
            new("MaintenanceType", "保全タイプ", false, "None / Calendar（日数）/ RunTime（時間）/ Count（回数）"),
            new("MaintenanceThreshold", "保全閾値", false, "保全タイプに応じた日数・時間・回数"),
            new("MaintenanceParts", "保全部品", false, null),
            new("IsActive", "有効", false, "true / false"),
        ]),
        new(EquipmentParts, "設備の保全部品", false,
        [
            new("EquipmentAssetNo", "設備の資産番号", true, "同じ設備の保全部品を一括置換する"),
            new("ProductCode", "部品の品目コード", true, "登録済みの品目コード"),
            new("Category", "管理区分", false, "Asset（資産管理部品）/ Consumable（消耗品）"),
            new("QuantityPer", "1回あたり数量", false, "0以上"),
            new("Note", "備考", false, null),
        ]),
        new(Tools, "治工具", false,
        [
            new("Code", "治工具コード", true, null),
            new("Name", "治工具名", true, null),
            new("ToolType", "種別", false, "型・切削工具・検査工具 など"),
            new("LifeThresholdCount", "寿命閾値(回数)", false, null),
            new("LifeThresholdHours", "寿命閾値(時間)", false, null),
            new("Status", "状態", false, "Available / InUse / UnderMaintenance / Retired"),
            new("IsActive", "有効", false, "true / false"),
        ]),
        new(WorkCenters, "作業区", false,
        [
            new("Code", "作業区コード", true, "既存コードと一致すれば更新、無ければ新規登録"),
            new("Name", "名称", true, null),
            new("Level", "段", false, "Plant（工場）/ Line（ライン）/ Area（エリア）/ WorkCenter（作業区）"),
            new("ParentCode", "上位の作業区コード", false,
                "1つ上の段のコード。工場は空欄。同じファイル内で上位を先に定義しなくてもよい"),
            new("IsActive", "有効", false, "true / false"),
        ]),
        new(Locations, "ロケーション", false,
        [
            new("Code", "ロケーションコード", true, null),
            new("WorkCenterCode", "所属する作業区コード", false, "登録済みの作業区コード（段は問わない）"),
            new("AreaType", "区分", false,
                "MaterialWarehouse（部材倉庫）/ InProcess（工程内）/ ProductWarehouse（製品倉庫）/ ShippingArea（出荷場）"),
            new("ShelfNo", "棚番", false, null),
            new("IsActive", "有効", false, "true / false"),
        ]),
        new(InspectionItems, "検査項目・基準", false,
        [
            new("Code", "検査項目コード", true, null),
            new("Name", "検査項目名", true, null),
            new("TargetProductCode", "対象品目コード", false, "登録済みの品目コード"),
            new("TargetProcessCode", "対象工程コード", false, "登録済みの工程コード"),
            new("Type", "検査種別", false, "Receiving / InProcess / FinalProduct / Sample"),
            new("LowerLimit", "規格値下限", false, null),
            new("UpperLimit", "規格値上限", false, null),
            new("StandardValue", "基準値", false, null),
            new("Method", "検査方法", false, null),
            new("SamplingCount", "サンプリング数", false, null),
            new("IsActive", "有効", false, "true / false"),
            new("Version", "版数", false, "出力のみ。基準値が変わる更新で自動採番"),
        ]),
        new(ControlItems, "工程管理項目", false,
        [
            new("Code", "工程管理項目コード", true, null),
            new("Name", "名称", true, null),
            new("Unit", "単位", false, "℃ / rpm など"),
            new("TargetProductCode", "対象品目コード", false, "登録済みの品目コード"),
            new("TargetProcessCode", "対象工程コード", false, "登録済みの工程コード"),
            new("TargetValue", "指示値", false, "許容範囲の内側であること"),
            new("LowerLimit", "許容下限", false, null),
            new("UpperLimit", "許容上限", false, null),
            new("IsActive", "有効", false, "true / false"),
        ]),
        new(Checklists, "チェックリスト", false,
        [
            new("Code", "チェックリストコード", true, "同じコードの複数行が1つのチェックリストになる"),
            new("Name", "チェックリスト名", true, "グループ内の最初の値を使用"),
            new("Category", "適用区分", false, "Common / Process / Product / Setup / Maintenance / Hse"),
            new("IsActive", "有効", false, "true / false"),
            new("Sequence", "項目の表示順", false, "空欄なら項目なし。取込時は同コードの項目を一括置換"),
            new("Text", "チェック内容", false, null),
            new("IsRequired", "必須項目", false, "true / false"),
        ]),
        new(DefectReasons, "不良理由", false,
        [
            new("Code", "不良理由コード", true, "既存コードと一致すれば更新、無ければ新規登録"),
            new("Name", "不良理由名", true, null),
            new("Category", "区分", false, "Material / Process / Equipment / Human / Other"),
            new("IsActive", "有効", false, "true / false"),
        ]),
        new(Skills, "スキル・資格", false,
        [
            new("Code", "スキル・資格コード", true, null),
            new("Name", "名称", true, null),
            new("Type", "種別", false, "Skill（スキル）/ Certification（資格）"),
            new("RequiresExpiry", "有効期限管理", false, "true / false"),
            new("IsActive", "有効", false, "true / false"),
        ])
        {
            // 単票の api/skills がシステム管理者専用（SkillsController）。CSVも同じ権限で絞る
            UserAdminWrite = true,
        },
        new(Bom, "MBOM（部品構成）", false,
        [
            new("ParentProductCode", "親品目コード", true, "同じ親の明細を一括置換する"),
            new("ChildProductCode", "子品目コード", true, null),
            new("QuantityPer", "必要数量（親1単位）", true, "0より大きい数値"),
            new("MakeOrBuy", "内外製区分", false, "InHouse（内製）/ Outsourced（外注）"),
            new("AlternativeGroup", "代替部品グループ", false, "グループの主材料（IsAlternative=false）はちょうど1つ"),
            new("IsAlternative", "代替部品", false, "true / false。trueの行はAlternativeGroupが必須で、投入するには理由の記録が必要"),
            new("RoutingSequence", "消費工程（工程順序）", false, "親品目の工順の工程順序。空なら最終工程。バックフラッシュはこの工程で部材を引く"),
        ]),
        new(Routing, "工順（BOP）", false,
        [
            new("ProductCode", "品目コード", true, "同じ品目の工順を一括置換する"),
            new("Sequence", "工程順序", true, "1以上。品目内で一意"),
            new("ProcessCode", "工程コード", true, null),
            new("StandardWorkMinutes", "標準作業時間(分)", false, null),
            new("StandardSetupMinutes", "標準段取り時間(分)", false, null),
            new("RequiredSkillCode", "必要スキル・資格コード", false, null),
            new("EquipmentAssetNo", "代表設備の資産番号", false, "候補設備の1つとして扱う"),
            new("EquipmentAssetNos", "候補設備の資産番号", false, "セミコロン区切り。空なら差立で設備を限定しない"),
            new("ToolCode", "使用治工具コード", false, null),
            new("WorkCenterCode", "作業区コード", false, "登録済みの作業区コード（段が作業区のもの）"),
            new("ChecklistCode", "チェックリストコード", false, null),
            new("ControlItems", "工程管理項目", false, "温度・回転数 など"),
            new("WorkProcedureNo", "作業手順書番号", false, "登録済みで有効な手順書の番号"),
        ]),
        new(WorkProcedures, "作業手順書（SOP）", true,
        [
            new("ProcedureNo", "手順書番号", true, "既存と一致すれば更新（版数+1）、無ければ新規登録"),
            new("Title", "表題", true, null),
            new("Steps", "手順ステップ", false, "手順書の所在を書かない場合は必須"),
            new("Reference", "手順書の所在", false, "別システムの文書番号・URLなど。手順ステップを書かない場合は必須"),
            new("IsActive", "有効", false, "true / false"),
        ]),
        new(Shifts, "勤務シフト（直）", true,
        [
            new("Code", "シフトコード", true, "既存と一致すれば更新、無ければ新規登録"),
            new("Name", "名称", true, "昼勤 / 夜勤 など"),
            new("StartTime", "開始時刻", true, "HH:mm"),
            new("EndTime", "終了時刻", true, "HH:mm。開始時刻以下なら翌日にまたぐ夜勤として扱う"),
            new("IsActive", "有効", false, "true / false"),
        ]),
        new(InspectionDevices, "検査機・測定器", true,
        [
            new("Code", "検査機コード", true, "既存と一致すれば更新、無ければ新規登録"),
            new("Name", "名称", true, null),
            new("SerialNo", "製造番号・管理番号", false, null),
            new("Location", "設置場所", false, null),
            new("CalibratedOn", "最終校正日", false, "yyyy-MM-dd"),
            new("CalibrationDueOn", "次回校正期限", false, "yyyy-MM-dd。空欄なら期限の判定を行わない"),
            new("CalibrationCycleDays", "校正周期（日）", false, "校正の記録時に次回期限を置くのに使う"),
            new("Note", "備考", false, null),
            new("IsActive", "有効", false, "true / false"),
        ]),
        new(Users, "ユーザー", true,
        [
            new("UserName", "ユーザー名", true, "既存ユーザーと一致すれば更新、無ければ新規登録"),
            new("DisplayName", "氏名（表示名）", true, null),
            new("Roles", "ロール", false,
                "セミコロン区切り。SystemAdmin / ProductionManager / Operator / Logistics / QualityControl / QualityAssurance / Maintenance"),
            new("WorkCenterCode", "作業場所の作業区コード", false, "登録済みの作業区コード（段は問わない）"),
            new("Department", "所属（部署・課）", false, null),
            new("ShiftCode", "所属する直のシフトコード", false, "登録済みで有効なシフトコード"),
            new("IsActive", "在籍", false, "false で無効化（ログイン不可・セッション失効）"),
            new("InitialPassword", "初期パスワード", false,
                "新規登録時のみ必須。8文字以上で英小文字と数字を含む。初回ログイン時に変更を強制"),
        ]),
        new(UserSkills, "ユーザーのスキル・資格", true,
        [
            new("UserName", "ユーザー名", true, "同じユーザーの割当を一括置換する"),
            new("SkillCode", "スキル・資格コード", true, null),
            new("AcquiredOn", "取得日", false, "yyyy-MM-dd"),
            new("ExpiresOn", "有効期限", false, "yyyy-MM-dd"),
        ]),
        // 生産計画はマスタではなく業務データだが、改訂のたびに同じキーを上書きしたいので、実績CSV（常に新規登録）でなく
        // マスタCSVの取込の仕組み（キーでupsert）だけを借りる。マスタの一括ZIPには入れない（ImportOrder・Standalone。
        // Spec.md 3.8・5.2 ProductionPlan。A-30-10-01）
        new(ProductionPlans, "生産計画", false,
        [
            new("BusinessDate", "製造日", true, "yyyy-MM-dd。製造日・品目・工程・作業区が同じなら計画数量を上書き、無ければ新規登録"),
            new("ProductCode", "品目コード", true, "登録済みの品目コード"),
            new("ProcessCode", "工程コード", true, "登録済みの工程コード"),
            new("WorkCenterCode", "作業区コード", false, "登録済みの作業区コード（段は問わない）。空欄も1つのキーとして扱う"),
            new("PlannedQuantity", "計画数量", true, "0以上。0は計画上の休止"),
            new("Note", "備考", false, null),
        ])
        {
            // 単票の api/production-plans と同じ権限（ProductionPlansController）
            WriteRoles = MesRoleGroups.ProductionManage,
        },
    ];

    /// <summary>
    /// 一括出力で付ける番号の順（＝取り込む順）。後の種別が前の種別のコードを参照する
    /// （ロケーション→品目の既定ロケーション、作業区→設備・工順、手順書→工順、直→ユーザー など）。
    /// 種別を追加したら、参照先より後ろに置く。マスタの一括ZIPに入るのはここに並べた種別だけ
    /// </summary>
    public static readonly IReadOnlyList<string> ImportOrder =
    [
        WorkCenters, Processes, Locations, Products, Skills, Shifts, Equipments, EquipmentParts, Tools,
        Checklists, DefectReasons, InspectionItems, ControlItems, InspectionDevices, Bom, WorkProcedures,
        Routing, Users, UserSkills,
    ];

    /// <summary>
    /// 取込の仕組みだけを借りている、マスタでない種別（生産計画）。マスタの一括ZIPの取込・出力には入れず、
    /// それぞれの画面から1ファイルずつ扱う（日々増える業務データを、マスタの移行や全件出力に巻き込まないため。Spec.md 3.8）
    /// </summary>
    public static readonly IReadOnlyList<string> Standalone = [ProductionPlans];

    public static CsvKindInfo? Find(string kind) =>
        All.FirstOrDefault(k => string.Equals(k.Kind, kind, StringComparison.OrdinalIgnoreCase));

    public static string[] ColumnNames(CsvKindInfo kind) => [.. kind.Columns.Select(c => c.Name)];
}

/// <summary>CSVの列挙値として受け付ける文字列（英語の列挙名＋画面と同じ日本語ラベル）</summary>
public static class CsvEnumLabels
{
    public static readonly IReadOnlyDictionary<string, ProductType> ProductTypes = Build(
        ("製品", ProductType.Product),
        ("半製品", ProductType.SemiFinished), ("中間品", ProductType.SemiFinished),
        ("半製品・中間品", ProductType.SemiFinished),
        ("部材", ProductType.Material));

    public static readonly IReadOnlyDictionary<string, ManufacturingOrderType> OrderTypes = Build(
        ("通常", ManufacturingOrderType.Normal), ("突発", ManufacturingOrderType.Spot),
        ("リワーク", ManufacturingOrderType.Rework));

    public static readonly IReadOnlyDictionary<string, SetupType> SetupTypes = Build(
        ("前段取り", SetupType.Pre), ("後段取り", SetupType.Post));

    public static readonly IReadOnlyDictionary<string, InspectionOrderType> InspectionOrderTypes = Build(
        ("受入検査", InspectionOrderType.Receiving), ("工程内検査", InspectionOrderType.InProcess),
        ("製品完成品検査", InspectionOrderType.FinalProduct), ("完成品検査", InspectionOrderType.FinalProduct),
        ("サンプル検査", InspectionOrderType.Sample), ("再検査", InspectionOrderType.Reinspection));

    public static readonly IReadOnlyDictionary<string, InspectionJudgment> InspectionJudgments = Build(
        ("合格", InspectionJudgment.Pass), ("不合格", InspectionJudgment.Fail));

    public static readonly IReadOnlyDictionary<string, WorkTimeType> WorkTimeTypes = Build(
        ("直接作業", WorkTimeType.Direct), ("直接", WorkTimeType.Direct),
        ("間接作業", WorkTimeType.Indirect), ("間接", WorkTimeType.Indirect));

    public static readonly IReadOnlyDictionary<string, TroubleCategory> TroubleCategories = Build(
        ("品質", TroubleCategory.Quality), ("コスト", TroubleCategory.Cost),
        ("納期", TroubleCategory.Delivery), ("安全", TroubleCategory.Safety));

    public static readonly IReadOnlyDictionary<string, EquipmentLogStatus> EquipmentLogStatuses = Build(
        ("稼働", EquipmentLogStatus.Running), ("停止", EquipmentLogStatus.Stopped),
        ("段取り", EquipmentLogStatus.Setup), ("故障", EquipmentLogStatus.Failure),
        ("アイドル", EquipmentLogStatus.Idle), ("待機", EquipmentLogStatus.Idle));

    public static readonly IReadOnlyDictionary<string, ShipmentJudgmentResult> ShipmentJudgmentResults = Build(
        ("可", ShipmentJudgmentResult.Approved), ("保留", ShipmentJudgmentResult.Hold),
        ("特採", ShipmentJudgmentResult.SpecialAcceptance), ("特別採用", ShipmentJudgmentResult.SpecialAcceptance));

    public static readonly IReadOnlyDictionary<string, MakeOrBuy> MakeOrBuys = Build(
        ("内製", MakeOrBuy.InHouse), ("外注", MakeOrBuy.Outsourced));

    public static readonly IReadOnlyDictionary<string, EquipmentStatus> EquipmentStatuses = Build(
        ("稼働可能", EquipmentStatus.Available), ("停止中", EquipmentStatus.Stopped),
        ("保全中", EquipmentStatus.UnderMaintenance), ("廃棄", EquipmentStatus.Retired),
        ("除却", EquipmentStatus.Retired));

    public static readonly IReadOnlyDictionary<string, MaintenancePartCategory> MaintenancePartCategories = Build(
        ("資産管理部品", MaintenancePartCategory.Asset), ("資産", MaintenancePartCategory.Asset),
        ("消耗品", MaintenancePartCategory.Consumable), ("消耗材", MaintenancePartCategory.Consumable));

    public static readonly IReadOnlyDictionary<string, MaintenanceType> MaintenanceTypes = Build(
        ("対象外", MaintenanceType.None), ("カレンダ", MaintenanceType.Calendar),
        ("稼働時間", MaintenanceType.RunTime), ("使用回数", MaintenanceType.Count));

    public static readonly IReadOnlyDictionary<string, ToolStatus> ToolStatuses = Build(
        ("使用可能", ToolStatus.Available), ("使用中", ToolStatus.InUse),
        ("メンテナンス中", ToolStatus.UnderMaintenance), ("廃棄", ToolStatus.Retired));

    public static readonly IReadOnlyDictionary<string, WorkCenterLevel> WorkCenterLevels = Build(
        ("工場", WorkCenterLevel.Plant), ("ライン", WorkCenterLevel.Line),
        ("エリア", WorkCenterLevel.Area), ("作業区", WorkCenterLevel.WorkCenter));

    public static readonly IReadOnlyDictionary<string, LocationAreaType> LocationAreaTypes = Build(
        ("部材倉庫", LocationAreaType.MaterialWarehouse), ("工程内", LocationAreaType.InProcess),
        ("製品倉庫", LocationAreaType.ProductWarehouse), ("出荷場", LocationAreaType.ShippingArea));

    public static readonly IReadOnlyDictionary<string, DefectReasonCategory> DefectReasonCategories = Build(
        ("材質・部材", DefectReasonCategory.Material), ("材質", DefectReasonCategory.Material),
        ("加工・作業", DefectReasonCategory.Process), ("加工", DefectReasonCategory.Process),
        ("設備", DefectReasonCategory.Equipment), ("人的要因", DefectReasonCategory.Human),
        ("その他", DefectReasonCategory.Other));

    public static readonly IReadOnlyDictionary<string, InspectionType> InspectionTypes = Build(
        ("受入検査", InspectionType.Receiving), ("工程内検査", InspectionType.InProcess),
        ("製品完成品検査", InspectionType.FinalProduct), ("完成品検査", InspectionType.FinalProduct),
        ("サンプル検査", InspectionType.Sample));

    public static readonly IReadOnlyDictionary<string, ChecklistCategory> ChecklistCategories = Build(
        ("共通", ChecklistCategory.Common), ("工程", ChecklistCategory.Process),
        ("品目", ChecklistCategory.Product), ("段取り", ChecklistCategory.Setup),
        ("保全", ChecklistCategory.Maintenance), ("HSE", ChecklistCategory.Hse));

    public static readonly IReadOnlyDictionary<string, SkillType> SkillTypes = Build(
        ("スキル", SkillType.Skill), ("資格", SkillType.Certification));

    private static Dictionary<string, TEnum> Build<TEnum>(params (string Label, TEnum Value)[] labels)
        where TEnum : struct, Enum
    {
        var map = new Dictionary<string, TEnum>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in Enum.GetNames<TEnum>())
        {
            map[name] = Enum.Parse<TEnum>(name);
        }
        foreach (var (label, value) in labels)
        {
            map[label] = value;
        }
        return map;
    }
}
