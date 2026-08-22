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

/// <summary>不良理由の区分（Spec.md 5.1 DefectReason。C-40-10-01 不良項目別分析の集計軸）</summary>
public enum DefectReasonCategory
{
    /// <summary>材質・部材</summary>
    Material,
    /// <summary>加工・作業</summary>
    Process,
    /// <summary>設備</summary>
    Equipment,
    /// <summary>人的要因</summary>
    Human,
    /// <summary>その他</summary>
    Other,
}

/// <summary>作業指示の状態変更の契機（Spec.md 5.2 WorkOrderStatusHistory）</summary>
public enum WorkOrderStatusChangeSource
{
    /// <summary>差立（配布）（B-10-20）</summary>
    Dispatch,
    /// <summary>着手（B-30-30-01）</summary>
    Start,
    /// <summary>実績入力による作業完了報告（B-30-30-06）</summary>
    ProductionRecord,
    /// <summary>製造完了承認（B-40-10-10）</summary>
    Approval,
    /// <summary>指図取消への連動（A-20）</summary>
    OrderCancel,
}

/// <summary>ロット系譜の関係区分（Spec.md 5.3 LotGenealogy。D-10-30-05〜07）</summary>
public enum LotRelationType
{
    /// <summary>分割（1ロット → 複数ロット）</summary>
    Split,
    /// <summary>統合（複数ロット → 1ロット）</summary>
    Merge,
    /// <summary>品目振替・ロット振替</summary>
    Transfer,
}

/// <summary>ロット状態変更の契機（Spec.md 5.3 LotStatusHistory）</summary>
public enum LotStatusChangeSource
{
    /// <summary>在庫ステータス変更操作（D-10-30-08）</summary>
    Manual,
    /// <summary>検査指示・判定（C-20）</summary>
    Inspection,
    /// <summary>不適合の対応指示・承認（C-30）</summary>
    Nonconformance,
    /// <summary>受入取消（D-10-10-04）</summary>
    Receiving,
}

/// <summary>段取り区分（B-20-50 前段取り／B-40-40 後段取り）</summary>
public enum SetupType
{
    /// <summary>前段取り</summary>
    Pre,
    /// <summary>後段取り</summary>
    Post,
}

/// <summary>部材投入の記録方式（Spec.md 5.2 MaterialConsumption）</summary>
public enum ConsumptionMethod
{
    /// <summary>手動記録（B-30-20-01）</summary>
    Manual,
    /// <summary>バックフラッシュ（MBOM×完了数量から自動算出。B-40-10-09）</summary>
    Backflush,
}

/// <summary>作業時間区分（B-30-30-02 直接／F-30-20-02 間接）</summary>
public enum WorkTimeType
{
    /// <summary>直接作業（作業指示に紐づく）</summary>
    Direct,
    /// <summary>間接作業（段取り・部材準備・設備メンテ等）</summary>
    Indirect,
}

/// <summary>トラブル区分（B-60-10-02。QCDS）</summary>
public enum TroubleCategory
{
    /// <summary>品質（Quality）</summary>
    Quality,
    /// <summary>コスト（Cost）</summary>
    Cost,
    /// <summary>納期（Delivery）</summary>
    Delivery,
    /// <summary>安全（Safety）</summary>
    Safety,
}

/// <summary>トラブル報告状態</summary>
public enum TroubleStatus
{
    /// <summary>発生（未対応）</summary>
    Open,
    /// <summary>対応中</summary>
    InProgress,
    /// <summary>完了</summary>
    Closed,
}

/// <summary>搬送・移動指示状態（Spec.md 5.2 TransferOrder）</summary>
public enum TransferOrderStatus
{
    /// <summary>指示</summary>
    Instructed,
    /// <summary>完了</summary>
    Completed,
    /// <summary>取消</summary>
    Canceled,
}

/// <summary>在庫トランザクション区分（Spec.md 5.3 InventoryTransaction）</summary>
public enum InventoryTransactionType
{
    /// <summary>受入</summary>
    Receipt,
    /// <summary>入庫（完成品・半製品の在庫計上）</summary>
    PutAway,
    /// <summary>出庫</summary>
    Issue,
    /// <summary>払出（工程払出）</summary>
    ProcessIssue,
    /// <summary>払出戻し</summary>
    IssueReturn,
    /// <summary>移動</summary>
    Move,
    /// <summary>調整</summary>
    Adjust,
    /// <summary>廃棄</summary>
    Discard,
    /// <summary>返品</summary>
    Return,
    /// <summary>振替（品目振替・ロット振替）</summary>
    Transfer,
    /// <summary>分割</summary>
    Split,
    /// <summary>統合</summary>
    Merge,
    /// <summary>棚卸調整</summary>
    StocktakeAdjust,
    /// <summary>出荷</summary>
    Ship,
}

/// <summary>ピッキング指示区分（Spec.md 5.3 PickingOrder）</summary>
public enum PickingOrderType
{
    /// <summary>工程払出（D-20）</summary>
    ProcessIssue,
    /// <summary>出荷（D-40-20）</summary>
    Shipping,
}

