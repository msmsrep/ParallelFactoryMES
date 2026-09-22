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
    ShippingService shipping) : ControllerBase
{
    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

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
        var outcome = await shipping.CreateAsync(request, shippingNo: null, CurrentUserId, ct);
        if (outcome.Failed)
        {
            return ToProblem(outcome);
        }
        var id = outcome.Value!.Id;
        return CreatedAtAction(nameof(Get), new { id }, await ToResponseAsync(id, ct));
    }

    /// <summary>
    /// 出荷実行（D-40-30。指定ロットの在庫を引き落とし、明細の出荷済数量を更新。
    /// 部分出荷可。全明細が満たされると完了（D-40-30-05））
    /// </summary>
    [HttpPost("{id:int}/ship")]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<ActionResult<ShippingOrderResponse>> Ship(
        int id, ShipExecuteRequest request, CancellationToken ct) =>
        await ToResponseAsync(await shipping.ShipAsync(id, request, CurrentUserId, ct), id, ct);

    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<ActionResult<ShippingOrderResponse>> Cancel(int id, CancellationToken ct) =>
        await ToResponseAsync(await shipping.CancelAsync(id, ct), id, ct);

    private async Task<ActionResult<ShippingOrderResponse>> ToResponseAsync(
        Outcome<ShippingOrder> outcome, int id, CancellationToken ct) =>
        outcome.Failed ? ToProblem(outcome) : await ToResponseAsync(id, ct);

    /// <summary>応答は明細・品目を読み直して返す（サービスが返すのは追跡中の本体のみのため）</summary>
    private async Task<ShippingOrderResponse> ToResponseAsync(int id, CancellationToken ct) =>
        ToResponse(await BaseQuery().FirstAsync(s => s.Id == id, ct));

    private ActionResult ToProblem(Outcome<ShippingOrder> outcome) => outcome.Kind switch
    {
        OutcomeError.NotFound => NotFound(),
        OutcomeError.Conflict => this.ConflictProblem(outcome.Error),
        _ => this.BadRequestProblem(outcome.Error),
    };

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
