using MesApp.Api.Localization;
using MesApp.Core.Contracts.Execution;
using MesApp.Core.Contracts.Inventory;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

// 実績CSVのうち物流の指示（B-50-10 搬送、D-20 ピッキング・払出、D-50-10 棚卸）。
// 搬送は TransferOrderService、ピッキングは PickingService、棚卸は StocktakeService を通す
public sealed partial class ActualCsvService
{
    /// <summary>
    /// 搬送指示（B-50-10-01〜02）。1行＝1件の指示で、Execute=true なら移動の実行まで進める。
    /// 搬送指示には番号が無いので、後のファイルからは指さない
    /// </summary>
    private async Task<int> ImportTransferOrdersAsync(
        CsvTable table, List<CsvImportError> errors, string? userId, CancellationToken ct)
    {
        var locationIds = await db.Locations.AsNoTracking()
            .ToDictionaryAsync(l => l.Code, l => l.Id, StringComparer.Ordinal, ct);

        var created = 0;
        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            var lotNumber = reader.RequiredText("LotNumber");
            reader.RequiredText("FromLocationCode");
            var fromId = reader.Reference("FromLocationCode", null, locationIds, "ロケーション");
            reader.RequiredText("ToLocationCode");
            var toId = reader.Reference("ToLocationCode", null, locationIds, "ロケーション");
            var quantity = reader.NumberOrNull("Quantity", null, 0.000001m);
            var execute = reader.Bool("Execute", false);
            if (quantity is null && !reader.Failed)
            {
                reader.Fail(ApiText.T("Quantity（数量）は必須です。"));
            }
            var lotId = lotNumber.Length == 0 ? null : await LotIdAsync(reader, lotNumber, "LotNumber", ct);
            if (reader.Failed)
            {
                continue;
            }

            var outcome = await transfers.CreateAsync(
                new TransferOrderRequest(lotId!.Value, quantity!.Value, fromId!.Value, toId!.Value), userId, ct);
            if (!outcome.Failed && execute)
            {
                outcome = await transfers.ExecuteAsync(outcome.Value!.Id, userId, ct);
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
    /// ピッキング指示（D-20-10-01〜02。出荷ピッキングは D-40-20）。ピッキング番号が同じ行を1件の指示にまとめ、
    /// 1行＝1明細（品目と数量）。ロット・ロケーションは単票と同じく先入れ先出し（有効期限優先）で自動引当する。
    /// Execute=true なら払出まで進める
    /// </summary>
    private async Task<int> ImportPickingOrdersAsync(
        CsvTable table, List<CsvImportError> errors, string? userId, CancellationToken ct)
    {
        var workOrders = await WorkOrderKeysAsync(ct);
        var productIds = await db.Products.AsNoTracking().Where(p => p.IsActive)
            .ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.Ordinal, ct);

        var created = 0;
        foreach (var group in table.Rows.GroupBy(row => table.Value(row, "PickingNo")).Select(g => g.ToList()))
        {
            var head = new CsvRowReader(table, group[0], errors);
            var pickingNo = head.RequiredText("PickingNo", 50);
            var type = head.Enum("Type", PickingOrderType.ProcessIssue, CsvEnumLabels.PickingOrderTypes);
            var execute = head.Bool("Execute", false);
            int? workOrderId = null;
            int? shippingOrderId = null;
            if (type == PickingOrderType.ProcessIssue)
            {
                workOrderId = ResolveWorkOrder(head, workOrders);
            }
            else if (head.Text("ShippingNo", null) is not { } shippingNo)
            {
                head.Fail(ApiText.T("出荷ピッキングには ShippingNo（出荷番号）が必要です。"));
            }
            else
            {
                shippingOrderId = await db.ShippingOrders.Where(s => s.ShippingNo == shippingNo)
                    .Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);
                if (shippingOrderId is null)
                {
                    head.Fail(ApiText.T("出荷指示 '{0}' は登録されていません（ShippingNo）。", shippingNo));
                }
            }

            var lines = new List<PickingRequestLine>();
            var failed = head.Failed;
            foreach (var row in group)
            {
                var reader = row == group[0] ? head : new CsvRowReader(table, row, errors);
                reader.RequiredText("ProductCode");
                var productId = reader.Reference("ProductCode", null, productIds, "有効な品目");
                var quantity = reader.NumberOrNull("Quantity", null, 0.000001m);
                if (quantity is null && !reader.Failed)
                {
                    reader.Fail(ApiText.T("Quantity（数量）は必須です。"));
                }
                if (!reader.Failed)
                {
                    lines.Add(new PickingRequestLine(productId!.Value, quantity!.Value));
                }
                failed |= reader.Failed;
            }
            if (failed)
            {
                continue;
            }

            var outcome = await picking.CreateAsync(
                new PickingOrderCreateRequest(type, workOrderId, shippingOrderId, lines), pickingNo, userId, ct);
            if (!outcome.Failed && execute)
            {
                outcome = await picking.ExecuteAsync(outcome.Value!.Id, userId, ct);
            }
            if (outcome.Failed)
            {
                FailRow(head, outcome.Error!);
                continue;
            }
            created++;
        }
        return created;
    }

