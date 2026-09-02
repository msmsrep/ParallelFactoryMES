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
/// 出庫・ピッキング指示（D-20-10 出庫指示・ピッキング指示、D-20-20 ピッキング実行・払出）。
/// 明細のロット・ロケーションは作成時に先入れ先出し（有効期限優先）で自動引当する（D-20-10-02）。
/// </summary>
[ApiController]
[Route("api/picking-orders")]
[Authorize(Roles = RoleGroups.InventoryManage)]
public class PickingOrdersController(
    MesAppDbContext db,
    InventoryService inventory,
    NumberingService numbering,
    IAuditLogger auditLogger) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<PagedResult<PickingOrderResponse>>> List(
        [FromQuery] PageQuery paging,
        [FromQuery] PickingOrderStatus? status = null,
        [FromQuery] int? workOrderId = null,
        CancellationToken ct = default)
    {
        var query = BaseQuery();
        if (status is not null)
        {
            query = query.Where(p => p.Status == status);
        }
        if (workOrderId is not null)
        {
            query = query.Where(p => p.WorkOrderId == workOrderId);
        }
        var orders = await query.OrderByDescending(p => p.Id).ToPagedResultAsync(paging, ct);
        return orders.Map(ToResponse);
    }

    [HttpGet("{id:int}")]
    [Authorize]
    public async Task<ActionResult<PickingOrderResponse>> Get(int id, CancellationToken ct)
    {
        var order = await BaseQuery().FirstOrDefaultAsync(p => p.Id == id, ct);
        return order is null ? NotFound() : ToResponse(order);
    }

    /// <summary>ピッキング指示の作成（払出先＝作業指示または出荷指示。FEFOで自動引当）</summary>
    [HttpPost]
    public async Task<ActionResult<PickingOrderResponse>> Create(
        PickingOrderCreateRequest request, CancellationToken ct)
    {
        if (request.Lines.Count == 0)
        {
            return BadRequest(new ProblemDetails { Title = "明細がありません。" });
        }
        if (request.Type == PickingOrderType.ProcessIssue)
        {
            if (request.WorkOrderId is null
                || !await db.WorkOrders.AnyAsync(w => w.Id == request.WorkOrderId, ct))
            {
                return BadRequest(new ProblemDetails { Title = "工程払出には有効な作業指示ID（workOrderId）が必要です。" });
            }
        }
        else if (request.ShippingOrderId is null
                 || !await db.ShippingOrders.AnyAsync(s => s.Id == request.ShippingOrderId, ct))
        {
            return BadRequest(new ProblemDetails { Title = "出荷ピッキングには有効な出荷指示ID（shippingOrderId）が必要です。" });
        }

        var order = new PickingOrder
        {
            OrderNo = await numbering.NextPickingNoAsync(ct),
            Type = request.Type,
            WorkOrderId = request.Type == PickingOrderType.ProcessIssue ? request.WorkOrderId : null,
            ShippingOrderId = request.Type == PickingOrderType.Shipping ? request.ShippingOrderId : null,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
        };

        try
        {
            foreach (var line in request.Lines)
            {
                var allocations = await inventory.AllocateFefoAsync(line.ProductId, line.Quantity, ct);
                order.Lines.AddRange(allocations.Select(a => new PickingLine
                {
                    ProductId = line.ProductId,
                    LotId = a.Lot.Id,
                    LocationId = a.LocationId,
                    Quantity = a.Quantity,
                }));
            }
        }
        catch (InventoryException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message });
        }

        db.PickingOrders.Add(order);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "PickingCreate", nameof(PickingOrder), order.Id.ToString(),
            detail: $"orderNo={order.OrderNo}, type={order.Type}", ct: ct);
        var saved = await BaseQuery().FirstAsync(p => p.Id == order.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, ToResponse(saved));
    }

    /// <summary>ピッキング実行・払出（D-20-20-01〜02。在庫を引き落として完了にする）</summary>
    [HttpPost("{id:int}/execute")]
    public async Task<ActionResult<PickingOrderResponse>> Execute(int id, CancellationToken ct)
    {
        var order = await db.PickingOrders
            .Include(p => p.Lines).ThenInclude(l => l.Lot)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (order is null)
        {
            return NotFound();
        }
        if (order.Status != PickingOrderStatus.Instructed)
        {
            return Conflict(new ProblemDetails { Title = $"状態 '{order.Status}' のピッキング指示は実行できません。" });
        }

        var type = order.Type == PickingOrderType.ProcessIssue
            ? InventoryTransactionType.ProcessIssue
            : InventoryTransactionType.Issue;
        try
        {
            foreach (var line in order.Lines)
            {
                await inventory.RemoveAsync(line.Lot!, line.LocationId, line.Quantity, type,
                    User.FindFirstValue(ClaimTypes.NameIdentifier),
                    workOrderId: order.WorkOrderId, pickingOrderId: order.Id,
                    shippingOrderId: order.ShippingOrderId,
                    note: $"ピッキング {order.OrderNo}", ct: ct);
            }
        }
        catch (InventoryException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message });
        }

        order.Status = PickingOrderStatus.Completed;
        order.ExecutedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        order.ExecutedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "PickingExecute", nameof(PickingOrder), id.ToString(),
            detail: $"orderNo={order.OrderNo}", ct: ct);
        var saved = await BaseQuery().FirstAsync(p => p.Id == id, ct);
        return ToResponse(saved);
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<PickingOrderResponse>> Cancel(int id, CancellationToken ct)
    {
        var order = await db.PickingOrders.FindAsync([id], ct);
        if (order is null)
        {
            return NotFound();
        }
        if (order.Status != PickingOrderStatus.Instructed)
        {
            return Conflict(new ProblemDetails { Title = $"状態 '{order.Status}' のピッキング指示は取消できません。" });
        }
        order.Status = PickingOrderStatus.Canceled;
        await db.SaveChangesAsync(ct);
        var saved = await BaseQuery().FirstAsync(p => p.Id == id, ct);
        return ToResponse(saved);
    }

    private IQueryable<PickingOrder> BaseQuery() =>
        db.PickingOrders.AsNoTracking()
            .Include(p => p.WorkOrder)
            .Include(p => p.ShippingOrder)
            .Include(p => p.Lines).ThenInclude(l => l.Product)
            .Include(p => p.Lines).ThenInclude(l => l.Lot)
            .Include(p => p.Lines).ThenInclude(l => l.Location);

    private static PickingOrderResponse ToResponse(PickingOrder p) =>
        new(p.Id, p.OrderNo, p.Type, p.Status,
            p.WorkOrderId, p.WorkOrder?.WorkOrderNo, p.ShippingOrderId, p.ShippingOrder?.ShippingNo,
            p.CreatedAt, p.ExecutedAt,
            p.Lines.Select(l => new PickingLineResponse(
                l.Id, l.ProductId, l.Product!.Code, l.Product!.Name,
                l.LotId, l.Lot!.LotNumber, l.LocationId, l.Location!.Code, l.Quantity)).ToList());
}
