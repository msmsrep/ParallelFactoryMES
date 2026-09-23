using MesApp.Api.Localization;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Contracts.Production;
using MesApp.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

// 実績CSVのうち製造指図（A-20-10-01 / B-10-10-01。登録・承認・展開は ManufacturingOrderService）
public sealed partial class ActualCsvService
{
    /// <summary>
    /// 製造指図（A-20-10-01）。1行＝1指図で、列の指定に応じて承認（A-20-20-01）・工程展開（B-10-10-01）まで進める。
    /// 後続の実績CSVは作業指示を「指図番号＋工程順序」で指すため、指図番号はここで確定させる
    /// </summary>
    private async Task<int> ImportManufacturingOrdersAsync(
        CsvTable table, List<CsvImportError> errors, string? userId, CancellationToken ct)
    {
        var productIds = await db.Products.AsNoTracking().Where(p => p.IsActive)
            .ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.Ordinal, ct);

        var created = 0;
        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            // 作業指示番号は「指図番号-工程順序2桁」になるため、その分を残す
            var orderNo = reader.Text("OrderNo", null, 46);
            reader.RequiredText("ProductCode");
            var productId = reader.Reference("ProductCode", null, productIds, "有効な品目");
            var quantity = reader.NumberOrNull("Quantity", null, 0.000001m);
            var dueDate = reader.DateOrNull("DueDate", null);
            var orderType = reader.Enum("OrderType", ManufacturingOrderType.Normal, CsvEnumLabels.OrderTypes);
            var sourceOrderNo = reader.Text("SourceOrderNo", null);
            var note = reader.Text("Note", null, 1000);
            var approve = reader.Bool("Approve", false);
            var expand = reader.Bool("Expand", false);
            var outputLotNumber = reader.Text("OutputLotNumber", null, 60);
            if (quantity is null && !reader.Failed)
            {
                reader.Fail(ApiText.T("Quantity（指図数量）は必須です。"));
            }
            if (expand && !approve)
            {
                reader.Fail(ApiText.T("工程展開は承認済みの指図だけに行えます。Expand を true にするときは Approve も true にしてください。"));
            }
            if (outputLotNumber is not null && !expand)
            {
                reader.Fail(ApiText.T("OutputLotNumber（産出ロット番号）は工程展開するとき（Expand=true）だけ指定できます。"));
            }

            int? sourceOrderId = null;
            if (sourceOrderNo is not null)
            {
                // 前の行で登録した指図も保存済みなので、DBを引けば同じファイル内の指図を指せる
                sourceOrderId = await db.ManufacturingOrders.Where(o => o.OrderNo == sourceOrderNo)
                    .Select(o => (int?)o.Id).FirstOrDefaultAsync(ct);
                if (sourceOrderId is null)
                {
                    reader.Fail(ApiText.T("元指図 '{0}' は登録されていません（SourceOrderNo）。", sourceOrderNo));
                }
            }
            if (reader.Failed)
            {
                continue;
            }

            var outcome = await orders.CreateAsync(
                new CreateManufacturingOrderRequest(productId!.Value, quantity!.Value, dueDate, orderType, sourceOrderId, note),
                orderNo, userId, ct);
            if (outcome.Order is { } order && approve)
            {
                outcome = await orders.ApproveAsync(order, userId, ct);
                if (outcome.Order is not null && expand)
                {
                    outcome = await orders.ExpandAsync(order, outputLotNumber, ct);
                }
            }
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
