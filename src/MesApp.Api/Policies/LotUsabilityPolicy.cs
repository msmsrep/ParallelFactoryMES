using MesApp.Core.Localization;
using MesApp.Api.Localization;
using System.Linq.Expressions;
using MesApp.Core.Entities;

namespace MesApp.Api.Policies;

/// <summary>
/// ロットを業務で使ってよいかの判定（Spec.md 3.4 部材投入／3.5 在庫・出荷）。
/// 部材投入・自動引当（FEFO）・出荷実行はいずれも「使える現品か」を同じ条件で判断する必要があるため、
/// 判定はコントローラに書かずここへ集約する。条件を変えるときはこのクラスだけを直す。
/// </summary>
/// <remarks>
/// 特採（不適合の条件付き使用可）はロットのステータスを正常へ戻すことで表現する
/// （NonconformanceController の特採承認処理。Spec.md 5.7）。
/// そのため本ポリシーに特採の例外分岐は置かない。
/// </remarks>
public static class LotUsabilityPolicy
{
    /// <summary>引当・投入・出荷の対象にしてよい在庫ステータスか（保留・検査待ち・不良・廃棄予定は不可）</summary>
    public static bool IsUsableStatus(LotStockStatus status) => status == LotStockStatus.Normal;

    /// <summary>業務日付時点で有効期限切れか（期限なしのロットは切れない）</summary>
    public static bool IsExpired(DateOnly? expiresOn, DateOnly businessDate) =>
        expiresOn is DateOnly limit && limit < businessDate;

    /// <summary>
    /// FEFO引当・在庫検索で「使える在庫」に絞り込む条件（EFで評価するためExpressionで返す）
    /// </summary>
    public static Expression<Func<InventoryStock, bool>> UsableStock(DateOnly businessDate) =>
        s => s.Lot!.StockStatus == LotStockStatus.Normal
             && (s.Lot!.ExpiresOn == null || s.Lot!.ExpiresOn >= businessDate);

    /// <summary>部材投入の可否。投入できない場合は日本語の理由を返す（可ならnull）</summary>
    public static string? CheckIssuable(Lot lot, DateOnly businessDate)
    {
        if (!IsUsableStatus(lot.StockStatus))
        {
            return ApiText.T("ステータス '{0}' のロット '{1}' は投入できません。", EnumLabels.Of(lot.StockStatus), lot.LotNumber);
        }
        if (IsExpired(lot.ExpiresOn, businessDate))
        {
            return ApiText.T("有効期限切れのロット '{0}' は投入できません（期限 {1:yyyy-MM-dd}）。", lot.LotNumber, lot.ExpiresOn);
        }
        return null;
    }

    /// <summary>出荷の可否。出荷できない場合は日本語の理由を返す（可ならnull）</summary>
    public static string? CheckShippable(Lot lot, DateOnly businessDate)
    {
        if (!IsUsableStatus(lot.StockStatus))
        {
            return ApiText.T("ステータス '{0}' のロット '{1}' は出荷できません。", EnumLabels.Of(lot.StockStatus), lot.LotNumber);
        }
        if (IsExpired(lot.ExpiresOn, businessDate))
        {
            return ApiText.T("有効期限切れのロット '{0}' は出荷できません（期限 {1:yyyy-MM-dd}）。", lot.LotNumber, lot.ExpiresOn);
        }
        return null;
    }
}
