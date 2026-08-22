using MesApp.Core.Entities;

namespace MesApp.Client.Web.Shared;

/// <summary>画面をまたいで使う日本語ラベル変換</summary>
public static class Labels
{
    public static string LotStatus(LotStockStatus status) => status switch
    {
        LotStockStatus.Normal => "正常",
        LotStockStatus.OnHold => "保留",
        LotStockStatus.AwaitingInspection => "検査待ち",
        LotStockStatus.Defective => "不良",
        LotStockStatus.ToBeDiscarded => "廃棄予定",
        _ => status.ToString(),
    };

    public static string DefectCategory(DefectReasonCategory category) => category switch
    {
        DefectReasonCategory.Material => "材質・部材",
        DefectReasonCategory.Process => "加工・作業",
        DefectReasonCategory.Equipment => "設備",
        DefectReasonCategory.Human => "人的要因",
        DefectReasonCategory.Other => "その他",
        _ => category.ToString(),
    };

    public static string LotRelation(LotRelationType relation) => relation switch
    {
        LotRelationType.Split => "分割",
        LotRelationType.Merge => "統合",
        LotRelationType.Transfer => "振替",
        _ => relation.ToString(),
    };

    public static string LotStatusSource(LotStatusChangeSource source) => source switch
    {
        LotStatusChangeSource.Manual => "在庫操作",
        LotStatusChangeSource.Inspection => "検査",
        LotStatusChangeSource.Nonconformance => "不適合",
        LotStatusChangeSource.Receiving => "受入",
        _ => source.ToString(),
    };
}
