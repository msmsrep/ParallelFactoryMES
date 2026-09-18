using MesApp.Core.Abstractions;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// ロットに対する在庫操作（D-10-30）：移動・調整・ステータス変更・分割・統合・振替・廃棄・返品・払出戻し。
/// 数量の移し替えとロット系譜（H-30-10）の記録をまとめる。
/// 保存・監査ログまで行い、ロットの生成を伴う分割・振替はここでトランザクションを張る。
/// </summary>
public sealed class LotOperationService(
    MesAppDbContext db,
    InventoryService inventory,
    LotStatusService lotStatus,
    NumberingService numbering,
    IAuditLogger auditLogger)
{
    /// <summary>在庫移動（D-10-30-02）</summary>
    public async Task<Outcome<Lot>> MoveAsync(
        int lotId, int fromLocationId, int toLocationId, decimal quantity, string? userId, CancellationToken ct)
    {
        var lot = await db.Lots.FirstOrDefaultAsync(l => l.Id == lotId, ct);
        if (lot is null)
        {
            return Outcome<Lot>.Invalid("存在しないロットIDです。");
        }
        if (!await db.Locations.AnyAsync(l => l.Id == toLocationId && l.IsActive, ct))
        {
            return Outcome<Lot>.Invalid("存在しない（または無効な）移動先ロケーションです。");
        }
        try
        {
            await inventory.MoveAsync(lot, fromLocationId, toLocationId, quantity,
                InventoryTransactionType.Move, userId, ct: ct);
        }
        catch (InventoryException ex)
        {
            return Outcome<Lot>.Invalid(ex.Message);
        }
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "Move", nameof(Lot), lot.Id.ToString(),
            detail: $"lot={lot.LotNumber}, qty={quantity}", ct: ct);
        return Outcome<Lot>.Ok(lot);
    }

    /// <summary>数量調整（実在庫との差異訂正 D-10-30-04。理由必須・変更前後を監査ログに残す）</summary>
    public async Task<Outcome<Lot>> AdjustAsync(
        int lotId, int locationId, decimal newQuantity, string? reason, string? userId, CancellationToken ct)
    {
        var lot = await db.Lots.FirstOrDefaultAsync(l => l.Id == lotId, ct);
        if (lot is null)
        {
            return Outcome<Lot>.Invalid("存在しないロットIDです。");
        }
        var stock = await db.InventoryStocks.FirstOrDefaultAsync(
            s => s.LotId == lotId && s.LocationId == locationId, ct);
        var current = stock?.Quantity ?? 0;
        var delta = newQuantity - current;
        if (delta == 0)
        {
            return Outcome<Lot>.Invalid("現在数量と同じため調整は不要です。");
        }

        try
        {
            if (delta > 0)
            {
                await inventory.AddAsync(lot, locationId, delta,
                    InventoryTransactionType.Adjust, userId, note: reason, ct: ct);
            }
            else
            {
                await inventory.RemoveAsync(lot, locationId, -delta,
                    InventoryTransactionType.Adjust, userId, note: reason, ct: ct);
            }
        }
        catch (InventoryException ex)
        {
            return Outcome<Lot>.Invalid(ex.Message);
        }
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "Adjust", nameof(Lot), lot.Id.ToString(),
            detail: new
            {
                lot = lot.LotNumber,
                locationId,
                before = current,
                after = newQuantity,
                reason,
            }, ct: ct);
        return Outcome<Lot>.Ok(lot);
    }

    /// <summary>在庫ステータス変更（保留・検査待ち・不良・廃棄予定等。D-10-30-08。ロット単位）</summary>
    public async Task<Outcome<Lot>> ChangeStatusAsync(
        int lotId, LotStockStatus status, string? reason, string? userId, CancellationToken ct)
    {
        var lot = await db.Lots.FirstOrDefaultAsync(l => l.Id == lotId, ct);
        if (lot is null)
        {
            return Outcome<Lot>.Invalid("存在しないロットIDです。");
        }
        var before = lot.StockStatus;
        lotStatus.ChangeStatus(lot, status, LotStatusChangeSource.Manual, reason, userId);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "StatusChange", nameof(Lot), lot.Id.ToString(),
            detail: new
            {
                lot = lot.LotNumber,
                before = before.ToString(),
                after = status.ToString(),
                reason,
            }, ct: ct);
        return Outcome<Lot>.Ok(lot);
    }

    /// <summary>在庫廃棄（D-50-30-01）</summary>
    public Task<Outcome<Lot>> DiscardAsync(
        int lotId, int locationId, decimal quantity, string? reason, string? userId, CancellationToken ct) =>
        RemoveSimpleAsync(lotId, locationId, quantity,
            InventoryTransactionType.Discard, "Discard", reason, userId, ct);

    /// <summary>返品（D-10-10-05。サプライヤーへの返品による在庫引落し）</summary>
    public Task<Outcome<Lot>> ReturnAsync(
        int lotId, int locationId, decimal quantity, string? reason, string? userId, CancellationToken ct) =>
        RemoveSimpleAsync(lotId, locationId, quantity,
            InventoryTransactionType.Return, "Return", reason, userId, ct);

    /// <summary>払出戻し（D-20-20-03。工程に払い出した部材の在庫戻し）</summary>
    public async Task<Outcome<Lot>> IssueReturnAsync(
        int lotId, int locationId, decimal quantity, int? workOrderId, string? userId, CancellationToken ct)
    {
        var lot = await db.Lots.FirstOrDefaultAsync(l => l.Id == lotId, ct);
        if (lot is null)
        {
            return Outcome<Lot>.Invalid("存在しないロットIDです。");
        }
        if (!await db.Locations.AnyAsync(l => l.Id == locationId && l.IsActive, ct))
        {
            return Outcome<Lot>.Invalid("存在しない（または無効な）ロケーションIDです。");
        }
        await inventory.AddAsync(lot, locationId, quantity,
            InventoryTransactionType.IssueReturn, userId, workOrderId: workOrderId, ct: ct);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "IssueReturn", nameof(Lot), lot.Id.ToString(),
            detail: $"lot={lot.LotNumber}, qty={quantity}", ct: ct);
        return Outcome<Lot>.Ok(lot);
    }

    /// <summary>ロケーションから数量を引き落とすだけの操作（廃棄・返品で共通）</summary>
    private async Task<Outcome<Lot>> RemoveSimpleAsync(
        int lotId, int locationId, decimal quantity,
        InventoryTransactionType type, string auditAction, string? reason,
        string? userId, CancellationToken ct)
    {
        var lot = await db.Lots.FirstOrDefaultAsync(l => l.Id == lotId, ct);
        if (lot is null)
        {
            return Outcome<Lot>.Invalid("存在しないロットIDです。");
        }
        try
        {
            await inventory.RemoveAsync(lot, locationId, quantity, type, userId, note: reason, ct: ct);
        }
        catch (InventoryException ex)
        {
            return Outcome<Lot>.Invalid(ex.Message);
        }
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", auditAction, nameof(Lot), lot.Id.ToString(),
            detail: $"lot={lot.LotNumber}, qty={quantity}, reason={reason}", ct: ct);
        return Outcome<Lot>.Ok(lot);
    }

    /// <summary>ロット分割（新ロットは親ロットの系譜・期限を引き継ぐ）</summary>
    public async Task<Outcome<Lot>> SplitAsync(
        int lotId, int locationId, decimal quantity, string? newLotNumber, string? userId, CancellationToken ct)
    {
        var lot = await db.Lots.Include(l => l.Product).FirstOrDefaultAsync(l => l.Id == lotId, ct);
        if (lot is null)
        {
            return Outcome<Lot>.Invalid("存在しないロットIDです。");
        }

        return await DeriveAsync(lot, lot.Product!, locationId, quantity, newLotNumber,
            InventoryTransactionType.Split, LotRelationType.Split, "分割", userId,
            (newLot) => auditLogger.LogAsync("Inventory", "Split", nameof(Lot), lot.Id.ToString(),
                detail: new { from = lot.LotNumber, to = newLot.LotNumber, quantity }, ct: ct),
            ct);
    }

    /// <summary>ロット統合（同一品目・同一ロケーションの在庫を統合先ロットへ移す）</summary>
    public async Task<Outcome<Lot>> MergeAsync(
        int sourceLotId, int targetLotId, int locationId, string? userId, CancellationToken ct)
    {
        var source = await db.Lots.FirstOrDefaultAsync(l => l.Id == sourceLotId, ct);
        var target = await db.Lots.FirstOrDefaultAsync(l => l.Id == targetLotId, ct);
        if (source is null || target is null)
        {
            return Outcome<Lot>.Invalid("存在しないロットIDです。");
        }
        if (source.Id == target.Id)
        {
            return Outcome<Lot>.Invalid("統合元と統合先が同一ロットです。");
        }
        if (source.ProductId != target.ProductId)
        {
            return Outcome<Lot>.Invalid("品目が異なるロットは統合できません。");
        }

        var stock = await db.InventoryStocks.FirstOrDefaultAsync(
            s => s.LotId == source.Id && s.LocationId == locationId, ct);
        if (stock is null || stock.Quantity <= 0)
        {
            return Outcome<Lot>.Invalid("統合元の在庫がありません。");
        }
        var quantity = stock.Quantity;

        try
        {
            await inventory.RemoveAsync(source, locationId, quantity,
                InventoryTransactionType.Merge, userId, note: $"統合 -> {target.LotNumber}", ct: ct);
            await inventory.AddAsync(target, locationId, quantity,
                InventoryTransactionType.Merge, userId, note: $"統合元 {source.LotNumber}", ct: ct);
            // 統合先ロットは親を複数持ちうるため、系譜はLot.ParentLotIdではなくLotGenealogyへ残す
            AddGenealogy(source.Id, target.Id, LotRelationType.Merge, quantity, userId);
        }
        catch (InventoryException ex)
        {
            return Outcome<Lot>.Invalid(ex.Message);
        }
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "Merge", nameof(Lot), target.Id.ToString(),
            detail: new { from = source.LotNumber, to = target.LotNumber, quantity }, ct: ct);
        return Outcome<Lot>.Ok(target);
    }

    /// <summary>品目振替・ロット振替（新しいロットを生成して数量を移す）</summary>
    public async Task<Outcome<Lot>> TransferAsync(
        int lotId, int locationId, decimal quantity, int? newProductId, string? newLotNumber,
        string? userId, CancellationToken ct)
    {
        if (newProductId is null && string.IsNullOrWhiteSpace(newLotNumber))
        {
            return Outcome<Lot>.Invalid("新品目ID（品目振替）または新ロット番号（ロット振替）を指定してください。");
        }
        var lot = await db.Lots.Include(l => l.Product).FirstOrDefaultAsync(l => l.Id == lotId, ct);
        if (lot is null)
        {
            return Outcome<Lot>.Invalid("存在しないロットIDです。");
        }

        var newProduct = lot.Product!;
        if (newProductId is int productId && productId != lot.ProductId)
        {
            var found = await db.Products.FirstOrDefaultAsync(p => p.Id == productId && p.IsActive, ct);
            if (found is null)
            {
                return Outcome<Lot>.Invalid("存在しない（または無効な）振替先品目IDです。");
            }
            newProduct = found;
        }

        return await DeriveAsync(lot, newProduct, locationId, quantity, newLotNumber,
            InventoryTransactionType.Transfer, LotRelationType.Transfer, "振替", userId,
            (newLot) => auditLogger.LogAsync("Inventory", "LotTransfer", nameof(Lot), lot.Id.ToString(),
                detail: new
                {
                    from = new { lot = lot.LotNumber, product = lot.Product!.Code },
                    to = new { lot = newLot.LotNumber, product = newProduct.Code },
                    quantity,
                }, ct: ct),
            ct);
    }

    /// <summary>
    /// 親ロットから新ロットを生成して数量を移す（分割・振替で共通）。
    /// 新ロットは親ロットの由来・製造日・期限・ステータス・グレードを引き継ぐ
    /// </summary>
    private async Task<Outcome<Lot>> DeriveAsync(
        Lot lot, Product newProduct, int locationId, decimal quantity, string? newLotNumber,
        InventoryTransactionType transactionType, LotRelationType relationType, string label,
        string? userId, Func<Lot, Task> audit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(newLotNumber))
        {
            newLotNumber = await numbering.NextLotNumberAsync(newProduct.Code, ct);
        }
        else if (await db.Lots.AnyAsync(l => l.LotNumber == newLotNumber, ct))
        {
            return Outcome<Lot>.Conflict($"ロット番号 '{newLotNumber}' は既に存在します。");
        }

        var newLot = new Lot
        {
            LotNumber = newLotNumber,
            ProductId = newProduct.Id,
            InitialQuantity = quantity,
            OriginType = lot.OriginType,
            ManufacturedOn = lot.ManufacturedOn,
            ExpiresOn = lot.ExpiresOn,
            StockStatus = lot.StockStatus,
            Grade = lot.Grade,
            ParentLotId = lot.Id,
            Product = newProduct,
        };
        db.Lots.Add(newLot);

        // newLot.Idの確定に一度SaveChangesが要るため保存が2回に分かれる。
        // 途中で失敗すると元ロットから減った在庫が新ロットに入らず消えるので、トランザクションでまとめる
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await inventory.RemoveAsync(lot, locationId, quantity,
                transactionType, userId, note: $"{label} -> {newLotNumber}", ct: ct);
            await db.SaveChangesAsync(ct); // newLot.Id確定＋在庫減算の確定
            await inventory.AddAsync(newLot, locationId, quantity,
                transactionType, userId, note: $"{label}元 {lot.LotNumber}", ct: ct);
            AddGenealogy(lot.Id, newLot.Id, relationType, quantity, userId);
        }
        catch (InventoryException ex)
        {
            return Outcome<Lot>.Invalid(ex.Message);
        }
        await db.SaveChangesAsync(ct);
        await audit(newLot);
        await transaction.CommitAsync(ct);
        return Outcome<Lot>.Ok(newLot);
    }

    /// <summary>
    /// ロット系譜の記録（分割・統合・振替）。トレーサビリティ（H-30-10）は
    /// Lot.ParentLotIdではなくこの関係を辿るため、由来が生じる操作では必ず残す
    /// </summary>
    private void AddGenealogy(
        int parentLotId, int childLotId, LotRelationType relationType, decimal quantity, string? userId) =>
        db.LotGenealogies.Add(new LotGenealogy
        {
            ParentLotId = parentLotId,
            ChildLotId = childLotId,
            RelationType = relationType,
            Quantity = quantity,
            PerformedByUserId = userId,
        });
}
