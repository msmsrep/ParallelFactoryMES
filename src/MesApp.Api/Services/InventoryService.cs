using MesApp.Api.Localization;
using MesApp.Api.Policies;
using MesApp.Core.Abstractions;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>在庫操作の業務エラー（在庫不足など。コントローラで400に変換する）</summary>
public class InventoryException(string message) : Exception(message);

/// <summary>
/// 在庫増減の一元管理（Spec.md 5.3）。全ての在庫変動は本サービス経由で行い、
/// InventoryTransactionに履歴を残す。SaveChangesは呼び出し側が行う（1操作＝1トランザクション）。
/// 同時実行はInventoryStock.ConcurrencyStampの楽観的制御（改訂7）で検出する。
/// </summary>
public class InventoryService(MesAppDbContext db, IBusinessDateService businessDate)
{
    /// <summary>在庫加算（受入・入庫・払出戻し・振替先など）</summary>
    public async Task<InventoryStock> AddAsync(
        Lot lot, int locationId, decimal quantity, InventoryTransactionType type,
        string? userId, int? workOrderId = null, int? pickingOrderId = null,
        int? shippingOrderId = null, string? note = null, CancellationToken ct = default)
    {
        var stock = await FindStockAsync(lot.Id, locationId, ct);
        if (stock is null)
        {
            stock = new InventoryStock
            {
                ProductId = lot.ProductId,
                LotId = lot.Id,
                LocationId = locationId,
                Quantity = 0,
            };
            db.InventoryStocks.Add(stock);
        }
        stock.Quantity += quantity;
        stock.ConcurrencyStamp = Guid.NewGuid().ToString("N");

        AddTransaction(type, lot, quantity, userId,
            toLocationId: locationId, workOrderId: workOrderId,
            pickingOrderId: pickingOrderId, shippingOrderId: shippingOrderId, note: note);
        return stock;
    }

    /// <summary>在庫減算（出庫・払出・廃棄・出荷など）。在庫不足はInventoryException</summary>
    public async Task<InventoryStock> RemoveAsync(
        Lot lot, int locationId, decimal quantity, InventoryTransactionType type,
        string? userId, int? workOrderId = null, int? pickingOrderId = null,
        int? shippingOrderId = null, string? note = null, CancellationToken ct = default)
    {
        var stock = await FindStockAsync(lot.Id, locationId, ct);
        if (stock is null || stock.Quantity < quantity)
        {
            throw new InventoryException(
                ApiText.T("在庫が不足しています（ロット '{0}'、現在数量 {1}、要求 {2}）。", lot.LotNumber, stock?.Quantity ?? 0, quantity));
        }
        stock.Quantity -= quantity;
        stock.ConcurrencyStamp = Guid.NewGuid().ToString("N");

        AddTransaction(type, lot, quantity, userId,
            fromLocationId: locationId, workOrderId: workOrderId,
            pickingOrderId: pickingOrderId, shippingOrderId: shippingOrderId, note: note);
        return stock;
    }

    /// <summary>在庫移動（移動・搬送。1トランザクションで移動元→先を記録）</summary>
    public async Task MoveAsync(
        Lot lot, int fromLocationId, int toLocationId, decimal quantity,
        InventoryTransactionType type, string? userId, string? note = null, CancellationToken ct = default)
    {
        var from = await FindStockAsync(lot.Id, fromLocationId, ct);
        if (from is null || from.Quantity < quantity)
        {
            throw new InventoryException(
                ApiText.T("移動元の在庫が不足しています（ロット '{0}'、現在数量 {1}、要求 {2}）。", lot.LotNumber, from?.Quantity ?? 0, quantity));
        }
        from.Quantity -= quantity;
        from.ConcurrencyStamp = Guid.NewGuid().ToString("N");

        var to = await FindStockAsync(lot.Id, toLocationId, ct);
        if (to is null)
        {
            to = new InventoryStock
            {
                ProductId = lot.ProductId,
                LotId = lot.Id,
                LocationId = toLocationId,
                Quantity = 0,
            };
            db.InventoryStocks.Add(to);
        }
        to.Quantity += quantity;
        to.ConcurrencyStamp = Guid.NewGuid().ToString("N");

        AddTransaction(type, lot, quantity, userId,
            fromLocationId: fromLocationId, toLocationId: toLocationId, note: note);
    }

    /// <summary>
    /// 先入れ先出し（有効期限優先＝FEFO、次に古いロット順）での引当（D-20-10-02）。
    /// 引当対象は LotUsabilityPolicy が「使える」と判定した在庫のみ（正常ステータスかつ期限内）。
    /// 不足分があればInventoryException。
    /// </summary>
    public async Task<List<(Lot Lot, int LocationId, decimal Quantity)>> AllocateFefoAsync(
        int productId, decimal quantity, CancellationToken ct = default)
    {
        var stocks = await db.InventoryStocks
            .Include(s => s.Lot)
            .Where(s => s.ProductId == productId && s.Quantity > 0)
            .Where(LotUsabilityPolicy.UsableStock(businessDate.Today))
            // SQLiteはDateTimeOffsetの並べ替え不可のため、古いロット順はId昇順（採番順）で代用する
            .OrderBy(s => s.Lot!.ExpiresOn == null).ThenBy(s => s.Lot!.ExpiresOn)
            .ThenBy(s => s.LotId).ThenBy(s => s.Id)
            .ToListAsync(ct);

        var result = new List<(Lot, int, decimal)>();
        var remaining = quantity;
        foreach (var stock in stocks)
        {
            if (remaining <= 0)
            {
                break;
            }
            var take = Math.Min(stock.Quantity, remaining);
            result.Add((stock.Lot!, stock.LocationId, take));
            remaining -= take;
        }
        if (remaining > 0)
        {
            var productCode = await db.Products.Where(p => p.Id == productId)
                .Select(p => p.Code).FirstOrDefaultAsync(ct);
            throw new InventoryException(
                ApiText.T("品目 '{0}' の利用可能在庫が不足しています（不足数量 {1}）。", productCode, remaining));
        }
        return result;
    }

    private Task<InventoryStock?> FindStockAsync(int lotId, int locationId, CancellationToken ct)
    {
        // 同一リクエスト内で複数回操作するケース（バックフラッシュ等）のためLocal優先で検索
        var local = db.InventoryStocks.Local
            .FirstOrDefault(s => s.LotId == lotId && s.LocationId == locationId);
        return local is not null
            ? Task.FromResult<InventoryStock?>(local)
            : db.InventoryStocks.FirstOrDefaultAsync(s => s.LotId == lotId && s.LocationId == locationId, ct);
    }

    private void AddTransaction(
        InventoryTransactionType type, Lot lot, decimal quantity, string? userId,
        int? fromLocationId = null, int? toLocationId = null, int? workOrderId = null,
        int? pickingOrderId = null, int? shippingOrderId = null, string? note = null)
    {
        db.InventoryTransactions.Add(new InventoryTransaction
        {
            Type = type,
            ProductId = lot.ProductId,
            LotId = lot.Id,
            Quantity = quantity,
            FromLocationId = fromLocationId,
            ToLocationId = toLocationId,
            WorkOrderId = workOrderId,
            PickingOrderId = pickingOrderId,
            ShippingOrderId = shippingOrderId,
            PerformedByUserId = userId,
            Note = note,
        });
    }
}
