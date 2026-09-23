using MesApp.Core.Localization;
using MesApp.Api.Localization;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Inventory;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// 棚卸（D-50-10）：指示作成（理論在庫のスナップショット）→実棚数登録→確定（差異調整）。
/// <para>保存と監査ログまで行う。状態は Controller で代入せず本サービス経由で変更する。</para>
/// </summary>
public sealed class StocktakeService(
    MesAppDbContext db,
    InventoryService inventory,
    NumberingService numbering,
    IAuditLogger auditLogger)
{
    /// <summary>
    /// 棚卸指示の作成（D-50-10-01。現在庫（数量&gt;0）のスナップショットを明細化）。
    /// stocktakeNo を渡すとその番号で登録する（CSV取込で後続の行から棚卸を指すため。空なら自動採番）
    /// </summary>
    public async Task<Outcome<Stocktake>> CreateAsync(
        StocktakeCreateRequest request, string? stocktakeNo, string? userId, CancellationToken ct)
    {
        if (request.TargetLocationId is int locationId
            && !await db.Locations.AnyAsync(l => l.Id == locationId, ct))
        {
            return Outcome<Stocktake>.Invalid(ApiText.T("存在しないロケーションIDです。"));
        }

        var stocksQuery = db.InventoryStocks.AsNoTracking().Where(s => s.Quantity > 0);
        if (request.TargetLocationId is not null)
        {
            stocksQuery = stocksQuery.Where(s => s.LocationId == request.TargetLocationId);
        }
        var stocks = await stocksQuery.ToListAsync(ct);
        if (stocks.Count == 0)
        {
            return Outcome<Stocktake>.Invalid(ApiText.T("対象在庫がありません。"));
        }

        if (string.IsNullOrWhiteSpace(stocktakeNo))
        {
            stocktakeNo = await numbering.NextStocktakeNoAsync(ct);
        }
        else if (NumberingService.IsAutoNumberFormat(stocktakeNo, NumberingService.StocktakeNoPrefix))
        {
            return Outcome<Stocktake>.Invalid(
                ApiText.T("棚卸番号 '{0}' は自動採番の形式（{1}〜）と重なるため指定できません。", stocktakeNo, NumberingService.StocktakeNoPrefix));
        }
        else if (await db.Stocktakes.AnyAsync(x => x.StocktakeNo == stocktakeNo, ct))
        {
            return Outcome<Stocktake>.Conflict(ApiText.T("棚卸番号 '{0}' は既に存在します。", stocktakeNo));
        }

        var stocktake = new Stocktake
        {
            StocktakeNo = stocktakeNo,
            TargetLocationId = request.TargetLocationId,
            CreatedByUserId = userId,
            Lines = stocks.Select(s => new StocktakeLine
            {
                ProductId = s.ProductId,
                LotId = s.LotId,
                LocationId = s.LocationId,
                TheoreticalQuantity = s.Quantity,
            }).ToList(),
        };
        db.Stocktakes.Add(stocktake);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "StocktakeCreate", nameof(Stocktake), stocktake.Id.ToString(),
            detail: $"stocktakeNo={stocktake.StocktakeNo}, lines={stocktake.Lines.Count}", ct: ct);
        return Outcome<Stocktake>.Ok(stocktake);
    }

    /// <summary>実棚数の登録（D-50-10-02。部分登録可・上書き可）</summary>
    public async Task<Outcome<Stocktake>> RegisterCountsAsync(
        int id, StocktakeCountRequest request, CancellationToken ct)
    {
        var stocktake = await db.Stocktakes.Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (stocktake is null)
        {
            return Outcome<Stocktake>.NotFound(ApiText.T("存在しない棚卸IDです。"));
        }
        if (stocktake.Status != StocktakeStatus.Instructed)
        {
            return Outcome<Stocktake>.Conflict(ApiText.T("状態 '{0}' の棚卸には登録できません。", EnumLabels.Of(stocktake.Status)));
        }

        var lineById = stocktake.Lines.ToDictionary(l => l.Id);
        // 実棚数は差異調整（＝在庫の増減）の根拠になるので、上書き前の値も残す（Spec.md 7.6）
        var changes = new List<object>();
        foreach (var count in request.Counts)
        {
            if (!lineById.TryGetValue(count.LineId, out var line))
            {
                return Outcome<Stocktake>.Invalid(ApiText.T("存在しない明細ID {0} が含まれています。", count.LineId));
            }
            changes.Add(new
            {
                lineId = line.Id,
                theoretical = line.TheoreticalQuantity,
                before = line.CountedQuantity,
                after = count.CountedQuantity,
            });
            line.CountedQuantity = count.CountedQuantity;
        }
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "StocktakeCount", nameof(Stocktake), id.ToString(),
            detail: new { stocktakeNo = stocktake.StocktakeNo, counts = changes }, ct: ct);
        return Outcome<Stocktake>.Ok(stocktake);
    }

    /// <summary>
    /// 棚卸確定（D-50-10-05）。実棚入力済みの明細について現在庫との差異を棚卸調整で反映する（D-50-10-04）。
    /// </summary>
    public async Task<Outcome<Stocktake>> FinalizeAsync(int id, string? userId, CancellationToken ct)
    {
        var stocktake = await db.Stocktakes
            .Include(s => s.Lines).ThenInclude(l => l.Lot)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (stocktake is null)
        {
            return Outcome<Stocktake>.NotFound(ApiText.T("存在しない棚卸IDです。"));
        }
        if (stocktake.Status != StocktakeStatus.Instructed)
        {
            return Outcome<Stocktake>.Conflict(ApiText.T("状態 '{0}' の棚卸は確定できません。", EnumLabels.Of(stocktake.Status)));
        }
        if (stocktake.Lines.All(l => l.CountedQuantity is null))
        {
            return Outcome<Stocktake>.Invalid(ApiText.T("実棚数が1件も登録されていません。"));
        }

        foreach (var line in stocktake.Lines.Where(l => l.CountedQuantity is not null))
        {
            // 差異は確定時点の現在庫と実棚の差で調整する（棚卸中の在庫変動があっても実棚に合わせる）
            var stock = await db.InventoryStocks.FirstOrDefaultAsync(
                s => s.LotId == line.LotId && s.LocationId == line.LocationId, ct);
            var current = stock?.Quantity ?? 0;
            var delta = line.CountedQuantity!.Value - current;
            if (delta == 0)
            {
                continue;
            }
            if (delta > 0)
            {
                await inventory.AddAsync(line.Lot!, line.LocationId, delta,
                    InventoryTransactionType.StocktakeAdjust, userId,
                    note: $"棚卸 {stocktake.StocktakeNo}", ct: ct);
            }
            else
            {
                await inventory.RemoveAsync(line.Lot!, line.LocationId, -delta,
                    InventoryTransactionType.StocktakeAdjust, userId,
                    note: $"棚卸 {stocktake.StocktakeNo}", ct: ct);
            }
            line.IsAdjusted = true;
        }

        stocktake.Status = StocktakeStatus.Finalized;
        stocktake.FinalizedAt = DateTimeOffset.UtcNow;
        stocktake.FinalizedByUserId = userId;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "StocktakeFinalize", nameof(Stocktake), id.ToString(),
            detail: $"stocktakeNo={stocktake.StocktakeNo}, " +
                    $"adjusted={stocktake.Lines.Count(l => l.IsAdjusted)}", ct: ct);
        return Outcome<Stocktake>.Ok(stocktake);
    }

    /// <summary>棚卸の取消（確定前のみ）</summary>
    public async Task<Outcome<Stocktake>> CancelAsync(int id, CancellationToken ct)
    {
        var stocktake = await db.Stocktakes.FindAsync([id], ct);
        if (stocktake is null)
        {
            return Outcome<Stocktake>.NotFound(ApiText.T("存在しない棚卸IDです。"));
        }
        if (stocktake.Status != StocktakeStatus.Instructed)
        {
            return Outcome<Stocktake>.Conflict(ApiText.T("状態 '{0}' の棚卸は取消できません。", EnumLabels.Of(stocktake.Status)));
        }
        var before = stocktake.Status;
        stocktake.Status = StocktakeStatus.Canceled;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "StocktakeCancel", nameof(Stocktake), id.ToString(),
            detail: new { stocktakeNo = stocktake.StocktakeNo, before, after = stocktake.Status }, ct: ct);
        return Outcome<Stocktake>.Ok(stocktake);
    }
}
