using MesApp.Core.Contracts.Masters;
using MesApp.Core.Entities;

namespace MesApp.Api.Services;

/// <summary>
/// CSV入出力に対応するマスタ種別の定義（列名・必須列・権限）。
/// 列名は英語固定で、画面・ドキュメントでは Label（日本語）を案内する。
/// </summary>
public static class MasterCsvKinds
{
    public const string Products = "products";
    public const string Processes = "processes";
    public const string Equipments = "equipments";
    public const string Tools = "tools";
    public const string Locations = "locations";
    public const string InspectionItems = "inspection-items";
    public const string Checklists = "checklists";
    public const string Skills = "skills";
    public const string Bom = "bom";
    public const string Routing = "routing";
    public const string Users = "users";
    public const string UserSkills = "user-skills";

    public static readonly List<CsvKindInfo> All =
    [
        new(Products, "品目", false,
        [
            new("Code", "品目コード", true, "既存コードと一致すれば更新、無ければ新規登録"),
            new("Name", "品目名", true, null),
            new("Unit", "単位", true, "個・kg・m など"),
            new("Specification", "規格", false, null),
            new("Type", "品目区分", false, "Product（製品）/ SemiFinished（半製品）/ Material（部材）"),
            new("StandardDefectRate", "標準不良率(%)", false, "0〜100"),
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
            new("Site", "設置場所", false, null),
            new("Status", "状態", false, "Available / Stopped / UnderMaintenance / Retired"),
            new("MaintenanceType", "保全タイプ", false, "None / Calendar（日数）/ RunTime（時間）/ Count（回数）"),
            new("MaintenanceThreshold", "保全閾値", false, "保全タイプに応じた日数・時間・回数"),
            new("MaintenanceParts", "保全部品", false, null),
            new("IsActive", "有効", false, "true / false"),
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
        new(Locations, "ロケーション", false,
        [
            new("Code", "ロケーションコード", true, null),
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
        new(Skills, "スキル・資格", false,
        [
            new("Code", "スキル・資格コード", true, null),
            new("Name", "名称", true, null),
            new("Type", "種別", false, "Skill（スキル）/ Certification（資格）"),
            new("RequiresExpiry", "有効期限管理", false, "true / false"),
            new("IsActive", "有効", false, "true / false"),
        ]),
        new(Bom, "MBOM（部品構成）", false,
        [
            new("ParentProductCode", "親品目コード", true, "同じ親の明細を一括置換する"),
            new("ChildProductCode", "子品目コード", true, null),
            new("QuantityPer", "必要数量（親1単位）", true, "0より大きい数値"),
            new("MakeOrBuy", "内外製区分", false, "InHouse（内製）/ Outsourced（外注）"),
            new("AlternativeGroup", "代替部品グループ", false, null),
        ]),
        new(Routing, "工順（BOP）", false,
        [
            new("ProductCode", "品目コード", true, "同じ品目の工順を一括置換する"),
            new("Sequence", "工程順序", true, "1以上。品目内で一意"),
            new("ProcessCode", "工程コード", true, null),
            new("StandardWorkMinutes", "標準作業時間(分)", false, null),
            new("StandardSetupMinutes", "標準段取り時間(分)", false, null),
            new("RequiredSkillCode", "必要スキル・資格コード", false, null),
            new("EquipmentAssetNo", "使用設備の資産番号", false, null),
            new("ToolCode", "使用治工具コード", false, null),
            new("ChecklistCode", "チェックリストコード", false, null),
            new("ControlItems", "工程管理項目", false, "温度・回転数 など"),
        ]),
        new(Users, "ユーザー", true,
        [
            new("UserName", "ユーザー名", true, "既存ユーザーと一致すれば更新、無ければ新規登録"),
            new("DisplayName", "氏名（表示名）", true, null),
            new("Roles", "ロール", false,
                "セミコロン区切り。SystemAdmin / ProductionManager / Operator / Logistics / QualityControl / QualityAssurance / Maintenance"),
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
    ];

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

    public static readonly IReadOnlyDictionary<string, MakeOrBuy> MakeOrBuys = Build(
        ("内製", MakeOrBuy.InHouse), ("外注", MakeOrBuy.Outsourced));

    public static readonly IReadOnlyDictionary<string, EquipmentStatus> EquipmentStatuses = Build(
        ("稼働可能", EquipmentStatus.Available), ("停止中", EquipmentStatus.Stopped),
        ("保全中", EquipmentStatus.UnderMaintenance), ("廃棄", EquipmentStatus.Retired),
        ("除却", EquipmentStatus.Retired));

    public static readonly IReadOnlyDictionary<string, MaintenanceType> MaintenanceTypes = Build(
        ("対象外", MaintenanceType.None), ("カレンダ", MaintenanceType.Calendar),
        ("稼働時間", MaintenanceType.RunTime), ("使用回数", MaintenanceType.Count));

    public static readonly IReadOnlyDictionary<string, ToolStatus> ToolStatuses = Build(
        ("使用可能", ToolStatus.Available), ("使用中", ToolStatus.InUse),
        ("メンテナンス中", ToolStatus.UnderMaintenance), ("廃棄", ToolStatus.Retired));

    public static readonly IReadOnlyDictionary<string, LocationAreaType> LocationAreaTypes = Build(
        ("部材倉庫", LocationAreaType.MaterialWarehouse), ("工程内", LocationAreaType.InProcess),
        ("製品倉庫", LocationAreaType.ProductWarehouse), ("出荷場", LocationAreaType.ShippingArea));

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
