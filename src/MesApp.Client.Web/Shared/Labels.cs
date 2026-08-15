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
}
