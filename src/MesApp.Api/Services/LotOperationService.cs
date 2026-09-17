using MesApp.Core.Abstractions;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>ロット操作の結果（Errorがあれば未反映。IsConflictはロット番号の重複）</summary>
public sealed record LotOperationOutcome(Lot? Lot, Product? Product, string? Error, bool IsConflict = false)
{
    public static LotOperationOutcome Ok(Lot lot, Product? product) => new(lot, product, null);
    public static LotOperationOutcome Invalid(string error) => new(null, null, error);
    public static LotOperationOutcome Conflict(string error) => new(null, null, error, IsConflict: true);
}

/// <summary>
/// ロットの分割・統合・振替（D-10-30-05〜07）。数量の移し替えとロット系譜（H-30-10）の記録をまとめる。
/// 保存・監査ログまで行い、ロットの生成を伴う分割・振替はここでトランザクションを張る。
/// </summary>
public sealed class LotOperationService(
    MesAppDbContext db,
    InventoryService inventory,
    NumberingService numbering,
    IAuditLogger auditLogger)
{
    /// <summary>ロット分割（新ロットは親ロットの系譜・期限を引き継ぐ）</summary>
    public async Task<LotOperationOutcome> SplitAsync(
        int lotId, int locationId, decimal quantity, string? newLotNumber, string? userId, CancellationToken ct)
    {
        var lot = await db.Lots.Include(l => l.Product).FirstOrDefaultAsync(l => l.Id == lotId, ct);
        if (lot is null)
        {
            return LotOperationOutcome.Invalid("存在しないロットIDです。");
        }

        return await DeriveAsync(lot, lot.Product!, locationId, quantity, newLotNumber,
            InventoryTransactionType.Split, LotRelationType.Split, "分割", userId,
            (newLot) => auditLogger.LogAsync("Inventory", "Split", nameof(Lot), lot.Id.ToString(),
                detail: new { from = lot.LotNumber, to = newLot.LotNumber, quantity }, ct: ct),
            ct);
    }

    /// <summary>ロット統合（同一品目・同一ロケーションの在庫を統合先ロットへ移す）</summary>
    public async Task<LotOperationOutcome> MergeAsync(
        int sourceLotId, int targetLotId, int locationId, string? userId, CancellationToken ct)
    {
        var source = await db.Lots.FirstOrDefaultAsync(l => l.Id == sourceLotId, ct);
        var target = await db.Lots.FirstOrDefaultAsync(l => l.Id == targetLotId, ct);
        if (source is null || target is null)
        {
            return LotOperationOutcome.Invalid("存在しないロットIDです。");
        }
        if (source.Id == target.Id)
        {
            return LotOperationOutcome.Invalid("統合元と統合先が同一ロットです。");
        }
        if (source.ProductId != target.ProductId)
        {
            return LotOperationOutcome.Invalid("品目が異なるロットは統合できません。");
        }

        var stock = await db.InventoryStocks.FirstOrDefaultAsync(
            s => s.LotId == source.Id && s.LocationId == locationId, ct);
        if (stock is null || stock.Quantity <= 0)
        {
            return LotOperationOutcome.Invalid("統合元の在庫がありません。");
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
            return LotOperationOutcome.Invalid(ex.Message);
        }
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "Merge", nameof(Lot), target.Id.ToString(),
            detail: new { from = source.LotNumber, to = target.LotNumber, quantity }, ct: ct);
        return LotOperationOutcome.Ok(target, null);
    }

    /// <summary>品目振替・ロット振替（新しいロットを生成して数量を移す）</summary>
    public async Task<LotOperationOutcome> TransferAsync(
        int lotId, int locationId, decimal quantity, int? newProductId, string? newLotNumber,
        string? userId, CancellationToken ct)
    {
        if (newProductId is null && string.IsNullOrWhiteSpace(newLotNumber))
        {
            return LotOperationOutcome.Invalid("新品目ID（品目振替）または新ロット番号（ロット振替）を指定してください。");
        }
        var lot = await db.Lots.Include(l => l.Product).FirstOrDefaultAsync(l => l.Id == lotId, ct);
        if (lot is null)
        {
            return LotOperationOutcome.Invalid("存在しないロットIDです。");
        }

        var newProduct = lot.Product!;
        if (newProductId is int productId && productId != lot.ProductId)
        {
            var found = await db.Products.FirstOrDefaultAsync(p => p.Id == productId && p.IsActive, ct);
            if (found is null)
            {
                return LotOperationOutcome.Invalid("存在しない（または無効な）振替先品目IDです。");
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
    private async Task<LotOperationOutcome> DeriveAsync(
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
            return LotOperationOutcome.Conflict($"ロット番号 '{newLotNumber}' は既に存在します。");
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
            return LotOperationOutcome.Invalid(ex.Message);
        }
        await db.SaveChangesAsync(ct);
        await audit(newLot);
        await transaction.CommitAsync(ct);
        return LotOperationOutcome.Ok(newLot, newProduct);
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