/// <summary>ピッキング指示状態</summary>
public enum PickingOrderStatus
{
    /// <summary>指示</summary>
    Instructed,
    /// <summary>完了（払出済）</summary>
    Completed,
    /// <summary>取消</summary>
    Canceled,
}

/// <summary>出荷指示状態（Spec.md 5.3 ShippingOrder）</summary>
public enum ShippingOrderStatus
{
    /// <summary>指示</summary>
    Instructed,
    /// <summary>完了（出荷済）</summary>
    Completed,
    /// <summary>取消</summary>
    Canceled,
}

/// <summary>棚卸状態（Spec.md 5.3 Stocktake）</summary>
public enum StocktakeStatus
{
    /// <summary>指示（実棚入力中）</summary>
    Instructed,
    /// <summary>確定（差異調整済み）</summary>
    Finalized,
    /// <summary>取消</summary>
    Canceled,
}

/// <summary>検査指示の検査種別（Spec.md 5.4 InspectionOrder。C-20）</summary>
public enum InspectionOrderType
{
    /// <summary>受入検査</summary>
    Receiving,
    /// <summary>工程内検査</summary>
    InProcess,
    /// <summary>製品完成品検査</summary>
    FinalProduct,
    /// <summary>サンプル検査</summary>
    Sample,
    /// <summary>再検査（C-20-50-01）</summary>
    Reinspection,
}

/// <summary>検査指示状態（Spec.md 5.4 InspectionOrder）</summary>
public enum InspectionOrderStatus
{
    /// <summary>指示</summary>
    Instructed,
    /// <summary>実施中</summary>
    InProgress,
    /// <summary>判定済</summary>
    Judged,
    /// <summary>承認済</summary>
    Approved,
    /// <summary>取消</summary>
    Canceled,
}

/// <summary>検査判定（C-20-10-04）</summary>
public enum InspectionJudgment
{
    /// <summary>合格</summary>
    Pass,
    /// <summary>不合格</summary>
    Fail,
}

/// <summary>不適合・逸脱の発生元（Spec.md 5.4 NonconformanceReport）</summary>
public enum NonconformanceSource
{
    /// <summary>生産実績（B-40-30）</summary>
    Production,
    /// <summary>検査（C-20）</summary>
    Inspection,
    /// <summary>受入（D-10-10）</summary>
    Receiving,
}

/// <summary>不適合対応指示（リワーク/保留/廃棄/特採。C-30-20-01）</summary>
public enum NonconformanceAction
{
    /// <summary>リワーク</summary>
    Rework,
    /// <summary>保留</summary>
    Hold,
    /// <summary>廃棄</summary>
    Discard,
    /// <summary>特別採用（特採。C-30-20-03）</summary>
    SpecialAcceptance,
}

/// <summary>不適合・逸脱状態</summary>
public enum NonconformanceStatus
{
    /// <summary>発生（未対応）</summary>
    Open,
    /// <summary>対応指示済</summary>
    ActionInstructed,
    /// <summary>対応完了</summary>
    ActionCompleted,
    /// <summary>承認済（クローズ）</summary>
    Closed,
}

/// <summary>出荷判定結果（可/保留/特採。H-10-10-02）</summary>
public enum ShipmentJudgmentResult
{
    /// <summary>可</summary>
    Approved,
    /// <summary>保留</summary>
    Hold,
    /// <summary>特別採用（特採）</summary>
    SpecialAcceptance,
}

/// <summary>設備稼働ログの状態（Spec.md 5.5 EquipmentLog。B-40-20、E-20-10）</summary>
public enum EquipmentLogStatus
{
    /// <summary>稼働</summary>
    Running,
    /// <summary>停止</summary>
    Stopped,
    /// <summary>段取り</summary>
    Setup,
    /// <summary>故障</summary>
    Failure,
}

/// <summary>保全種別（定期/計画外。Spec.md 5.5 MaintenancePlan）</summary>
public enum MaintenanceCategory
{
    /// <summary>定期</summary>
    Periodic,
    /// <summary>計画外</summary>
    Unplanned,
}

/// <summary>保全計画状態（E-30-10）</summary>
public enum MaintenancePlanStatus
{
    /// <summary>計画</summary>
    Planned,
    /// <summary>指示発行済</summary>
    Ordered,
    /// <summary>完了</summary>
    Completed,
    /// <summary>取消</summary>
    Canceled,
}

/// <summary>保全指示の依頼区分（計画/突発依頼。E-30-20、E-30-30）</summary>
public enum MaintenanceRequestType
{
    /// <summary>計画（保全計画に基づく）</summary>
    Planned,
    /// <summary>突発依頼（計画外の保全依頼 E-30-30-01）</summary>
    Spot,
}

/// <summary>保全指示状態</summary>
public enum MaintenanceOrderStatus
{
    /// <summary>指示</summary>
    Instructed,
    /// <summary>完了（実績登録済み）</summary>
    Completed,
    /// <summary>取消</summary>
    Canceled,
}
