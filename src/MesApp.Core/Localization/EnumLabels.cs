using System.Globalization;
using System.Resources;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Dashboard;
using MesApp.Core.Contracts.Production;
using MesApp.Core.Entities;

namespace MesApp.Core.Localization;

/// <summary>
/// 区分値（enum）とロールの表示名（Spec.md 7.9 多言語対応）。API と画面で同じ表示名を使うため Core に集める
/// （以前は画面・API のあちこちに同じ変換が散らばり、同じ値の表記が画面によって違っていた）。
/// <para>
/// 表示名の原文（日本語）はここに書き、英語の訳は <c>CoreText.en.resx</c> に原文をキーにして持つ。
/// 属性とリフレクションを使わず表で持つのは、Release の Blazor WASM がこのアセンブリをトリミングするため。
/// </para>
/// </summary>
public static class EnumLabels
{
    private static readonly ResourceManager Resources =
        new("MesApp.Core.Localization.CoreText", typeof(EnumLabels).Assembly);

    private static readonly Dictionary<Enum, string> Originals = new()
    {
        [ProductType.Product] = "製品",
        [ProductType.SemiFinished] = "半製品・中間品",
        [ProductType.Material] = "部材",
        [MakeOrBuy.InHouse] = "内製",
        [MakeOrBuy.Outsourced] = "外注",
        [EquipmentStatus.Available] = "稼働可能",
        [EquipmentStatus.Stopped] = "停止中",
        [EquipmentStatus.UnderMaintenance] = "保全中",
        [EquipmentStatus.Retired] = "廃棄・除却",
        [MaintenanceType.None] = "対象外",
        [MaintenanceType.Calendar] = "カレンダ",
        [MaintenanceType.RunTime] = "稼働時間",
        [MaintenanceType.Count] = "使用回数",
        [ToolStatus.Available] = "使用可能",
        [ToolStatus.InUse] = "使用中",
        [ToolStatus.UnderMaintenance] = "メンテナンス中",
        [ToolStatus.Retired] = "廃棄",
        [ToolIssueStatus.Allocated] = "引当済",
        [ToolIssueStatus.Issued] = "払出済",
        [ToolIssueStatus.Returned] = "返却済",
        [ToolIssueStatus.Canceled] = "取消",
        [WorkCenterLevel.Plant] = "工場",
        [WorkCenterLevel.Line] = "ライン",
        [WorkCenterLevel.Area] = "エリア",
        [WorkCenterLevel.WorkCenter] = "作業区",
        [LocationAreaType.MaterialWarehouse] = "部材倉庫",
        [LocationAreaType.InProcess] = "工程内",
        [LocationAreaType.ProductWarehouse] = "製品倉庫",
        [LocationAreaType.ShippingArea] = "出荷場",
        [InspectionType.Receiving] = "受入",
        [InspectionType.InProcess] = "工程内",
        [InspectionType.FinalProduct] = "完成品",
        [InspectionType.Sample] = "サンプル",
        [ChecklistCategory.Common] = "共通",
        [ChecklistCategory.Process] = "工程",
        [ChecklistCategory.Product] = "品目",
        [ChecklistCategory.Setup] = "段取り",
        [ChecklistCategory.Maintenance] = "保全",
        [ChecklistCategory.Hse] = "HSE",
        [SkillType.Skill] = "スキル",
        [SkillType.Certification] = "資格",
        [ManufacturingOrderType.Normal] = "通常",
        [ManufacturingOrderType.Rework] = "リワーク",
        [ManufacturingOrderType.Spot] = "突発",
        [ManufacturingOrderStatus.Draft] = "未承認",
        [ManufacturingOrderStatus.Approved] = "承認済",
        [ManufacturingOrderStatus.Released] = "展開済",
        [ManufacturingOrderStatus.Completed] = "完了",
        [ManufacturingOrderStatus.Canceled] = "取消",
        [WorkOrderStatus.Created] = "未配布",
        [WorkOrderStatus.Dispatched] = "配布済",
        [WorkOrderStatus.Started] = "着手",
        [WorkOrderStatus.Completed] = "完了",
        [WorkOrderStatus.Approved] = "承認済",
        [WorkOrderStatus.Canceled] = "取消",
        [LotOriginType.Production] = "生産",
        [LotOriginType.Receiving] = "受入",
        [LotStockStatus.Normal] = "正常",
        [LotStockStatus.OnHold] = "保留",
        [LotStockStatus.AwaitingInspection] = "検査待ち",
        [LotStockStatus.Defective] = "不良",
        [LotStockStatus.ToBeDiscarded] = "廃棄予定",
        [DefectReasonCategory.Material] = "材質・部材",
        [DefectReasonCategory.Process] = "加工・作業",
        [DefectReasonCategory.Equipment] = "設備",
        [DefectReasonCategory.Human] = "人的要因",
        [DefectReasonCategory.Other] = "その他",
        [WorkOrderStatusChangeSource.Dispatch] = "差立",
        [WorkOrderStatusChangeSource.Start] = "着手",
        [WorkOrderStatusChangeSource.ProductionRecord] = "実績入力",
        [WorkOrderStatusChangeSource.Approval] = "承認",
        [WorkOrderStatusChangeSource.OrderCancel] = "指図取消",
        [LotRelationType.Split] = "分割",
        [LotRelationType.Merge] = "統合",
        [LotRelationType.Transfer] = "振替",
        [LotStatusChangeSource.Manual] = "在庫操作",
        [LotStatusChangeSource.Inspection] = "検査",
        [LotStatusChangeSource.Nonconformance] = "不適合",
        [LotStatusChangeSource.Receiving] = "受入",
        [SetupType.Pre] = "前段取り",
        [SetupType.Post] = "後段取り",
        [ConsumptionMethod.Manual] = "手動",
        [ConsumptionMethod.Backflush] = "バックフラッシュ",
        [WorkTimeType.Direct] = "直接",
        [WorkTimeType.Indirect] = "間接",
        [TroubleCategory.Quality] = "品質",
        [TroubleCategory.Cost] = "コスト",
        [TroubleCategory.Delivery] = "納期",
        [TroubleCategory.Safety] = "安全",
        [TroubleStatus.Open] = "発生",
        [TroubleStatus.InProgress] = "対応中",
        [TroubleStatus.Closed] = "完了",
        [TransferOrderStatus.Instructed] = "指示中",
        [TransferOrderStatus.Completed] = "完了",
        [TransferOrderStatus.Canceled] = "取消",
        [InventoryTransactionType.Receipt] = "受入",
        [InventoryTransactionType.PutAway] = "入庫",
        [InventoryTransactionType.Issue] = "出庫",
        [InventoryTransactionType.ProcessIssue] = "工程払出",
        [InventoryTransactionType.IssueReturn] = "払出戻し",
        [InventoryTransactionType.Move] = "移動",
        [InventoryTransactionType.Adjust] = "調整",
        [InventoryTransactionType.Discard] = "廃棄",
        [InventoryTransactionType.Return] = "返品",
        [InventoryTransactionType.Transfer] = "振替",
        [InventoryTransactionType.Split] = "分割",
        [InventoryTransactionType.Merge] = "統合",
        [InventoryTransactionType.StocktakeAdjust] = "棚卸調整",
        [InventoryTransactionType.Ship] = "出荷",
        [InventoryTransactionType.MaintenanceIssue] = "保全消費",
        [InventoryTransactionType.SampleRetention] = "サンプル保管",
        [SampleStorageStatus.Stored] = "保管中",
        [SampleStorageStatus.Consumed] = "払出済",
        [SampleStorageStatus.Disposed] = "廃棄済",
        [PickingOrderType.ProcessIssue] = "工程払出",
        [PickingOrderType.Shipping] = "出荷",
        [PickingOrderStatus.Instructed] = "指示",
        [PickingOrderStatus.Completed] = "完了",
        [PickingOrderStatus.Canceled] = "取消",
        [ShippingOrderStatus.Instructed] = "指示",
        [ShippingOrderStatus.Completed] = "完了",
        [ShippingOrderStatus.Canceled] = "取消",
        [StocktakeStatus.Instructed] = "指示",
        [StocktakeStatus.Finalized] = "確定",
        [StocktakeStatus.Canceled] = "取消",
        [InspectionOrderType.Receiving] = "受入",
        [InspectionOrderType.InProcess] = "工程内",
        [InspectionOrderType.FinalProduct] = "完成品",
        [InspectionOrderType.Sample] = "サンプル",
        [InspectionOrderType.Reinspection] = "再検査",
        [InspectionOrderStatus.Instructed] = "指示",
        [InspectionOrderStatus.InProgress] = "実施中",
        [InspectionOrderStatus.Judged] = "判定済",
        [InspectionOrderStatus.Approved] = "承認済",
        [InspectionOrderStatus.Canceled] = "取消",
        [InspectionJudgment.Pass] = "合格",
        [InspectionJudgment.Fail] = "不合格",
        [NonconformanceSource.Production] = "生産実績",
        [NonconformanceSource.Inspection] = "検査",
        [NonconformanceSource.Receiving] = "受入",
        [NonconformanceAction.Rework] = "リワーク",
        [NonconformanceAction.Hold] = "保留",
        [NonconformanceAction.Discard] = "廃棄",
        [NonconformanceAction.SpecialAcceptance] = "特別採用",
        [NonconformanceStatus.Open] = "発生",
        [NonconformanceStatus.ActionInstructed] = "対応指示済",
        [NonconformanceStatus.ActionCompleted] = "対応完了",
        [NonconformanceStatus.Closed] = "クローズ",
        [ShipmentJudgmentResult.Approved] = "可",
        [ShipmentJudgmentResult.Hold] = "保留",
        [ShipmentJudgmentResult.SpecialAcceptance] = "特別採用",
        [EquipmentLogStatus.Running] = "稼働",
        [EquipmentLogStatus.Stopped] = "停止",
        [EquipmentLogStatus.Setup] = "段取り",
        [EquipmentLogStatus.Failure] = "故障",
        [EquipmentLogStatus.Idle] = "アイドル",
        [MaintenancePartCategory.Asset] = "資産管理部品",
        [MaintenancePartCategory.Consumable] = "消耗品",
        [MaintenanceCategory.Periodic] = "定期",
        [MaintenanceCategory.Unplanned] = "計画外",
        [MaintenancePlanStatus.Planned] = "計画",
        [MaintenancePlanStatus.Ordered] = "指示発行済",
        [MaintenancePlanStatus.Completed] = "完了",
        [MaintenancePlanStatus.Canceled] = "取消",
        [MaintenanceRequestType.Planned] = "計画",
        [MaintenanceRequestType.Spot] = "突発依頼",
        [MaintenanceOrderStatus.Instructed] = "指示",
        [MaintenanceOrderStatus.Completed] = "完了",
        [MaintenanceOrderStatus.Canceled] = "取消",
        [WorkOrderDelayKind.OverdueDueDate] = "納期超過",
        [WorkOrderDelayKind.OverrunStandardTime] = "標準時間超過",
        [DashboardPeriodUnit.Day] = "日次",
        [DashboardPeriodUnit.Week] = "週次",
        [DashboardPeriodUnit.Month] = "月次",
        [DashboardAxis.Process] = "工程",
        [DashboardAxis.Product] = "品目",
        [DashboardAxis.Shift] = "直",
        [DashboardAxis.Line] = "ライン",
        [DashboardAxis.WorkCenter] = "作業区",
        [DashboardAxis.Equipment] = "設備",
    };

