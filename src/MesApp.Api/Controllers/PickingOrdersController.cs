using System.Security.Claims;
using MesApp.Api.Services;
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
/// 参照系は認証済みユーザー全員に開放し、更新系のみ在庫権限に絞る（Spec.md 7.4）。
/// <b>クラスへ <c>[Authorize(Roles = ...)]</c> を付けてはならない</b>：認可属性はクラスとアクションで
/// 合成されるため、アクション側の <c>[Authorize]</c> では開放できず、参照系まで在庫ロール限定になる。
/// </summary>
[ApiController]
[Route("api/picking-orders")]
[Authorize]
public class PickingOrdersController(
    MesAppDbContext db,
    PickingService picking) : ControllerBase
{
    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet]
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
    public async Task<ActionResult<PickingOrderResponse>> Get(int id, CancellationToken ct)
    {
        var order = await BaseQuery().FirstOrDefaultAsync(p => p.Id == id, ct);
        return order is null ? NotFound() : ToResponse(order);
    }

    /// <summary>ピッキング指示の作成（払出先＝作業指示または出荷指示。FEFOで自動引当）</summary>
    [HttpPost]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<ActionResult<PickingOrderResponse>> Create(
        PickingOrderCreateRequest request, CancellationToken ct)
    {
        var outcome = await picking.CreateAsync(request, null, CurrentUserId, ct);
        if (outcome.Failed)
        {
            return ToProblem(outcome);
        }
        var id = outcome.Value!.Id;
        return CreatedAtAction(nameof(Get), new { id }, await ToResponseAsync(id, ct));
    }

    /// <summary>ピッキング実行・払出（D-20-20-01〜02。在庫を引き落として完了にする）</summary>
    [HttpPost("{id:int}/execute")]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<ActionResult<PickingOrderResponse>> Execute(int id, CancellationToken ct) =>
        await ToResponseAsync(await picking.ExecuteAsync(id, CurrentUserId, ct), id, ct);

    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<ActionResult<PickingOrderResponse>> Cancel(int id, CancellationToken ct) =>
        await ToResponseAsync(await picking.CancelAsync(id, ct), id, ct);

    private async Task<ActionResult<PickingOrderResponse>> ToResponseAsync(
        Outcome<PickingOrder> outcome, int id, CancellationToken ct) =>
        outcome.Failed ? ToProblem(outcome) : await ToResponseAsync(id, ct);

    /// <summary>応答は明細の品目・ロット・ロケーションを読み直して返す（サービスが返すのは追跡中の本体のみのため）</summary>
    private async Task<PickingOrderResponse> ToResponseAsync(int id, CancellationToken ct) =>
        ToResponse(await BaseQuery().FirstAsync(p => p.Id == id, ct));

    private ActionResult ToProblem(Outcome<PickingOrder> outcome) => outcome.Kind switch
    {
        OutcomeError.NotFound => NotFound(),
        OutcomeError.Conflict => this.ConflictProblem(outcome.Error),
        _ => this.BadRequestProblem(outcome.Error),
    };

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
