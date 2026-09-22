using System.Security.Claims;
using MesApp.Api.Services;
using MesApp.Core.Contracts.Common;
using MesApp.Core.Contracts.Quality;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 出荷判定（Spec.md 3.7：H-10-10）。判定（可/保留/特採）→承認の単段階承認。
/// 出荷指示を対象にした承認済みの「可」または「特採」判定が、出荷実行のゲートになる。
/// </summary>
[ApiController]
[Route("api/shipment-judgments")]
[Authorize]
public class ShipmentJudgmentsController(
    MesAppDbContext db,
    ShipmentJudgmentService judgments) : ControllerBase
{
    /// <summary>出荷判定一覧（H-10-10-01）</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ShipmentJudgmentResponse>>> List(
        [FromQuery] PageQuery paging,
        [FromQuery] int? shippingOrderId = null, CancellationToken ct = default)
    {
        var query = BaseQuery();
        if (shippingOrderId is not null)
        {
            query = query.Where(j => j.ShippingOrderId == shippingOrderId);
        }
        var judgments = await query.OrderByDescending(j => j.Id).ToPagedResultAsync(paging, ct);
        return judgments.Map(ToResponse);
    }

    /// <summary>出荷判定書データ（H-10-10-04。帳票出力はPhase 7）</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ShipmentJudgmentResponse>> Get(int id, CancellationToken ct)
    {
        var judgment = await BaseQuery().FirstOrDefaultAsync(j => j.Id == id, ct);
        return judgment is null ? NotFound() : ToResponse(judgment);
    }

    /// <summary>出荷判定（H-10-10-02。対象はロットまたは出荷指示）</summary>
    [HttpPost]
    [Authorize(Roles = MesRoleGroups.QaManage)]
    public async Task<ActionResult<ShipmentJudgmentResponse>> Create(
        ShipmentJudgmentCreateRequest request, CancellationToken ct)
    {
        var outcome = await judgments.CreateAsync(request, User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct);
        if (outcome.Failed)
        {
            return ToProblem(outcome);
        }
        var id = outcome.Value!.Id;
        var saved = await BaseQuery().FirstAsync(j => j.Id == id, ct);
        return CreatedAtAction(nameof(Get), new { id }, ToResponse(saved));
    }

    /// <summary>判定承認（H-10-10-03）</summary>
    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = MesRoleGroups.QaManage)]
    public async Task<ActionResult<ShipmentJudgmentResponse>> Approve(int id, CancellationToken ct)
    {
        var outcome = await judgments.ApproveAsync(id, User.FindFirstValue(ClaimTypes.NameIdentifier), ct);
        if (outcome.Failed)
        {
            return ToProblem(outcome);
        }
        var saved = await BaseQuery().FirstAsync(j => j.Id == id, ct);
        return ToResponse(saved);
    }

    private ActionResult ToProblem(Outcome<ShipmentJudgment> outcome) => outcome.Kind switch
    {
        OutcomeError.NotFound => NotFound(),
        OutcomeError.Conflict => this.ConflictProblem(outcome.Error),
        _ => this.BadRequestProblem(outcome.Error),
    };

    private IQueryable<ShipmentJudgment> BaseQuery() =>
        db.ShipmentJudgments.AsNoTracking()
            .Include(j => j.Lot)
            .Include(j => j.ShippingOrder)
            .Include(j => j.JudgedBy);

    private static ShipmentJudgmentResponse ToResponse(ShipmentJudgment j) =>
        new(j.Id, j.JudgmentNo, j.LotId, j.Lot?.LotNumber,
            j.ShippingOrderId, j.ShippingOrder?.ShippingNo,
            j.Result, j.JudgedByUserId, j.JudgedBy?.DisplayName, j.JudgedAt,
            j.ApprovedByUserId, j.ApprovedAt, j.Note);
}
