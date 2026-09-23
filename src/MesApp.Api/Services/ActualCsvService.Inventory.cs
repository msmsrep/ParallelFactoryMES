using MesApp.Api.Localization;
using MesApp.Core.Contracts.Inventory;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

// 実績CSVのうち在庫（D-10-10-02 受入、D-10-30 在庫オペレーション）
public sealed partial class ActualCsvService
{
    /// <summary>在庫オペレーションCSVの操作（Operation 列。単票の api/inventory/{操作} と1対1）</summary>
    private enum InventoryOperation { Move, Adjust, Status, Split, Merge, Transfer, Discard, Return, IssueReturn }

    private static readonly IReadOnlyDictionary<string, InventoryOperation> InventoryOperations =
        new Dictionary<string, InventoryOperation>(StringComparer.OrdinalIgnoreCase)
        {
            ["Move"] = InventoryOperation.Move, ["移動"] = InventoryOperation.Move,
            ["Adjust"] = InventoryOperation.Adjust, ["調整"] = InventoryOperation.Adjust,
            ["Status"] = InventoryOperation.Status, ["状態変更"] = InventoryOperation.Status,
            ["Split"] = InventoryOperation.Split, ["分割"] = InventoryOperation.Split,
            ["Merge"] = InventoryOperation.Merge, ["統合"] = InventoryOperation.Merge,
            ["Transfer"] = InventoryOperation.Transfer, ["振替"] = InventoryOperation.Transfer,
            ["Discard"] = InventoryOperation.Discard, ["廃棄"] = InventoryOperation.Discard,
            ["Return"] = InventoryOperation.Return, ["返品"] = InventoryOperation.Return,
            ["IssueReturn"] = InventoryOperation.IssueReturn, ["払出戻し"] = InventoryOperation.IssueReturn,
        };

    /// <summary>操作ごとの必須列（単票の要求で必須の項目。数量調整の理由は API でも必須）</summary>
    private static string[] RequiredColumnsOf(InventoryOperation operation) => operation switch
    {
        InventoryOperation.Move => ["LocationCode", "ToLocationCode", "Quantity"],
        InventoryOperation.Adjust => ["LocationCode", "Quantity", "Reason"],
        InventoryOperation.Status => ["Status"],
        InventoryOperation.Merge => ["LocationCode", "TargetLotNumber"],
        _ => ["LocationCode", "Quantity"],
    };

    /// <summary>
    /// 在庫オペレーション（D-10-30 移動・調整・状態変更・分割・統合・振替、D-50-30-01 廃棄、D-10-10-05 返品、
    /// D-20-20-03 払出戻し）。1行＝1操作で、行ごとに単票と同じ <see cref="LotOperationService"/> の同名メソッドを呼ぶ。
    /// 分割・振替の新ロット番号（NewLotNumber）を指定すれば、同じファイルの後の行からそのロットを指せる
    /// </summary>
    private async Task<int> ImportInventoryOperationsAsync(
        CsvTable table, List<CsvImportError> errors, string? userId, CancellationToken ct)
    {
        var workOrders = await WorkOrderKeysAsync(ct);
        var locationIds = await db.Locations.AsNoTracking()
            .ToDictionaryAsync(l => l.Code, l => l.Id, StringComparer.Ordinal, ct);
        var productIds = await db.Products.AsNoTracking().Where(p => p.IsActive)
            .ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.Ordinal, ct);

        var created = 0;
        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            reader.RequiredText("Operation");
            var operation = reader.Enum("Operation", InventoryOperation.Move, InventoryOperations);
            var lotNumber = reader.RequiredText("LotNumber");
            var locationId = reader.Reference("LocationCode", null, locationIds, "ロケーション");
            var toLocationId = reader.Reference("ToLocationCode", null, locationIds, "ロケーション");
            // 数量調整は調整後の数量（0 で在庫を無くす）、ほかは操作する数量
            var quantity = reader.NumberOrNull("Quantity", null, 0m);
            var newLotNumber = reader.Text("NewLotNumber", null, 60);
            var targetLotNumber = reader.Text("TargetLotNumber", null);
            var productId = reader.Reference("ProductCode", null, productIds, "有効な品目");
            var status = reader.Enum("Status", LotStockStatus.Normal, CsvEnumLabels.LotStockStatuses);
            var workOrderId = ResolveOptionalWorkOrder(reader, workOrders);
            var reason = reader.Text("Reason", null, 500);
            if (reader.Failed)
            {
                continue;
            }
            var missing = RequiredColumnsOf(operation).Where(c => table.Value(row, c) is null).ToList();
            if (missing.Count > 0)
            {
                reader.Fail(ApiText.T("操作 '{0}' には {1} が必要です。", operation, string.Join(", ", missing)));
                continue;
            }
            if (operation != InventoryOperation.Adjust && quantity == 0)
            {
                reader.Fail(ApiText.T("Quantity（数量）は0より大きい値にしてください。"));
                continue;
            }
            // 前の行の分割・振替で作ったロットも指せるよう、行ごとにDBを引く
            var lotId = await LotIdAsync(reader, lotNumber, "LotNumber", ct);
            var targetLotId = targetLotNumber is null ? null : await LotIdAsync(reader, targetLotNumber, "TargetLotNumber", ct);
            if (reader.Failed)
            {
                continue;
            }

            var lot = lotId!.Value;
            var outcome = operation switch
            {
                InventoryOperation.Move => await lotOperations.MoveAsync(
                    lot, locationId!.Value, toLocationId!.Value, quantity!.Value, userId, ct),
                InventoryOperation.Adjust => await lotOperations.AdjustAsync(
                    lot, locationId!.Value, quantity!.Value, reason, userId, ct),
                InventoryOperation.Status => await lotOperations.ChangeStatusAsync(lot, status, reason, userId, ct),
                InventoryOperation.Split => await lotOperations.SplitAsync(
                    lot, locationId!.Value, quantity!.Value, newLotNumber, userId, ct),
                InventoryOperation.Merge => await lotOperations.MergeAsync(lot, targetLotId!.Value, locationId!.Value, userId, ct),
                InventoryOperation.Transfer => await lotOperations.TransferAsync(
                    lot, locationId!.Value, quantity!.Value, productId, newLotNumber, userId, ct),
                InventoryOperation.Discard => await lotOperations.DiscardAsync(
                    lot, locationId!.Value, quantity!.Value, reason, userId, ct),
                InventoryOperation.Return => await lotOperations.ReturnAsync(
                    lot, locationId!.Value, quantity!.Value, reason, userId, ct),
                _ => await lotOperations.IssueReturnAsync(lot, locationId!.Value, quantity!.Value, workOrderId, userId, ct),
            };
            if (outcome.Failed)
            {
                FailRow(reader, outcome.Error!);
                continue;
            }
            created++;
        }
        return created;
    }

    /// <summary>ロット番号→ロットID（無ければ行エラー）</summary>
    private async Task<int?> LotIdAsync(CsvRowReader reader, string lotNumber, string column, CancellationToken ct)
    {
        var lotId = await db.Lots.Where(l => l.LotNumber == lotNumber).Select(l => (int?)l.Id).FirstOrDefaultAsync(ct);
        if (lotId is null)
        {
            reader.Fail(ApiText.T("ロット '{0}' は登録されていません（{1}）。", lotNumber, column));
        }
        return lotId;
    }

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