    private static readonly Dictionary<string, string> RoleOriginals = new()
    {
        [MesRoles.SystemAdmin] = "システム管理者",
        [MesRoles.ProductionManager] = "生産管理",
        [MesRoles.Operator] = "現場作業者",
        [MesRoles.Logistics] = "物流・倉庫",
        [MesRoles.QualityControl] = "品質管理",
        [MesRoles.QualityAssurance] = "品質保証",
        [MesRoles.Maintenance] = "設備保全",
    };

    /// <summary>区分値の表示名（現在の表示言語）。表に無い値は値の名前をそのまま返す</summary>
    public static string Of(Enum value) =>
        Originals.TryGetValue(value, out var original) ? Translate(original) : value.ToString();

    /// <summary>区分値の表示名の原文（日本語）。表示言語に左右されてはいけない用途向け</summary>
    public static string OriginalOf(Enum value) =>
        Originals.TryGetValue(value, out var original) ? original : value.ToString();

    /// <summary>ロールの表示名（現在の表示言語）。表に無いロールは名前をそのまま返す</summary>
    public static string Role(string role) =>
        RoleOriginals.TryGetValue(role, out var original) ? Translate(original) : role;

    /// <summary>原文を現在の表示言語へ訳す。訳が無ければ原文を返す</summary>
    private static string Translate(string original)
    {
        try
        {
            return Resources.GetString(original, CultureInfo.CurrentUICulture) ?? original;
        }
        catch (MissingManifestResourceException)
        {
            return original;
        }
    }
}
