using System.Security.Claims;
using MesApp.Api.Services;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Inventory;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 在庫オペレーション（D-10-30、D-30-10、D-40-40 共通）：照会・移動・調整・ステータス変更・
/// 分割/統合・振替・廃棄・返品・払出戻し・期限管理。参照は認証済み全員、更新は在庫管理ロール。
/// </summary>
[ApiController]
[Route("api/inventory")]
[Authorize]
public class InventoryController(
    MesAppDbContext db,
    InventoryService inventory,
    NumberingService numbering,
    IBusinessDateService businessDate,
    IAuditLogger auditLogger) : ControllerBase
{
    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // ---- 照会（D-10-30-01：品目別・ロケーション別・ロット別）----

    [HttpGet("stocks")]
    public async Task<ActionResult<List<StockResponse>>> Stocks(
        [FromQuery] int? productId = null,
        [FromQuery] int? locationId = null,
        [FromQuery] int? lotId = null,
        [FromQuery] bool includeEmpty = false,
        CancellationToken ct = default)
    {
        var query = db.InventoryStocks.AsNoTracking().AsQueryable();
        if (productId is not null)
        {
            query = query.Where(s => s.ProductId == productId);
        }
        if (locationId is not null)
        {
            query = query.Where(s => s.LocationId == locationId);
        }
        if (lotId is not null)
        {
            query = query.Where(s => s.LotId == lotId);
        }
        if (!includeEmpty)
        {
            query = query.Where(s => s.Quantity > 0);
        }
        return await query
            .OrderBy(s => s.Product!.Code).ThenBy(s => s.Lot!.LotNumber).ThenBy(s => s.Location!.Code)
            .Select(s => new StockResponse(
                s.Id, s.ProductId, s.Product!.Code, s.Product!.Name,
                s.LotId, s.Lot!.LotNumber, s.Lot!.StockStatus, s.Lot!.ExpiresOn,
                s.LocationId, s.Location!.Code, s.Quantity))
            .ToListAsync(ct);
    }

    /// <summary>滞留在庫の期限管理・アラート（D-10-30-09。有効期限が指定日数以内または超過の在庫）</summary>
    [HttpGet("expiring")]
    public async Task<ActionResult<List<StockResponse>>> Expiring(
        [FromQuery] int withinDays = 30, CancellationToken ct = default)
    {
        var threshold = businessDate.Today.AddDays(withinDays);
        return await db.InventoryStocks.AsNoTracking()
            .Where(s => s.Quantity > 0 && s.Lot!.ExpiresOn != null && s.Lot!.ExpiresOn <= threshold)
            .OrderBy(s => s.Lot!.ExpiresOn)
            .Select(s => new StockResponse(
                s.Id, s.ProductId, s.Product!.Code, s.Product!.Name,
                s.LotId, s.Lot!.LotNumber, s.Lot!.StockStatus, s.Lot!.ExpiresOn,
                s.LocationId, s.Location!.Code, s.Quantity))
            .ToListAsync(ct);
    }

    [HttpGet("transactions")]
    public async Task<ActionResult<List<TransactionResponse>>> Transactions(
        [FromQuery] int? lotId = null,
        [FromQuery] int? productId = null,
        [FromQuery] int? workOrderId = null,
        CancellationToken ct = default)
    {
        var query = db.InventoryTransactions.AsNoTracking().AsQueryable();
        if (lotId is not null)
        {
            query = query.Where(t => t.LotId == lotId);
        }
        if (productId is not null)
        {
            query = query.Where(t => t.ProductId == productId);
        }
        if (workOrderId is not null)
        {
            query = query.Where(t => t.WorkOrderId == workOrderId);
        }
        return await query.OrderByDescending(t => t.Id).Take(500)
            .Select(t => new TransactionResponse(
                t.Id, t.Type, t.ProductId, t.Product!.Code,
                t.LotId, t.Lot!.LotNumber, t.Quantity,
                t.FromLocationId, t.FromLocation!.Code, t.ToLocationId, t.ToLocation!.Code,
                t.WorkOrderId, t.Timestamp, t.Note))
            .ToListAsync(ct);
    }

    [HttpGet("lots/{id:int}")]
    public async Task<ActionResult<LotResponse>> GetLot(int id, CancellationToken ct)
    {
        var lot = await db.Lots.AsNoTracking()
            .Where(l => l.Id == id)
            .Select(l => new LotResponse(
                l.Id, l.LotNumber, l.ProductId, l.Product!.Code, l.Product!.Name,
                l.InitialQuantity, l.OriginType, l.StockStatus,
                l.ManufacturedOn, l.ExpiresOn, l.Grade, l.ParentLotId))
            .FirstOrDefaultAsync(ct);
        return lot is null ? NotFound() : lot;
    }

    // ---- 更新系（在庫管理ロール）----

    /// <summary>在庫移動（D-10-30-02）</summary>
    [HttpPost("move")]
    [Authorize(Roles = RoleGroups.InventoryManage)]
    public async Task<IActionResult> Move(MoveRequest request, CancellationToken ct)
    {
        var lot = await db.Lots.FirstOrDefaultAsync(l => l.Id == request.LotId, ct);
        if (lot is null)
        {
            return BadRequest(new ProblemDetails { Title = "存在しないロットIDです。" });
        }
        if (!await db.Locations.AnyAsync(l => l.Id == request.ToLocationId && l.IsActive, ct))
        {
            return BadRequest(new ProblemDetails { Title = "存在しない（または無効な）移動先ロケーションです。" });
        }
        try
        {
            await inventory.MoveAsync(lot, request.FromLocationId, request.ToLocationId, request.Quantity,
                InventoryTransactionType.Move, CurrentUserId, ct: ct);
        }
        catch (InventoryException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message });
        }
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "Move", nameof(Lot), lot.Id.ToString(),
            detail: $"lot={lot.LotNumber}, qty={request.Quantity}", ct: ct);
        return NoContent();
    }

    /// <summary>数量調整（実在庫との差異訂正 D-10-30-04。理由必須・監査ログ記録）</summary>
    [HttpPost("adjust")]
    [Authorize(Roles = RoleGroups.InventoryManage)]
    public async Task<IActionResult> Adjust(AdjustRequest request, CancellationToken ct)
    {
        var lot = await db.Lots.FirstOrDefaultAsync(l => l.Id == request.LotId, ct);
        if (lot is null)
        {
            return BadRequest(new ProblemDetails { Title = "存在しないロットIDです。" });
        }
        var stock = await db.InventoryStocks.FirstOrDefaultAsync(
            s => s.LotId == request.LotId && s.LocationId == request.LocationId, ct);
        var current = stock?.Quantity ?? 0;
        var delta = request.NewQuantity - current;
        if (delta == 0)
        {
            return BadRequest(new ProblemDetails { Title = "現在数量と同じため調整は不要です。" });
        }

        try
        {
            if (delta > 0)
            {
                await inventory.AddAsync(lot, request.LocationId, delta,
                    InventoryTransactionType.Adjust, CurrentUserId, note: request.Reason, ct: ct);
            }
            else
            {
                await inventory.RemoveAsync(lot, request.LocationId, -delta,
                    InventoryTransactionType.Adjust, CurrentUserId, note: request.Reason, ct: ct);
            }
        }
        catch (InventoryException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message });
        }
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "Adjust", nameof(Lot), lot.Id.ToString(),
            detail: $"lot={lot.LotNumber}, {current} -> {request.NewQuantity}, reason={request.Reason}", ct: ct);
        return NoContent();
    }

    /// <summary>在庫ステータス変更（保留・検査待ち・不良・廃棄予定等。D-10-30-08。ロット単位）</summary>
    [HttpPost("status")]
    [Authorize(Roles = RoleGroups.InventoryManage)]
    public async Task<IActionResult> ChangeStatus(LotStatusRequest request, CancellationToken ct)
    {
        var lot = await db.Lots.FirstOrDefaultAsync(l => l.Id == request.LotId, ct);
        if (lot is null)
        {
            return BadRequest(new ProblemDetails { Title = "存在しないロットIDです。" });
        }
        var before = lot.StockStatus;
        lot.StockStatus = request.Status;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "StatusChange", nameof(Lot), lot.Id.ToString(),
            detail: $"lot={lot.LotNumber}, {before} -> {request.Status}, reason={request.Reason}", ct: ct);
        return NoContent();
    }

    /// <summary>ロット分割（D-10-30-05。新ロットは親ロットの系譜・期限を引き継ぐ）</summary>
    [HttpPost("split")]
    [Authorize(Roles = RoleGroups.InventoryManage)]
    public async Task<ActionResult<LotResponse>> Split(SplitRequest request, CancellationToken ct)
    {
        var lot = await db.Lots.Include(l => l.Product).FirstOrDefaultAsync(l => l.Id == request.LotId, ct);
        if (lot is null)
        {
            return BadRequest(new ProblemDetails { Title = "存在しないロットIDです。" });
        }

        var newLotNumber = request.NewLotNumber;
        if (string.IsNullOrWhiteSpace(newLotNumber))
        {
            newLotNumber = await numbering.NextLotNumberAsync(lot.Product!.Code, ct);
        }
        else if (await db.Lots.AnyAsync(l => l.LotNumber == newLotNumber, ct))
        {
            return Conflict(new ProblemDetails { Title = $"ロット番号 '{newLotNumber}' は既に存在します。" });
        }

        var newLot = new Lot
        {
            LotNumber = newLotNumber,
            ProductId = lot.ProductId,
            InitialQuantity = request.Quantity,
            OriginType = lot.OriginType,
            ManufacturedOn = lot.ManufacturedOn,
            ExpiresOn = lot.ExpiresOn,
            StockStatus = lot.StockStatus,
            Grade = lot.Grade,
            ParentLotId = lot.Id,
        };
        db.Lots.Add(newLot);

        try
        {
            await inventory.RemoveAsync(lot, request.LocationId, request.Quantity,
                InventoryTransactionType.Split, CurrentUserId, note: $"分割 -> {newLotNumber}", ct: ct);
            await db.SaveChangesAsync(ct); // newLot.Id確定＋在庫減算の確定
            await inventory.AddAsync(newLot, request.LocationId, request.Quantity,
                InventoryTransactionType.Split, CurrentUserId, note: $"分割元 {lot.LotNumber}", ct: ct);
        }
        catch (InventoryException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message });
        }
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "Split", nameof(Lot), lot.Id.ToString(),
            detail: $"{lot.LotNumber} -> {newLotNumber}, qty={request.Quantity}", ct: ct);

        return new LotResponse(newLot.Id, newLot.LotNumber, lot.ProductId, lot.Product!.Code, lot.Product!.Name,
            newLot.InitialQuantity, newLot.OriginType, newLot.StockStatus,
            newLot.ManufacturedOn, newLot.ExpiresOn, newLot.Grade, newLot.ParentLotId);
    }

    /// <summary>ロット統合（D-10-30-05。同一品目・同一ロケーションの在庫を統合先ロットへ移す）</summary>
    [HttpPost("merge")]
    [Authorize(Roles = RoleGroups.InventoryManage)]
    public async Task<IActionResult> Merge(MergeRequest request, CancellationToken ct)
    {
        var source = await db.Lots.FirstOrDefaultAsync(l => l.Id == request.SourceLotId, ct);
        var target = await db.Lots.FirstOrDefaultAsync(l => l.Id == request.TargetLotId, ct);
        if (source is null || target is null)
        {
            return BadRequest(new ProblemDetails { Title = "存在しないロットIDです。" });
        }
        if (source.Id == target.Id)
        {
            return BadRequest(new ProblemDetails { Title = "統合元と統合先が同一ロットです。" });
        }
        if (source.ProductId != target.ProductId)
        {
            return BadRequest(new ProblemDetails { Title = "品目が異なるロットは統合できません。" });
        }

        var stock = await db.InventoryStocks.FirstOrDefaultAsync(
            s => s.LotId == source.Id && s.LocationId == request.LocationId, ct);
        if (stock is null || stock.Quantity <= 0)
        {
            return BadRequest(new ProblemDetails { Title = "統合元の在庫がありません。" });
        }
        var quantity = stock.Quantity;

        try
        {
            await inventory.RemoveAsync(source, request.LocationId, quantity,
                InventoryTransactionType.Merge, CurrentUserId, note: $"統合 -> {target.LotNumber}", ct: ct);
            await inventory.AddAsync(target, request.LocationId, quantity,
                InventoryTransactionType.Merge, CurrentUserId, note: $"統合元 {source.LotNumber}", ct: ct);
        }
        catch (InventoryException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message });
        }
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "Merge", nameof(Lot), target.Id.ToString(),
            detail: $"{source.LotNumber} -> {target.LotNumber}, qty={quantity}", ct: ct);
        return NoContent();
    }

    /// <summary>品目振替・ロット振替（D-10-30-06〜07。新しいロットを生成して数量を移す）</summary>
    [HttpPost("transfer")]
    [Authorize(Roles = RoleGroups.InventoryManage)]
    public async Task<ActionResult<LotResponse>> Transfer(LotTransferRequest request, CancellationToken ct)
    {
        if (request.NewProductId is null && string.IsNullOrWhiteSpace(request.NewLotNumber))
        {
            return BadRequest(new ProblemDetails { Title = "新品目ID（品目振替）または新ロット番号（ロット振替）を指定してください。" });
        }
        var lot = await db.Lots.Include(l => l.Product).FirstOrDefaultAsync(l => l.Id == request.LotId, ct);
        if (lot is null)
        {
            return BadRequest(new ProblemDetails { Title = "存在しないロットIDです。" });
        }

        var newProduct = lot.Product!;
        if (request.NewProductId is int newProductId && newProductId != lot.ProductId)
        {
            var found = await db.Products.FirstOrDefaultAsync(p => p.Id == newProductId && p.IsActive, ct);
            if (found is null)
            {
                return BadRequest(new ProblemDetails { Title = "存在しない（または無効な）振替先品目IDです。" });
            }
            newProduct = found;
        }

        var newLotNumber = request.NewLotNumber;
        if (string.IsNullOrWhiteSpace(newLotNumber))
        {
            newLotNumber = await numbering.NextLotNumberAsync(newProduct.Code, ct);
        }
        else if (await db.Lots.AnyAsync(l => l.LotNumber == newLotNumber, ct))
        {
            return Conflict(new ProblemDetails { Title = $"ロット番号 '{newLotNumber}' は既に存在します。" });
        }

        var newLot = new Lot
        {
            LotNumber = newLotNumber,
            ProductId = newProduct.Id,
            InitialQuantity = request.Quantity,
            OriginType = lot.OriginType,
            ManufacturedOn = lot.ManufacturedOn,
            ExpiresOn = lot.ExpiresOn,
            StockStatus = lot.StockStatus,
            Grade = lot.Grade,
            ParentLotId = lot.Id,
        };
        db.Lots.Add(newLot);

        try
        {
            await inventory.RemoveAsync(lot, request.LocationId, request.Quantity,
                InventoryTransactionType.Transfer, CurrentUserId, note: $"振替 -> {newLotNumber}", ct: ct);
            await db.SaveChangesAsync(ct);
            await inventory.AddAsync(newLot, request.LocationId, request.Quantity,
                InventoryTransactionType.Transfer, CurrentUserId, note: $"振替元 {lot.LotNumber}", ct: ct);
        }
        catch (InventoryException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message });
        }
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "LotTransfer", nameof(Lot), lot.Id.ToString(),
            detail: $"{lot.LotNumber}({lot.Product!.Code}) -> {newLotNumber}({newProduct.Code}), qty={request.Quantity}", ct: ct);

        return new LotResponse(newLot.Id, newLot.LotNumber, newProduct.Id, newProduct.Code, newProduct.Name,
            newLot.InitialQuantity, newLot.OriginType, newLot.StockStatus,
            newLot.ManufacturedOn, newLot.ExpiresOn, newLot.Grade, newLot.ParentLotId);
    }

    /// <summary>在庫廃棄（D-50-30-01）</summary>
    [HttpPost("discard")]
    [Authorize(Roles = RoleGroups.InventoryManage)]
    public async Task<IActionResult> Discard(DiscardRequest request, CancellationToken ct)
    {
        return await RemoveSimpleAsync(request.LotId, request.LocationId, request.Quantity,
            InventoryTransactionType.Discard, "Discard", request.Reason, ct);
    }

    /// <summary>返品（D-10-10-05。サプライヤーへの返品による在庫引落し）</summary>
    [HttpPost("return")]
    [Authorize(Roles = RoleGroups.InventoryManage)]
    public async Task<IActionResult> Return(ReturnRequest request, CancellationToken ct)
    {
        return await RemoveSimpleAsync(request.LotId, request.LocationId, request.Quantity,
            InventoryTransactionType.Return, "Return", request.Reason, ct);
    }

    /// <summary>払出戻し（D-20-20-03。工程に払い出した部材の在庫戻し）</summary>
    [HttpPost("issue-return")]
    [Authorize(Roles = RoleGroups.InventoryManage)]
    public async Task<IActionResult> IssueReturn(IssueReturnRequest request, CancellationToken ct)
    {
        var lot = await db.Lots.FirstOrDefaultAsync(l => l.Id == request.LotId, ct);
        if (lot is null)
        {
            return BadRequest(new ProblemDetails { Title = "存在しないロットIDです。" });
        }
        if (!await db.Locations.AnyAsync(l => l.Id == request.LocationId && l.IsActive, ct))
        {
            return BadRequest(new ProblemDetails { Title = "存在しない（または無効な）ロケーションIDです。" });
        }
        await inventory.AddAsync(lot, request.LocationId, request.Quantity,
            InventoryTransactionType.IssueReturn, CurrentUserId,
            workOrderId: request.WorkOrderId, ct: ct);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "IssueReturn", nameof(Lot), lot.Id.ToString(),
            detail: $"lot={lot.LotNumber}, qty={request.Quantity}", ct: ct);
        return NoContent();
    }

    private async Task<IActionResult> RemoveSimpleAsync(
        int lotId, int locationId, decimal quantity,
        InventoryTransactionType type, string auditAction, string? reason, CancellationToken ct)
    {
        var lot = await db.Lots.FirstOrDefaultAsync(l => l.Id == lotId, ct);
        if (lot is null)
        {
            return BadRequest(new ProblemDetails { Title = "存在しないロットIDです。" });
        }
        try
        {
            await inventory.RemoveAsync(lot, locationId, quantity, type, CurrentUserId, note: reason, ct: ct);
        }
        catch (InventoryException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message });
        }
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", auditAction, nameof(Lot), lot.Id.ToString(),
            detail: $"lot={lot.LotNumber}, qty={quantity}, reason={reason}", ct: ct);
        return NoContent();
    }
}
