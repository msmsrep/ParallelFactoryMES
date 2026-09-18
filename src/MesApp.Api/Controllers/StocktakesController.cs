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
    StocktakeService stocktakes) : ControllerBase
{
    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet]
    public async Task<ActionResult<PagedResult<StocktakeResponse>>> List(
        [FromQuery] PageQuery paging, CancellationToken ct = default)
    {
        var result = await BaseQuery().OrderByDescending(s => s.Id).ToPagedResultAsync(paging, ct);
        return result.Map(ToResponse);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<StocktakeResponse>> Get(int id, CancellationToken ct)
    {
        var stocktake = await BaseQuery().FirstOrDefaultAsync(s => s.Id == id, ct);
        return stocktake is null ? NotFound() : ToResponse(stocktake);
    }

    /// <summary>棚卸指示の作成（D-50-10-01。現在庫（数量&gt;0）のスナップショットを明細化）</summary>
    [HttpPost]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<ActionResult<StocktakeResponse>> Create(
        StocktakeCreateRequest request, CancellationToken ct)
    {
        var outcome = await stocktakes.CreateAsync(request, CurrentUserId, ct);
        if (outcome.Failed)
        {
            return ToProblem(outcome);
        }
        var id = outcome.Value!.Id;
        return CreatedAtAction(nameof(Get), new { id }, await ToResponseAsync(id, ct));
    }

    /// <summary>実棚数の登録（D-50-10-02。部分登録可・上書き可）</summary>
    [HttpPut("{id:int}/counts")]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<ActionResult<StocktakeResponse>> RegisterCounts(
        int id, StocktakeCountRequest request, CancellationToken ct) =>
        await ToResponseAsync(await stocktakes.RegisterCountsAsync(id, request, ct), id, ct);

    /// <summary>
    /// 棚卸確定（D-50-10-05）。実棚入力済みの明細について現在庫との差異を棚卸調整で反映する（D-50-10-04）。
    /// </summary>
    [HttpPost("{id:int}/finalize")]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<ActionResult<StocktakeResponse>> Finalize(int id, CancellationToken ct) =>
        await ToResponseAsync(await stocktakes.FinalizeAsync(id, CurrentUserId, ct), id, ct);

    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<ActionResult<StocktakeResponse>> Cancel(int id, CancellationToken ct) =>
        await ToResponseAsync(await stocktakes.CancelAsync(id, ct), id, ct);

    private async Task<ActionResult<StocktakeResponse>> ToResponseAsync(
        Outcome<Stocktake> outcome, int id, CancellationToken ct) =>
        outcome.Failed ? ToProblem(outcome) : await ToResponseAsync(id, ct);

    /// <summary>応答は明細の品目・ロット・ロケーションを読み直して返す（サービスが返すのは追跡中の本体のみのため）</summary>
    private async Task<StocktakeResponse> ToResponseAsync(int id, CancellationToken ct) =>
        ToResponse(await BaseQuery().FirstAsync(s => s.Id == id, ct));

    private ActionResult ToProblem(Outcome<Stocktake> outcome) => outcome.Kind switch
    {
        OutcomeError.NotFound => NotFound(),
        OutcomeError.Conflict => this.ConflictProblem(outcome.Error),
        _ => this.BadRequestProblem(outcome.Error),
    };

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
