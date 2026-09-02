using System.Security.Claims;
using MesApp.Api.Policies;
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
/// 出荷（D-40-20 出荷指示、D-40-30 出荷実行・完了報告）。
/// 出荷実行はロット・ロケーション指定で在庫を引き落とす。部分出荷可能で、
/// 全明細が出荷済みになると完了になる。出荷には承認済みの出荷判定（H-10-10）が必要で、
/// 併せてロットの使用可否（Spec.md 3.9。LotUsabilityPolicy）を満たす必要がある。
/// 参照系は認証済みユーザー全員に開放し、更新系のみ在庫権限に絞る（Spec.md 7.4）。
/// <b>クラスへ <c>[Authorize(Roles = ...)]</c> を付けてはならない</b>：認可属性はクラスとアクションで
/// 合成されるため、アクション側の <c>[Authorize]</c> では開放できず、参照系まで在庫ロール限定になる。
/// </summary>
[ApiController]
[Route("api/shipping-orders")]
[Authorize]
public class ShippingOrdersController(
    MesAppDbContext db,
    InventoryService inventory,
    NumberingService numbering,
    IBusinessDateService businessDate,
    IAuditLogger auditLogger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ShippingOrderResponse>>> List(
        [FromQuery] PageQuery paging,
        [FromQuery] ShippingOrderStatus? status = null, CancellationToken ct = default)
    {
        var query = BaseQuery();
        if (status is not null)
        {
            query = query.Where(s => s.Status == status);
        }
        var orders = await query.OrderByDescending(s => s.Id).ToPagedResultAsync(paging, ct);
        return orders.Map(ToResponse);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ShippingOrderResponse>> Get(int id, CancellationToken ct)
    {
        var order = await BaseQuery().FirstOrDefaultAsync(s => s.Id == id, ct);
        return order is null ? NotFound() : ToResponse(order);
    }

    /// <summary>出荷指示の作成（D-40-20-01）</summary>
    [HttpPost]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<ActionResult<ShippingOrderResponse>> Create(
        ShippingOrderCreateRequest request, CancellationToken ct)
    {
        if (request.Lines.Count == 0)
        {
            return BadRequest(new ProblemDetails { Title = "明細がありません。" });
        }
        var productIds = request.Lines.Select(l => l.ProductId).Distinct().ToList();
        if (await db.Products.CountAsync(p => productIds.Contains(p.Id), ct) != productIds.Count)
        {
            return BadRequest(new ProblemDetails { Title = "存在しない品目IDが含まれています。" });
        }

        var order = new ShippingOrder
        {
            ShippingNo = await numbering.NextShippingNoAsync(ct),
            Destination = request.Destination,
            PlannedDate = request.PlannedDate,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            Lines = request.Lines
                .Select(l => new ShippingLine { ProductId = l.ProductId, Quantity = l.Quantity })
                .ToList(),
        };
        db.ShippingOrders.Add(order);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "ShippingCreate", nameof(ShippingOrder), order.Id.ToString(),
            detail: $"shippingNo={order.ShippingNo}, dest={order.Destination}", ct: ct);
        var saved = await BaseQuery().FirstAsync(s => s.Id == order.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, ToResponse(saved));
    }

    /// <summary>
    /// 出荷実行（D-40-30。指定ロットの在庫を引き落とし、明細の出荷済数量を更新。
    /// 部分出荷可。全明細が満たされると完了（D-40-30-05））
    /// </summary>
    [HttpPost("{id:int}/ship")]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<ActionResult<ShippingOrderResponse>> Ship(
        int id, ShipExecuteRequest request, CancellationToken ct)
    {
        var order = await db.ShippingOrders.Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (order is null)
        {
            return NotFound();
        }
        if (order.Status != ShippingOrderStatus.Instructed)
        {
            return Conflict(new ProblemDetails { Title = $"状態 '{order.Status}' の出荷指示は実行できません。" });
        }
        if (request.Lines.Count == 0)
        {
            return BadRequest(new ProblemDetails { Title = "出荷明細がありません。" });
        }

        // 出荷判定ゲート（H-10-10、Spec.md 5.3 出荷判定参照）：
        // この出荷指示を対象とする承認済みの「可」または「特採」判定が必要
        var hasApprovedJudgment = await db.ShipmentJudgments
            .AnyAsync(ShipmentGatePolicy.ValidJudgment(id), ct);
        if (ShipmentGatePolicy.CheckJudgment(hasApprovedJudgment) is string judgmentReason)
        {
            return Conflict(new ProblemDetails { Title = judgmentReason });
        }

        var lotIds = request.Lines.Select(l => l.LotId).Distinct().ToList();
        var lots = await db.Lots.Where(l => lotIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);

        // ロットの品目単位で出荷指示明細との突合を行う
        var shipTotals = new Dictionary<int, decimal>(); // productId -> qty
        var today = businessDate.Today;
        foreach (var line in request.Lines)
        {
            if (!lots.TryGetValue(line.LotId, out var lot))
            {
                return BadRequest(new ProblemDetails { Title = "存在しないロットIDが含まれています。" });
            }
            // 出荷判定の承認後に保留・不良になったロットを出荷させない（判定書の存在だけでは不十分）。
            // 特採は不適合承認時にステータスが正常へ戻るため、ここでは正常のみを許可すればよい
            if (LotUsabilityPolicy.CheckShippable(lot, today) is string reason)
            {
                return Conflict(new ProblemDetails { Title = reason });
            }
            shipTotals[lot.ProductId] = shipTotals.GetValueOrDefault(lot.ProductId) + line.Quantity;
        }
        foreach (var (productId, qty) in shipTotals)
        {
            var orderLine = order.Lines.FirstOrDefault(l => l.ProductId == productId);
            if (orderLine is null)
            {
                return BadRequest(new ProblemDetails { Title = "出荷指示に含まれない品目のロットが指定されています。" });
            }
            if (orderLine.ShippedQuantity + qty > orderLine.Quantity)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = $"出荷数量が指示数量を超えています（指示 {orderLine.Quantity}、出荷済 {orderLine.ShippedQuantity}、今回 {qty}）。",
                });
            }
        }

        try
        {
            foreach (var line in request.Lines)
            {
                await inventory.RemoveAsync(lots[line.LotId], line.LocationId, line.Quantity,
                    InventoryTransactionType.Ship, User.FindFirstValue(ClaimTypes.NameIdentifier),
                    shippingOrderId: order.Id, note: $"出荷 {order.ShippingNo}", ct: ct);
            }
        }
        catch (InventoryException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message });
        }

        foreach (var (productId, qty) in shipTotals)
        {
            order.Lines.First(l => l.ProductId == productId).ShippedQuantity += qty;
        }
        if (order.Lines.All(l => l.ShippedQuantity >= l.Quantity))
        {
            order.Status = ShippingOrderStatus.Completed;
            order.ShippedAt = DateTimeOffset.UtcNow;
            order.ShippedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        }
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "Ship", nameof(ShippingOrder), id.ToString(),
            detail: $"shippingNo={order.ShippingNo}, completed={order.Status == ShippingOrderStatus.Completed}", ct: ct);
        var saved = await BaseQuery().FirstAsync(s => s.Id == id, ct);
        return ToResponse(saved);
    }

    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<ActionResult<ShippingOrderResponse>> Cancel(int id, CancellationToken ct)
    {
        var order = await db.ShippingOrders.FindAsync([id], ct);
        if (order is null)
        {
            return NotFound();
        }
        if (order.Status != ShippingOrderStatus.Instructed)
        {
            return Conflict(new ProblemDetails { Title = $"状態 '{order.Status}' の出荷指示は取消できません。" });
        }
        order.Status = ShippingOrderStatus.Canceled;
        await db.SaveChangesAsync(ct);
        var saved = await BaseQuery().FirstAsync(s => s.Id == id, ct);
        return ToResponse(saved);
    }

    /// <summary>
    /// 出荷指示選択用の選択肢（出荷判定・出荷向けピッキングの対象指定）。
    /// 出荷番号・出荷先の部分一致で絞り込む。
    /// </summary>
    [HttpGet("options")]
    public async Task<ActionResult<OptionsResult<ShippingOrderResponse>>> Options(
        [FromQuery] OptionQuery options,
        [FromQuery] ShippingOrderStatus? status = null,
        CancellationToken ct = default)
    {
        var query = BaseQuery();
        if (status is not null)
        {
            query = query.Where(s => s.Status == status);
        }
        if (options.Keyword is { } keyword)
        {
            query = query.Where(s => s.ShippingNo.Contains(keyword) || s.Destination.Contains(keyword));
        }
        var result = await query.OrderByDescending(s => s.Id).ToOptionsResultAsync(options, ct);
        return new OptionsResult<ShippingOrderResponse>([.. result.Items.Select(ToResponse)], result.Truncated);
    }

    private IQueryable<ShippingOrder> BaseQuery() =>
        db.ShippingOrders.AsNoTracking()
            .Include(s => s.Lines).ThenInclude(l => l.Product);

    private static ShippingOrderResponse ToResponse(ShippingOrder s) =>
        new(s.Id, s.ShippingNo, s.Destination, s.PlannedDate, s.Status, s.CreatedAt, s.ShippedAt,
            s.Lines.Select(l => new ShippingLineResponse(
                l.Id, l.ProductId, l.Product!.Code, l.Product!.Name, l.Quantity, l.ShippedQuantity)).ToList());
}
