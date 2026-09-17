using System.Security.Claims;
using MesApp.Api.Services;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Common;
using MesApp.Core.Contracts.Inventory;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 棚卸（D-50-10）：指示作成（理論在庫のスナップショット）→実棚数登録→差異一覧→確定（差異調整）
/// 参照系は認証済みユーザー全員に開放し、更新系のみ在庫権限に絞る（Spec.md 7.4）。
/// <b>クラスへ <c>[Authorize(Roles = ...)]</c> を付けてはならない</b>：認可属性はクラスとアクションで
/// 合成されるため、アクション側の <c>[Authorize]</c> では開放できず、参照系まで在庫ロール限定になる。
/// </summary>
[ApiController]
[Route("api/stocktakes")]
[Authorize]
public class StocktakesController(
    MesAppDbContext db,
    InventoryService inventory,
    NumberingService numbering,
    IAuditLogger auditLogger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<StocktakeResponse>>> List(
        [FromQuery] PageQuery paging, CancellationToken ct = default)
    {
        var stocktakes = await BaseQuery().OrderByDescending(s => s.Id).ToPagedResultAsync(paging, ct);
        return stocktakes.Map(ToResponse);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<StocktakeResponse>> Get(int id, CancellationToken ct)
    {
        var stocktake = await BaseQuery().FirstOrDefaultAsync(s => s.Id == id, ct);
        return stocktake is null ? NotFound() : ToResponse(stocktake);
    }

    /// <summary>棚卸指示の作成（D-50-10-01。現在庫（数量>0）のスナップショットを明細化）</summary>
    [HttpPost]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<ActionResult<StocktakeResponse>> Create(StocktakeCreateRequest request, CancellationToken ct)
    {
        if (request.TargetLocationId is int locationId
            && !await db.Locations.AnyAsync(l => l.Id == locationId, ct))
        {
            return this.BadRequestProblem("存在しないロケーションIDです。");
        }

        var stocksQuery = db.InventoryStocks.AsNoTracking().Where(s => s.Quantity > 0);
        if (request.TargetLocationId is not null)
        {
            stocksQuery = stocksQuery.Where(s => s.LocationId == request.TargetLocationId);
        }
        var stocks = await stocksQuery.ToListAsync(ct);
        if (stocks.Count == 0)
        {
            return this.BadRequestProblem("対象在庫がありません。");
        }

        var stocktake = new Stocktake
        {
            StocktakeNo = await numbering.NextStocktakeNoAsync(ct),
            TargetLocationId = request.TargetLocationId,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
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
        var saved = await BaseQuery().FirstAsync(s => s.Id == stocktake.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = stocktake.Id }, ToResponse(saved));
    }

    /// <summary>実棚数の登録（D-50-10-02。部分登録可・上書き可）</summary>
    [HttpPut("{id:int}/counts")]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<ActionResult<StocktakeResponse>> RegisterCounts(
        int id, StocktakeCountRequest request, CancellationToken ct)
    {
        var stocktake = await db.Stocktakes.Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (stocktake is null)
        {
            return NotFound();
        }
        if (stocktake.Status != StocktakeStatus.Instructed)
        {
            return this.ConflictProblem($"状態 '{stocktake.Status}' の棚卸には登録できません。");
        }

        var lineById = stocktake.Lines.ToDictionary(l => l.Id);
        // 実棚数は差異調整（＝在庫の増減）の根拠になるので、上書き前の値も残す（Spec.md 7.6）
        var changes = new List<object>();
        foreach (var count in request.Counts)
        {
            if (!lineById.TryGetValue(count.LineId, out var line))
            {
                return this.BadRequestProblem($"存在しない明細ID {count.LineId} が含まれています。");
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
        var saved = await BaseQuery().FirstAsync(s => s.Id == id, ct);
        return ToResponse(saved);
    }

    /// <summary>
    /// 棚卸確定（D-50-10-05）。実棚入力済みの明細について現在庫との差異を棚卸調整で反映する（D-50-10-04）。
    /// </summary>
    [HttpPost("{id:int}/finalize")]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<ActionResult<StocktakeResponse>> Finalize(int id, CancellationToken ct)
    {
        var stocktake = await db.Stocktakes
            .Include(s => s.Lines).ThenInclude(l => l.Lot)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (stocktake is null)
        {
            return NotFound();
        }
        if (stocktake.Status != StocktakeStatus.Instructed)
        {
            return this.ConflictProblem($"状態 '{stocktake.Status}' の棚卸は確定できません。");
        }
        if (stocktake.Lines.All(l => l.CountedQuantity is null))
        {
            return this.BadRequestProblem("実棚数が1件も登録されていません。");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
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
        var saved = await BaseQuery().FirstAsync(s => s.Id == id, ct);
        return ToResponse(saved);
    }

    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<ActionResult<StocktakeResponse>> Cancel(int id, CancellationToken ct)
    {
        var stocktake = await db.Stocktakes.FindAsync([id], ct);
        if (stocktake is null)
        {
            return NotFound();
        }
        if (stocktake.Status != StocktakeStatus.Instructed)
        {
            return this.ConflictProblem($"状態 '{stocktake.Status}' の棚卸は取消できません。");
        }
        var before = stocktake.Status;
        stocktake.Status = StocktakeStatus.Canceled;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "StocktakeCancel", nameof(Stocktake), id.ToString(),
            detail: new { stocktakeNo = stocktake.StocktakeNo, before, after = stocktake.Status }, ct: ct);
        var saved = await BaseQuery().FirstAsync(s => s.Id == id, ct);
        return ToResponse(saved);
    }

    private IQueryable<Stocktake> BaseQuery() =>
        db.Stocktakes.AsNoTracking()
            .Include(s => s.Lines).ThenInclude(l => l.Product)
            .Include(s => s.Lines).ThenInclude(l => l.Lot)
            .Include(s => s.Lines).ThenInclude(l => l.Location);

    private static StocktakeResponse ToResponse(Stocktake s) =>
        new(s.Id, s.StocktakeNo, s.TargetLocationId, s.Status, s.CreatedAt, s.FinalizedAt,
            s.Lines.OrderBy(l => l.Product!.Code).ThenBy(l => l.Lot!.LotNumber)
                .Select(l => new StocktakeLineResponse(
                    l.Id, l.ProductId, l.Product!.Code, l.Product!.Name,
                    l.LotId, l.Lot!.LotNumber, l.LocationId, l.Location!.Code,
                    l.TheoreticalQuantity, l.CountedQuantity,
                    l.CountedQuantity - l.TheoreticalQuantity, l.IsAdjusted))
                .ToList());
}
