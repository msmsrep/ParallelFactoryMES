using MesApp.Core.Contracts.Masters;

namespace MesApp.Api.Services;

/// <summary>実績CSVの種別（列定義と、取込に必要なロール）</summary>
/// <param name="WriteRoles">
/// 取込に必要なロールグループ。<b>単票APIの <c>[Authorize(Roles = ...)]</c> と同じ定数を使う</b>
/// （片方だけ緩いと、画面からは登録できない実績がCSVからは入る。Spec.md 7.4）
/// </param>
public sealed record ActualCsvKind(CsvKindInfo Info, string WriteRoles);

/// <summary>
/// 実績（取引データ）のCSV一括取込に対応する種別（Spec.md 3.8）。
/// マスタCSVと違い<b>取込は常に新規登録</b>で、既存の実績を更新しない（訂正は理由付きの訂正機能で行う）。
/// 他のマスタ・実績はIDではなくコード・番号で参照する。
/// </summary>
public static class ActualCsvKinds
{
    public const string Receiving = "receiving";

    public static readonly List<ActualCsvKind> All =
    [
        new(new CsvKindInfo(Receiving, "受入", false,
        [
            new("ProductCode", "品目コード", true, "登録済みで有効な品目コード"),
            new("Quantity", "受入数量", true, "0より大きい数値"),
            new("LocationCode", "入庫先ロケーションコード", true, "登録済みで有効なロケーションコード"),
            new("LotNumber", "受入ロット番号", false, "空欄なら自動採番。既存のロット番号と重複できない"),
            new("ExpiresOn", "有効期限", false, "yyyy-MM-dd"),
            new("Note", "備考", false, null),
        ]), MesRoleGroups.InventoryManage),
    ];

    public static ActualCsvKind? Find(string kind) =>
        All.FirstOrDefault(k => string.Equals(k.Info.Kind, kind, StringComparison.OrdinalIgnoreCase));
}
