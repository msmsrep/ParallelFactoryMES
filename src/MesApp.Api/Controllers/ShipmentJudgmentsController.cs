using System.Security.Claims;
using MesApp.Api.Services;
using MesApp.Core.Abstractions;
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
    NumberingService numbering,
    IAuditLogger auditLogger) : ControllerBase
{
    /// <summary>出荷判定一覧（H-10-10-01）</summary>
    [HttpGet]
    public async Task<ActionResult<List<ShipmentJudgmentResponse>>> List(
        [FromQuery] int? shippingOrderId = null, CancellationToken ct = default)
    {
        var query = BaseQuery();
        if (shippingOrderId is not null)
        {
            query = query.Where(j => j.ShippingOrderId == shippingOrderId);
        }
        var judgments = await query.OrderByDescending(j => j.Id).ToListAsync(ct);
        return judgments.Select(ToResponse).ToList();
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
    [Authorize(Roles = RoleGroups.QaManage)]
    public async Task<ActionResult<ShipmentJudgmentResponse>> Create(
        ShipmentJudgmentCreateRequest request, CancellationToken ct)
    {
        if (request.LotId is null && request.ShippingOrderId is null)
        {
            return BadRequest(new ProblemDetails { Title = "対象ロットIDまたは出荷指示IDを指定してください。" });
        }
        if (request.LotId is int lotId && !await db.Lots.AnyAsync(l => l.Id == lotId, ct))
        {
            return BadRequest(new ProblemDetails { Title = "存在しないロットIDです。" });
        }
        if (request.ShippingOrderId is int shippingOrderId
            && !await db.ShippingOrders.AnyAsync(s => s.Id == shippingOrderId, ct))
        {
            return BadRequest(new ProblemDetails { Title = "存在しない出荷指示IDです。" });
        }

        var judgment = new ShipmentJudgment
        {
            JudgmentNo = await numbering.NextJudgmentNoAsync(ct),
            LotId = request.LotId,
            ShippingOrderId = request.ShippingOrderId,
            Result = request.Result,
            JudgedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            Note = request.Note,
        };
        db.ShipmentJudgments.Add(judgment);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Quality", "ShipmentJudge", nameof(ShipmentJudgment), judgment.Id.ToString(),
            detail: $"judgmentNo={judgment.JudgmentNo}, result={judgment.Result}", ct: ct);
        var saved = await BaseQuery().FirstAsync(j => j.Id == judgment.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = judgment.Id }, ToResponse(saved));
    }

    /// <summary>判定承認（H-10-10-03）</summary>
    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = RoleGroups.QaManage)]
    public async Task<ActionResult<ShipmentJudgmentResponse>> Approve(int id, CancellationToken ct)
    {
        var judgment = await db.ShipmentJudgments.FirstOrDefaultAsync(j => j.Id == id, ct);
        if (judgment is null)
        {
            return NotFound();
        }
        if (judgment.ApprovedAt is not null)
        {
            return Conflict(new ProblemDetails { Title = "既に承認済みです。" });
        }
        judgment.ApprovedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        judgment.ApprovedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Quality", "ShipmentJudgeApprove", nameof(ShipmentJudgment), id.ToString(),
            detail: $"judgmentNo={judgment.JudgmentNo}", ct: ct);
        var saved = await BaseQuery().FirstAsync(j => j.Id == id, ct);
        return ToResponse(saved);
    }

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
