using System.Security.Claims;
using MesApp.Api.Services;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Execution;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 搬送・移動指示（B-50-10-01〜02 半製品の搬送指示と移動実行、D-30-10-04 工程間在庫搬送）。
/// <para>
/// 実行は <c>InventoryService.MoveAsync</c> で実在庫を動かすため、更新系は
/// <c>InventoryController</c> の在庫操作と同じ在庫権限（<see cref="RoleGroups.InventoryManage"/>）で揃える。
/// 参照は認証済みユーザー全員に開放する。
/// </para>
/// </summary>
[ApiController]
[Route("api/transfer-orders")]
[Authorize]
public class TransferOrdersController(
    MesAppDbContext db,
    InventoryService inventory,
    IAuditLogger auditLogger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<TransferOrderResponse>>> List(
        [FromQuery] TransferOrderStatus? status = null, CancellationToken ct = default)
    {
        var query = db.TransferOrders.AsNoTracking().AsQueryable();
        if (status is not null)
        {
            query = query.Where(t => t.Status == status);
        }
        return await query.OrderByDescending(t => t.Id)
            .Select(Projection)
            .ToListAsync(ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TransferOrderResponse>> Get(int id, CancellationToken ct)
    {
        var order = await db.TransferOrders.AsNoTracking()
            .Where(t => t.Id == id).Select(Projection).FirstOrDefaultAsync(ct);
        return order is null ? NotFound() : order;
    }

    /// <summary>搬送指示の作成（B-50-10-01）</summary>
    [HttpPost]
    [Authorize(Roles = RoleGroups.InventoryManage)]
    public async Task<ActionResult<TransferOrderResponse>> Create(TransferOrderRequest request, CancellationToken ct)
    {
        if (!await db.Lots.AnyAsync(l => l.Id == request.LotId, ct))
        {
            return BadRequest(new ProblemDetails { Title = "存在しないロットIDです。" });
        }
        if (request.FromLocationId == request.ToLocationId)
        {
            return BadRequest(new ProblemDetails { Title = "移動元と移動先が同一です。" });
        }
        var locationIds = new[] { request.FromLocationId, request.ToLocationId };
        if (await db.Locations.CountAsync(l => locationIds.Contains(l.Id) && l.IsActive, ct) != 2)
        {
            return BadRequest(new ProblemDetails { Title = "存在しない（または無効な）ロケーションが含まれています。" });
        }

        var order = new TransferOrder
        {
            LotId = request.LotId,
            Quantity = request.Quantity,
            FromLocationId = request.FromLocationId,
            ToLocationId = request.ToLocationId,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
        };
        db.TransferOrders.Add(order);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, await GetResponseAsync(order.Id, ct));
    }

    /// <summary>移動実行（B-50-10-02。在庫を移動して完了にする）</summary>
    [HttpPost("{id:int}/execute")]
    [Authorize(Roles = RoleGroups.InventoryManage)]
    public async Task<ActionResult<TransferOrderResponse>> Execute(int id, CancellationToken ct)
    {
        var order = await db.TransferOrders.Include(t => t.Lot).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (order is null)
        {
            return NotFound();
        }
        if (order.Status != TransferOrderStatus.Instructed)
        {
            return Conflict(new ProblemDetails { Title = $"状態 '{order.Status}' の搬送指示は実行できません。" });
        }

        try
        {
            await inventory.MoveAsync(order.Lot!, order.FromLocationId, order.ToLocationId, order.Quantity,
                InventoryTransactionType.Move, User.FindFirstValue(ClaimTypes.NameIdentifier),
                note: $"搬送指示 #{order.Id}", ct: ct);
        }
        catch (InventoryException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message });
        }

        order.Status = TransferOrderStatus.Completed;
        order.ExecutedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        order.ExecutedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "Transfer", nameof(TransferOrder), id.ToString(), ct: ct);
        return await GetResponseAsync(id, ct);
    }

    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = RoleGroups.InventoryManage)]
    public async Task<ActionResult<TransferOrderResponse>> Cancel(int id, CancellationToken ct)
    {
        var order = await db.TransferOrders.FindAsync([id], ct);
        if (order is null)
        {
            return NotFound();
        }
        if (order.Status != TransferOrderStatus.Instructed)
        {
            return Conflict(new ProblemDetails { Title = $"状態 '{order.Status}' の搬送指示は取消できません。" });
        }
        order.Status = TransferOrderStatus.Canceled;
        await db.SaveChangesAsync(ct);
        return await GetResponseAsync(id, ct);
    }

    private async Task<TransferOrderResponse> GetResponseAsync(int id, CancellationToken ct) =>
        await db.TransferOrders.AsNoTracking()
            .Where(t => t.Id == id).Select(Projection).FirstAsync(ct);

    private static readonly System.Linq.Expressions.Expression<Func<TransferOrder, TransferOrderResponse>> Projection =
        t => new TransferOrderResponse(t.Id, t.LotId, t.Lot!.LotNumber, t.Lot!.Product!.Code, t.Quantity,
            t.FromLocationId, t.FromLocation!.Code, t.ToLocationId, t.ToLocation!.Code,
            t.Status, t.CreatedAt, t.ExecutedAt);
}
