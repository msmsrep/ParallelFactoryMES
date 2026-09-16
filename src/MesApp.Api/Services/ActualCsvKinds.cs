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
    public const string ManufacturingOrders = "manufacturing-orders";

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
        new(new CsvKindInfo(ManufacturingOrders, "製造指図", false,
        [
            new("OrderNo", "指図番号", false,
                "空欄なら自動採番。後続の実績CSVから指図を指すので指定を推奨。MOで始まる番号は自動採番用のため使えない"),
            new("ProductCode", "品目コード", true, "登録済みで有効な品目コード（工順が必要）"),
            new("Quantity", "指図数量", true, "0より大きい数値"),
            new("DueDate", "納期", false, "yyyy-MM-dd"),
            new("OrderType", "指図区分", false, "Normal（通常）/ Spot（突発）/ Rework（リワーク）。省略時は通常"),
            new("SourceOrderNo", "元指図番号", false, "リワーク指図のときだけ必須。同じファイルの前の行の指図も指せる"),
            new("Note", "備考", false, null),
            new("Approve", "承認する", false, "true で登録に続けて承認する"),
            new("Expand", "工程展開する", false, "true で承認に続けて作業指示へ展開する（Approve も true が必要）"),
            new("OutputLotNumber", "産出ロット番号", false, "工程展開するときのロット番号。空欄なら自動採番"),
        ]), MesRoleGroups.ProductionManage),
    ];

    public static ActualCsvKind? Find(string kind) =>
        All.FirstOrDefault(k => string.Equals(k.Info.Kind, kind, StringComparison.OrdinalIgnoreCase));
}