    /// <summary>
    /// 棚卸指示（D-50-10-01）。1行＝1件の指示で、作成時点の現在庫（数量&gt;0）が明細になる。
    /// 後続の実棚数CSVが指示を指すため、棚卸番号は手入力（ST〜は自動採番用として拒否）
    /// </summary>
    private async Task<int> ImportStocktakesAsync(
        CsvTable table, List<CsvImportError> errors, string? userId, CancellationToken ct)
    {
        var locationIds = await db.Locations.AsNoTracking()
            .ToDictionaryAsync(l => l.Code, l => l.Id, StringComparer.Ordinal, ct);

        var created = 0;
        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            var stocktakeNo = reader.RequiredText("StocktakeNo", 50);
            // 空欄は全ロケーション（単票の作成要求と同じ）
            var locationId = reader.Reference("LocationCode", null, locationIds, "ロケーション");
            if (reader.Failed)
            {
                continue;
            }

            var outcome = await stocktakes.CreateAsync(new StocktakeCreateRequest(locationId), stocktakeNo, userId, ct);
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
    /// 実棚数（D-50-10-02）。棚卸番号が同じ行をまとめて登録し、Finalize=true なら確定（D-50-10-05。
    /// 差異を棚卸調整として在庫に反映する）まで進める。明細はロット番号とロケーションで指す
    /// </summary>
    private async Task<int> ImportStocktakeCountsAsync(
        CsvTable table, List<CsvImportError> errors, string? userId, CancellationToken ct)
    {
        var created = 0;
        foreach (var group in table.Rows.GroupBy(row => table.Value(row, "StocktakeNo")).Select(g => g.ToList()))
        {
            var head = new CsvRowReader(table, group[0], errors);
            var stocktakeNo = head.RequiredText("StocktakeNo");
            var finalize = head.Bool("Finalize", false);
            // 前のファイル（棚卸指示CSV）で作った指示も指せるよう、まとまりごとにDBを引く
            var stocktake = stocktakeNo.Length == 0
                ? null
                : await db.Stocktakes.AsNoTracking()
                    .Where(s => s.StocktakeNo == stocktakeNo)
                    .Select(s => new
                    {
                        s.Id,
                        Lines = s.Lines.Select(l => new { l.Id, l.Lot!.LotNumber, l.Location!.Code }).ToList(),
                    })
                    .FirstOrDefaultAsync(ct);
            if (stocktakeNo.Length > 0 && stocktake is null)
            {
                head.Fail(ApiText.T("棚卸 '{0}' は登録されていません（StocktakeNo）。", stocktakeNo));
            }

            var counts = new List<StocktakeCountLine>();
            var failed = head.Failed;
            foreach (var row in group)
            {
                var reader = row == group[0] ? head : new CsvRowReader(table, row, errors);
                var lotNumber = reader.RequiredText("LotNumber");
                var locationCode = reader.RequiredText("LocationCode");
                var counted = reader.NumberOrNull("CountedQuantity", null, 0m);
                if (counted is null && !reader.Failed)
                {
                    reader.Fail(ApiText.T("CountedQuantity（実棚数量）は必須です。"));
                }
                if (!reader.Failed && stocktake is not null)
                {
                    var line = stocktake.Lines.FirstOrDefault(l => l.LotNumber == lotNumber && l.Code == locationCode);
                    if (line is null)
                    {
                        reader.Fail(ApiText.T("棚卸 '{0}' にロット '{1}'・ロケーション '{2}' の明細はありません。",
                            stocktakeNo, lotNumber, locationCode));
                    }
                    else
                    {
                        counts.Add(new StocktakeCountLine(line.Id, counted!.Value));
                    }
                }
                failed |= reader.Failed;
            }
            if (failed)
            {
                continue;
            }

            var outcome = await stocktakes.RegisterCountsAsync(stocktake!.Id, new StocktakeCountRequest(counts), ct);
            if (!outcome.Failed && finalize)
            {
                outcome = await stocktakes.FinalizeAsync(stocktake.Id, userId, ct);
            }
            if (outcome.Failed)
            {
                FailRow(head, outcome.Error!);
                continue;
            }
            created++;
        }
        return created;
    }
}
