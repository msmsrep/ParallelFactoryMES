namespace MesApp.Core.Entities;

/// <summary>品目区分（Spec.md 5.1 Product）</summary>
public enum ProductType
{
    /// <summary>製品</summary>
    Product,
    /// <summary>半製品・中間品</summary>
    SemiFinished,
    /// <summary>部材</summary>
    Material,
}

/// <summary>内外製区分（A-40-10-03）</summary>
public enum MakeOrBuy
{
    /// <summary>内製</summary>
    InHouse,
    /// <summary>外注</summary>
    Outsourced,
}

/// <summary>設備状態（Spec.md 5.1 Equipment）</summary>
public enum EquipmentStatus
{
    /// <summary>稼働可能</summary>
    Available,
    /// <summary>停止中</summary>
    Stopped,
    /// <summary>保全中</summary>
    UnderMaintenance,
    /// <summary>廃棄・除却</summary>
    Retired,
}

/// <summary>保全タイプ（E-10-10：カレンダ/時間/回数ベース）</summary>
public enum MaintenanceType
{
    /// <summary>保全対象外</summary>
    None,
    /// <summary>カレンダベース（周期日数）</summary>
    Calendar,
    /// <summary>稼働時間ベース</summary>
    RunTime,
    /// <summary>使用回数ベース</summary>
    Count,
}

/// <summary>治工具状態（Spec.md 5.1 Tool）</summary>
public enum ToolStatus
{
    /// <summary>使用可能</summary>
    Available,
    /// <summary>使用中</summary>
    InUse,
    /// <summary>メンテナンス中</summary>
    UnderMaintenance,
    /// <summary>廃棄</summary>
    Retired,
}

/// <summary>倉庫/エリア区分（Spec.md 5.1 Location）</summary>
public enum LocationAreaType
{
    /// <summary>部材倉庫</summary>
    MaterialWarehouse,
    /// <summary>工程内</summary>
    InProcess,
    /// <summary>製品倉庫</summary>
    ProductWarehouse,
    /// <summary>出荷場</summary>
    ShippingArea,
}

/// <summary>検査種別（C-10-10、C-20）</summary>
public enum InspectionType
{
    /// <summary>受入検査</summary>
    Receiving,
    /// <summary>工程内検査</summary>
    InProcess,
    /// <summary>製品完成品検査</summary>
    FinalProduct,
    /// <summary>サンプル検査</summary>
    Sample,
}

/// <summary>チェックリスト適用区分（Spec.md 5.1 Checklist。HSE項目の組み込みを含む G-20/G-30）</summary>
public enum ChecklistCategory
{
    /// <summary>共通</summary>
    Common,
    /// <summary>工程</summary>
    Process,
    /// <summary>品目</summary>
    Product,
    /// <summary>段取り</summary>
    Setup,
    /// <summary>保全</summary>
    Maintenance,
    /// <summary>HSE（健康衛生・安全）</summary>
    Hse,
}

/// <summary>スキル・資格種別（F-20-10-01）</summary>
public enum SkillType
{
    /// <summary>スキル</summary>
    Skill,
    /// <summary>資格</summary>
    Certification,
}

/// <summary>指図区分（A-20、B-70-10 リワーク、B-10-10-04 突発）</summary>
public enum ManufacturingOrderType
{
    /// <summary>通常</summary>
    Normal,
    /// <summary>リワーク</summary>
    Rework,
    /// <summary>突発</summary>
    Spot,
}

/// <summary>製造指図状態（Spec.md 5.2 ManufacturingOrder）</summary>
public enum ManufacturingOrderStatus
{
    /// <summary>未承認（下書き）</summary>
    Draft,
    /// <summary>承認済</summary>
    Approved,
    /// <summary>展開済（作業指示に展開済み）</summary>
    Released,
    /// <summary>完了</summary>
    Completed,
    /// <summary>取消</summary>
    Canceled,
}

/// <summary>作業指示状態（Spec.md 5.2 WorkOrder）</summary>
public enum WorkOrderStatus
{
    /// <summary>未配布</summary>
    Created,
    /// <summary>配布済</summary>
    Dispatched,
    /// <summary>着手</summary>
    Started,
    /// <summary>完了</summary>
    Completed,
    /// <summary>承認済</summary>
    Approved,
    /// <summary>取消（指図取消に連動）</summary>
    Canceled,
}

/// <summary>ロット由来区分（Spec.md 5.3 Lot）</summary>
public enum LotOriginType
{
    /// <summary>生産</summary>
    Production,
    /// <summary>受入</summary>
    Receiving,
}

/// <summary>在庫ステータス（Spec.md 5.3 Lot）</summary>
public enum LotStockStatus
{
    /// <summary>正常</summary>
    Normal,
    /// <summary>保留</summary>
    OnHold,
    /// <summary>検査待ち</summary>
    AwaitingInspection,
    /// <summary>不良</summary>
    Defective,
    /// <summary>廃棄予定</summary>
    ToBeDiscarded,
}
