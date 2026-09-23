using System.Security.Claims;
using MesApp.Api.Services;
using MesApp.Core.Contracts.Common;
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
/// <c>InventoryController</c> の在庫操作と同じ在庫権限（<see cref="MesRoleGroups.InventoryManage"/>）で揃える。
/// 参照は認証済みユーザー全員に開放する。
/// </para>
/// </summary>
[ApiController]
[Route("api/transfer-orders")]
[Authorize]
public class TransferOrdersController(
    MesAppDbContext db,
    TransferOrderService transfers) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<TransferOrderResponse>>> List(
        [FromQuery] PageQuery paging,
        [FromQuery] TransferOrderStatus? status = null, CancellationToken ct = default)
    {
        var query = db.TransferOrders.AsNoTracking().AsQueryable();
        if (status is not null)
        {
            query = query.Where(t => t.Status == status);
        }
        return await query.OrderByDescending(t => t.Id)
            .Select(Projection)
            .ToPagedResultAsync(paging, ct);
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
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<ActionResult<TransferOrderResponse>> Create(TransferOrderRequest request, CancellationToken ct)
    {
        // 判定・保存は実績CSV取込と共通（TransferOrderService）
        var outcome = await transfers.CreateAsync(request, CurrentUserId, ct);
        if (outcome.Failed)
        {
            return ToProblem(outcome);
        }
        var id = outcome.Value!.Id;
        return CreatedAtAction(nameof(Get), new { id }, await GetResponseAsync(id, ct));
    }

    /// <summary>移動実行（B-50-10-02。在庫を移動して完了にする）</summary>
    [HttpPost("{id:int}/execute")]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<ActionResult<TransferOrderResponse>> Execute(int id, CancellationToken ct)
    {
        var outcome = await transfers.ExecuteAsync(id, CurrentUserId, ct);
        return outcome.Failed ? ToProblem(outcome) : await GetResponseAsync(id, ct);
    }

    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<ActionResult<TransferOrderResponse>> Cancel(int id, CancellationToken ct)
    {
        var outcome = await transfers.CancelAsync(id, ct);
        return outcome.Failed ? ToProblem(outcome) : await GetResponseAsync(id, ct);
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private ActionResult ToProblem(Outcome<TransferOrder> outcome) => outcome.Kind switch
    {
        OutcomeError.NotFound => NotFound(),
        OutcomeError.Conflict => this.ConflictProblem(outcome.Error),
        _ => this.BadRequestProblem(outcome.Error),
    };

    private async Task<TransferOrderResponse> GetResponseAsync(int id, CancellationToken ct) =>
        await db.TransferOrders.AsNoTracking()
            .Where(t => t.Id == id).Select(Projection).FirstAsync(ct);

    private static readonly System.Linq.Expressions.Expression<Func<TransferOrder, TransferOrderResponse>> Projection =
        t => new TransferOrderResponse(t.Id, t.LotId, t.Lot!.LotNumber, t.Lot!.Product!.Code, t.Quantity,
            t.FromLocationId, t.FromLocation!.Code, t.ToLocationId, t.ToLocation!.Code,
            t.Status, t.CreatedAt, t.ExecutedAt);
}
