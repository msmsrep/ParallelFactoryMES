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
/// 不適合・逸脱管理（Spec.md 3.3：B-40-30 逸脱・品質不具合の記録、C-30 不適合対応・特別採用）。
/// 対応指示は不適合の連鎖（Spec.md 5.7）に従い、保留/廃棄はロットの在庫ステータスへ、
/// リワークはリワーク指図の自動起票へ、特採は承認記録付きのロット解放へ接続する。
/// </summary>
[ApiController]
[Route("api/nonconformances")]
[Authorize]
public class NonconformanceController(
    MesAppDbContext db,
    NumberingService numbering,
    LotStatusService lotStatus,
    IAuditLogger auditLogger) : ControllerBase
{
    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>一覧（C-30-10-01。状態・発生元でフィルタ可能）</summary>
    [HttpGet]
    public async Task<ActionResult<List<NonconformanceResponse>>> List(
        [FromQuery] NonconformanceStatus? status = null,
        [FromQuery] NonconformanceSource? source = null,
        CancellationToken ct = default)
    {
        var query = BaseQuery();
        if (status is not null)
        {
            query = query.Where(n => n.Status == status);
        }
        if (source is not null)
        {
            query = query.Where(n => n.Source == source);
        }
        var reports = await query.OrderByDescending(n => n.Id).ToListAsync(ct);
        return reports.Select(ToResponse).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<NonconformanceResponse>> Get(int id, CancellationToken ct)
    {
        var report = await BaseQuery().FirstOrDefaultAsync(n => n.Id == id, ct);
        return report is null ? NotFound() : ToResponse(report);
    }

    /// <summary>逸脱・品質不具合の記録（B-40-30-01〜02。現場作業者も登録可能）</summary>
    [HttpPost]
    public async Task<ActionResult<NonconformanceResponse>> Create(
        NonconformanceCreateRequest request, CancellationToken ct)
    {
        if (request.LotId is int lotId && !await db.Lots.AnyAsync(l => l.Id == lotId, ct))
        {
            return BadRequest(new ProblemDetails { Title = "存在しないロットIDです。" });
        }
        if (request.WorkOrderId is int workOrderId
            && !await db.WorkOrders.AnyAsync(w => w.Id == workOrderId, ct))
        {
            return BadRequest(new ProblemDetails { Title = "存在しない作業指示IDです。" });
        }
        if (request.InspectionOrderId is int inspectionOrderId
            && !await db.InspectionOrders.AnyAsync(i => i.Id == inspectionOrderId, ct))
        {
            return BadRequest(new ProblemDetails { Title = "存在しない検査指示IDです。" });
        }

        var report = new NonconformanceReport
        {
            ReportNo = await numbering.NextNonconformanceNoAsync(ct),
            Source = request.Source,
            LotId = request.LotId,
            WorkOrderId = request.WorkOrderId,
            InspectionOrderId = request.InspectionOrderId,
            Content = request.Content,
            CauseCategory = request.CauseCategory,
            CauseDetail = request.CauseDetail,
            ReportedByUserId = CurrentUserId,
        };
        db.NonconformanceReports.Add(report);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Quality", "NonconformanceCreate", nameof(NonconformanceReport),
            report.Id.ToString(), detail: $"reportNo={report.ReportNo}, source={report.Source}", ct: ct);
        var saved = await BaseQuery().FirstAsync(n => n.Id == report.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = report.Id }, ToResponse(saved));
    }

    /// <summary>
    /// 対応指示（C-30-20-01）。保留→ロット保留、廃棄→ロット廃棄予定、
    /// リワーク→リワーク指図の自動起票（対象ロットの由来指図が特定できる場合）、特採→承認待ち。
    /// </summary>
    [HttpPost("{id:int}/instruct")]
    [Authorize(Roles = RoleGroups.QualityManage)]
    public async Task<ActionResult<NonconformanceResponse>> InstructAction(
        int id, NonconformanceActionRequest request, CancellationToken ct)
    {
        var report = await db.NonconformanceReports
            .Include(n => n.Lot).ThenInclude(l => l!.SourceWorkOrder)
            .FirstOrDefaultAsync(n => n.Id == id, ct);
        if (report is null)
        {
            return NotFound();
        }
        if (report.Status is NonconformanceStatus.Closed)
        {
            return Conflict(new ProblemDetails { Title = "クローズ済みの不適合には対応指示できません。" });
        }

        report.Action = request.Action;
        report.ActionInstruction = request.Instruction;
        report.ActionInstructedByUserId = CurrentUserId;
        report.ActionInstructedAt = DateTimeOffset.UtcNow;
        report.Status = NonconformanceStatus.ActionInstructed;

        // 不適合の連鎖（Spec.md 5.7）
        switch (request.Action)
        {
            case NonconformanceAction.Hold when report.Lot is not null:
                lotStatus.ChangeStatus(report.Lot, LotStockStatus.OnHold,
                    LotStatusChangeSource.Nonconformance,
                    $"不適合 {report.ReportNo} の保留指示（{request.Instruction}）",
                    CurrentUserId, nonconformanceReportId: report.Id);
                break;
            case NonconformanceAction.Discard when report.Lot is not null:
                lotStatus.ChangeStatus(report.Lot, LotStockStatus.ToBeDiscarded,
                    LotStatusChangeSource.Nonconformance,
                    $"不適合 {report.ReportNo} の廃棄指示（{request.Instruction}）",
                    CurrentUserId, nonconformanceReportId: report.Id);
                break;
            case NonconformanceAction.Rework when report.Lot is not null:
                // リワーク指図の自動起票（B-70-10-01。由来指図が特定できる場合のみ）
                var sourceOrderId = report.Lot.SourceWorkOrder?.ManufacturingOrderId;
                if (sourceOrderId is not null)
                {
                    var rework = new ManufacturingOrder
                    {
                        OrderNo = await numbering.NextOrderNoAsync(ct),
                        ProductId = report.Lot.ProductId,
                        Quantity = report.Lot.InitialQuantity,
                        OrderType = ManufacturingOrderType.Rework,
                        SourceOrderId = sourceOrderId,
                        Note = $"不適合 {report.ReportNo} のリワーク",
                        CreatedByUserId = CurrentUserId,
                    };
                    db.ManufacturingOrders.Add(rework);
                    report.ReworkOrder = rework;
                }
                break;
        }

        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Quality", "NonconformanceInstruct", nameof(NonconformanceReport),
            id.ToString(), detail: $"reportNo={report.ReportNo}, action={request.Action}", ct: ct);
        var saved = await BaseQuery().FirstAsync(n => n.Id == id, ct);
        return ToResponse(saved);
    }

    /// <summary>対応実行記録（C-30-20-02）</summary>
    [HttpPost("{id:int}/record-action")]
    public async Task<ActionResult<NonconformanceResponse>> RecordAction(
        int id, NonconformanceActionRecordRequest request, CancellationToken ct)
    {
        var report = await db.NonconformanceReports.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (report is null)
        {
            return NotFound();
        }
        if (report.Status != NonconformanceStatus.ActionInstructed)
        {
            return Conflict(new ProblemDetails { Title = "対応指示済みの不適合のみ対応実績を記録できます。" });
        }

        report.ActionRecord = request.Record;
        report.ActionCompletedByUserId = CurrentUserId;
        report.ActionCompletedAt = DateTimeOffset.UtcNow;
        report.Status = NonconformanceStatus.ActionCompleted;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Quality", "NonconformanceAction", nameof(NonconformanceReport),
            id.ToString(), detail: $"reportNo={report.ReportNo}", ct: ct);
        var saved = await BaseQuery().FirstAsync(n => n.Id == id, ct);
        return ToResponse(saved);
    }

    /// <summary>
    /// 逸脱承認・特別採用の承認（C-30-20-03）。特採の場合は対象ロットを正常へ戻し次工程進行を許可する。
    /// </summary>
    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = RoleGroups.QualityManage)]
    public async Task<ActionResult<NonconformanceResponse>> Approve(int id, CancellationToken ct)
    {
        var report = await db.NonconformanceReports.Include(n => n.Lot)
            .FirstOrDefaultAsync(n => n.Id == id, ct);
        if (report is null)
        {
            return NotFound();
        }
        if (report.Status is NonconformanceStatus.Open)
        {
            return Conflict(new ProblemDetails { Title = "対応指示前の不適合は承認できません。" });
        }
        if (report.Status is NonconformanceStatus.Closed)
        {
            return Conflict(new ProblemDetails { Title = "既にクローズ済みです。" });
        }

        report.ApprovedByUserId = CurrentUserId;
        report.ApprovedAt = DateTimeOffset.UtcNow;
        report.Status = NonconformanceStatus.Closed;

        // 特採：承認記録付きで次工程進行（Spec.md 5.7）
        if (report.Action == NonconformanceAction.SpecialAcceptance && report.Lot is not null)
        {
            lotStatus.ChangeStatus(report.Lot, LotStockStatus.Normal,
                LotStatusChangeSource.Nonconformance,
                $"不適合 {report.ReportNo} の特採承認による解放",
                CurrentUserId, nonconformanceReportId: report.Id);
        }
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Quality", "NonconformanceApprove", nameof(NonconformanceReport),
            id.ToString(), detail: $"reportNo={report.ReportNo}, action={report.Action}", ct: ct);
        var saved = await BaseQuery().FirstAsync(n => n.Id == id, ct);
        return ToResponse(saved);
    }

    private IQueryable<NonconformanceReport> BaseQuery() =>
        db.NonconformanceReports.AsNoTracking()
            .Include(n => n.Lot)
            .Include(n => n.WorkOrder)
            .Include(n => n.ReworkOrder)
            .Include(n => n.ReportedBy);

    private static NonconformanceResponse ToResponse(NonconformanceReport n) =>
        new(n.Id, n.ReportNo, n.Source, n.Status,
            n.LotId, n.Lot?.LotNumber, n.WorkOrderId, n.WorkOrder?.WorkOrderNo,
            n.InspectionOrderId, n.Content, n.CauseCategory, n.CauseDetail,
            n.Action, n.ActionInstruction, n.ActionInstructedAt,
            n.ReworkOrderId, n.ReworkOrder?.OrderNo,
            n.ActionRecord, n.ActionCompletedAt,
            n.ApprovedByUserId, n.ApprovedAt,
            n.ReportedByUserId, n.ReportedBy?.DisplayName, n.CreatedAt);
}
