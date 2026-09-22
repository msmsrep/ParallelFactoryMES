using MesApp.Api.Localization;
using MesApp.Core.Contracts.Inventory;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Contracts.Quality;
using MesApp.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

// 実績CSVのうち出荷（D-40-20 出荷指示、H-10-10 出荷判定、D-40-30 出荷実行）。
// 出荷判定は品質保証、指示と実行は物流の権限なので、1つの種別にまとめず3つに分ける（ActualCsvKinds）
public sealed partial class ActualCsvService
{
    /// <summary>
    /// 出荷指示（D-40-20-01）。出荷番号が同じ行を1件の指示にまとめ、1行＝1明細（品目）とする。
    /// 後続の出荷判定・出荷実行のCSVは指示を出荷番号で指すため、番号はここで確定させる
    /// </summary>
    private async Task<int> ImportShippingOrdersAsync(
        CsvTable table, List<CsvImportError> errors, string? userId, CancellationToken ct)
    {
        var productIds = await db.Products.AsNoTracking().Where(p => p.IsActive)
            .ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.Ordinal, ct);

        var groups = table.Rows
            .GroupBy(row => table.Value(row, "ShippingNo"))
            .Select(g => g.ToList())
            .ToList();

        var created = 0;
        foreach (var group in groups)
        {
            var head = new CsvRowReader(table, group[0], errors);
            var shippingNo = head.RequiredText("ShippingNo", 50);
            var destination = head.RequiredText("Destination", 200);
            var plannedDate = head.DateOrNull("PlannedDate", null);
            if (head.Failed)
            {
                continue;
            }

            var lines = new List<ShippingLineRequest>();
            var failed = false;
            foreach (var row in group)
            {
                var reader = new CsvRowReader(table, row, errors);
                reader.RequiredText("ProductCode");
                var productId = reader.Reference("ProductCode", null, productIds, "有効な品目");
                var quantity = reader.NumberOrNull("Quantity", null, 0.000001m);
                if (quantity is null && !reader.Failed)
                {
                    reader.Fail(ApiText.T("Quantity（指示数量）は必須です。"));
                }
                // 出荷実行はロットの品目で明細を1つに決めて突き合わせるため、同じ品目の明細を2つ持たせない
                if (productId is not null && lines.Any(l => l.ProductId == productId))
                {
                    reader.Fail(ApiText.T("出荷番号 '{0}' に品目 '{1}' の行が重複しています。", shippingNo, table.Value(row, "ProductCode")));
                }
                if (reader.Failed)
                {
                    failed = true;
                    continue;
                }
                lines.Add(new ShippingLineRequest(productId!.Value, quantity!.Value));
            }
            if (failed)
            {
                continue;
            }

            var outcome = await shipping.CreateAsync(
                new ShippingOrderCreateRequest(destination, plannedDate, lines), shippingNo, userId, ct);
            if (outcome.Failed)
            {
                FailRow(head, outcome.Error!);
                continue;
            }
            created++;
        }
        return created;
    }

    /// <summary>出荷判定（H-10-10-02）。1行＝1件の判定で、列の指定に応じて承認（H-10-10-03）まで進める</summary>
    private async Task<int> ImportShipmentJudgmentsAsync(
        CsvTable table, List<CsvImportError> errors, string userId, CancellationToken ct)
    {
        var created = 0;
        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            var shippingNo = reader.Text("ShippingNo", null);
            var lotNumber = reader.Text("LotNumber", null);
            reader.RequiredText("Result");
            var result = reader.Enum("Result", ShipmentJudgmentResult.Hold, CsvEnumLabels.ShipmentJudgmentResults);
            var approve = reader.Bool("Approve", false);
            var note = reader.Text("Note", null, 1000);
            if (shippingNo is null && lotNumber is null && !reader.Failed)
            {
                reader.Fail(ApiText.T("ShippingNo（出荷番号）と LotNumber（対象ロット番号）のどちらかは必須です。"));
            }
            // 前のファイル（出荷指示・工程展開）で作られた指示・ロットも指せるよう、行ごとにDBを引く
            var shippingOrderId = await ResolveShippingOrderAsync(reader, shippingNo, ct);
            int? lotId = null;
            if (lotNumber is not null)
            {
                lotId = await db.Lots.Where(l => l.LotNumber == lotNumber)
                    .Select(l => (int?)l.Id).FirstOrDefaultAsync(ct);
                if (lotId is null)
                {
                    reader.Fail(ApiText.T("ロット '{0}' は登録されていません（LotNumber）。", lotNumber));
                }
            }
            if (reader.Failed)
            {
                continue;
            }

            var outcome = await judgments.CreateAsync(
                new ShipmentJudgmentCreateRequest(lotId, shippingOrderId, result, note), userId, ct);
            if (outcome.Value is { } judgment && approve)
            {
                outcome = await judgments.ApproveAsync(judgment.Id, userId, ct);
            }
            if (outcome.Failed)
            {
                FailRow(reader, outcome.Error!);
                continue;
            }
            created++;
        }
        return created;
    }

    /// <summary>
    /// 出荷実行（D-40-30）。出荷番号が同じ行を1回の出荷にまとめ、1行＝1ロットの引落しとする。
    /// 出荷判定のゲートとロットの使用可否は単票APIと同じく <see cref="ShippingService.ShipAsync"/> が判定する
    /// </summary>
    private async Task<int> ImportShipmentsAsync(
        CsvTable table, List<CsvImportError> errors, string? userId, CancellationToken ct)
    {
        var locationIds = await db.Locations.AsNoTracking()
            .ToDictionaryAsync(l => l.Code, l => l.Id, StringComparer.Ordinal, ct);

        var groups = table.Rows
            .GroupBy(row => table.Value(row, "ShippingNo"))
            .Select(g => g.ToList())
            .ToList();

        var created = 0;
        foreach (var group in groups)
        {
            var head = new CsvRowReader(table, group[0], errors);
            var shippingNo = head.RequiredText("ShippingNo");
            var shippingOrderId = await ResolveShippingOrderAsync(head, shippingNo, ct);
            if (head.Failed)
            {
                continue;
            }

            var lines = new List<ShipLineRequest>();
            var failed = false;
            foreach (var row in group)
            {
                var reader = new CsvRowReader(table, row, errors);
                var lotNumber = reader.RequiredText("LotNumber");
                reader.RequiredText("LocationCode");
                var locationId = reader.Reference("LocationCode", null, locationIds, "ロケーション");
                var quantity = reader.NumberOrNull("Quantity", null, 0.000001m);
                if (quantity is null && !reader.Failed)
                {
                    reader.Fail(ApiText.T("Quantity（出荷数量）は必須です。"));
                }
                int? lotId = null;
                if (lotNumber.Length > 0)
                {
                    lotId = await db.Lots.Where(l => l.LotNumber == lotNumber)
                        .Select(l => (int?)l.Id).FirstOrDefaultAsync(ct);
                    if (lotId is null)
                    {
                        reader.Fail(ApiText.T("ロット '{0}' は登録されていません（LotNumber）。", lotNumber));
                    }
                }
                if (reader.Failed)
                {
                    failed = true;
                    continue;
                }
                lines.Add(new ShipLineRequest(lotId!.Value, locationId!.Value, quantity!.Value));
            }
            if (failed)
            {
                continue;
            }

            var outcome = await shipping.ShipAsync(shippingOrderId!.Value, new ShipExecuteRequest(lines), userId, ct);
            if (outcome.Failed)
            {
                FailRow(head, outcome.Error!);
                continue;
            }
            created++;
        }
        return created;
    }

    /// <summary>出荷番号→出荷指示ID（番号が空ならnull。見つからなければ行をエラーにする）</summary>
    private async Task<int?> ResolveShippingOrderAsync(CsvRowReader reader, string? shippingNo, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(shippingNo))
        {
            return null;
        }
        var id = await db.ShippingOrders.Where(s => s.ShippingNo == shippingNo)
            .Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);
        if (id is null)
        {
            reader.Fail(ApiText.T("出荷指示 '{0}' は登録されていません（ShippingNo）。", shippingNo));
        }
        return id;
    }
}
