using MesApp.Api.Localization;
using MesApp.Core.Contracts.Inventory;
using MesApp.Core.Contracts.Masters;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

// 実績CSVのうち在庫（D-10-10-02 受入）
public sealed partial class ActualCsvService
{
    /// <summary>受入（D-10-10-02）。1行＝1ロットの受入登録</summary>
    private async Task<int> ImportReceivingAsync(
        CsvTable table, List<CsvImportError> errors, string? userId, CancellationToken ct)
    {
        // 無効なマスタは候補に入れない（単票APIも無効な品目・ロケーションへの受入を拒否する）
        var productIds = await db.Products.AsNoTracking().Where(p => p.IsActive)
            .ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.Ordinal, ct);
        var locationIds = await db.Locations.AsNoTracking().Where(l => l.IsActive)
            .ToDictionaryAsync(l => l.Code, l => l.Id, StringComparer.Ordinal, ct);

        var created = 0;
        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            reader.RequiredText("ProductCode");
            var productId = reader.Reference("ProductCode", null, productIds, "有効な品目");
            var quantity = reader.NumberOrNull("Quantity", null, 0.000001m);
            reader.RequiredText("LocationCode");
            var locationId = reader.Reference("LocationCode", null, locationIds, "有効なロケーション");
            var lotNumber = reader.Text("LotNumber", null, 60);
            var expiresOn = reader.DateOrNull("ExpiresOn", null);
            var note = reader.Text("Note", null, 500);
            if (quantity is null && !reader.Failed)
            {
                reader.Fail(ApiText.T("Quantity（受入数量）は必須です。"));
            }
            if (reader.Failed)
            {
                continue;
            }

            var outcome = await receiving.ReceiveAsync(
                new ReceivingRequest(productId!.Value, quantity!.Value, locationId!.Value, lotNumber, expiresOn, note),
                userId, ct);
            if (outcome.Error is not null)
            {
                FailRow(reader, outcome.Error);
                continue;
            }
            created++;
        }
        return created;
    }
}
