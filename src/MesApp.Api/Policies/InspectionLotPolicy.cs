using MesApp.Api.Localization;
using MesApp.Core.Entities;
using MesApp.Core.Localization;

namespace MesApp.Api.Policies;

/// <summary>
/// 検査指示が対象ロットを検査待ちで拘束してよいかの判定（Spec.md 5.7 検査とロットの在庫ステータス。C-20）。
/// <para>
/// 検査指示の発行はロットを検査待ちにし、判定で正常/不良へ、取消で発行前のステータスへ戻す。
/// 保留・廃棄予定のロットへ発行できると、発行→取消や発行→合格で保留が外れてしまう（保留の解除は不適合の処置で行う）。
/// 判定前の指示が2件あると、先に合格した方でロットが正常になり、もう一方の判定を待たずに使えてしまう。
/// </para>
/// 単票API（<c>InspectionOrdersController</c>）と実績CSV取込の両方が <c>InspectionService</c> 経由でここを通る。
/// </summary>
public static class InspectionLotPolicy
{
    /// <summary>
    /// ロットを拘束する検査指示（サンプル検査以外）を発行できるか。できなければ日本語の理由を返す。
    /// 正常・検査待ちのロットに発行できる。再検査（C-20-50-01）に限り、不合格で不良になったロットにも発行できる
    /// </summary>
    /// <param name="pendingOrderNo">そのロットを拘束している判定前の検査指示の番号（無ければ null）</param>
    public static string? CheckIssuable(Lot lot, InspectionOrderType type, string? pendingOrderNo)
    {
        if (pendingOrderNo is not null)
        {
            return ApiText.T("ロット '{0}' には判定前の検査指示 {1} があります。判定するか取り消してから発行してください。",
                lot.LotNumber, pendingOrderNo);
        }
        var issuable = lot.StockStatus is LotStockStatus.Normal or LotStockStatus.AwaitingInspection
                       || (type == InspectionOrderType.Reinspection && lot.StockStatus == LotStockStatus.Defective);
        return issuable
            ? null
            : ApiText.T("在庫ステータスが「{0}」のロット '{1}' には検査指示を発行できません（不良ロットは再検査のみ。保留は不適合の処置で解除してください）。",
                EnumLabels.Of(lot.StockStatus), lot.LotNumber);
    }
}
