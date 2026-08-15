using System.Security.Claims;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Execution;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 製造トラブル報告（B-40-10-06 発生報告、B-60-10-02〜04 対応履歴の記録）
/// </summary>
[ApiController]
[Route("api/trouble-reports")]
[Authorize]
public class TroubleReportsController(MesAppDbContext db, IAuditLogger auditLogger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<TroubleReportResponse>>> List(
        [FromQuery] TroubleStatus? status = null,
        [FromQuery] TroubleCategory? category = null,
        CancellationToken ct = default)
    {
        var query = db.TroubleReports.AsNoTracking().AsQueryable();
        if (status is not null)
        {
            query = query.Where(t => t.Status == status);
        }
        if (category is not null)
        {
            query = query.Where(t => t.Category == category);
        }
        return await query.OrderByDescending(t => t.Id)
            .Select(Projection)
            .ToListAsync(ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TroubleReportResponse>> Get(int id, CancellationToken ct)
    {
        var report = await db.TroubleReports.AsNoTracking()
            .Where(t => t.Id == id).Select(Projection).FirstOrDefaultAsync(ct);
        return report is null ? NotFound() : report;
    }

    [HttpPost]
    public async Task<ActionResult<TroubleReportResponse>> Create(TroubleReportRequest request, CancellationToken ct)
    {
        if (request.WorkOrderId is int workOrderId
            && !await db.WorkOrders.AnyAsync(w => w.Id == workOrderId, ct))
        {
            return BadRequest(new ProblemDetails { Title = "存在しない作業指示IDです。" });
        }
        if (request.EquipmentId is int equipmentId
            && !await db.Equipments.AnyAsync(e => e.Id == equipmentId, ct))
        {
            return BadRequest(new ProblemDetails { Title = "存在しない設備IDです。" });
        }

        var report = new TroubleReport
        {
            OccurredAt = request.OccurredAt,
            Category = request.Category,
            WorkOrderId = request.WorkOrderId,
            EquipmentId = request.EquipmentId,
            Content = request.Content,
            ReportedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!,
        };
        db.TroubleReports.Add(report);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Execution", "TroubleReport", nameof(TroubleReport), report.Id.ToString(),
            detail: $"category={request.Category}", ct: ct);
        return CreatedAtAction(nameof(Get), new { id = report.Id }, await GetResponseAsync(report.Id, ct));
    }

    /// <summary>対応履歴の追記と状態更新（B-60-10-03〜04。履歴は追記式でタイムスタンプ付き）</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<TroubleReportResponse>> Update(
        int id, TroubleUpdateRequest request, CancellationToken ct)
    {
        var report = await db.TroubleReports.FindAsync([id], ct);
        if (report is null)
        {
            return NotFound();
        }

        if (!string.IsNullOrWhiteSpace(request.ResponseNote))
        {
            var userName = User.Identity?.Name;
            var entry = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm} {userName}] {request.ResponseNote}";
            report.ResponseHistory = string.IsNullOrEmpty(report.ResponseHistory)
                ? entry
                : $"{report.ResponseHistory}\n{entry}";
        }
        report.Status = request.Status;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Execution", "TroubleUpdate", nameof(TroubleReport), id.ToString(),
            detail: $"status={request.Status}", ct: ct);
        return await GetResponseAsync(id, ct);
    }

    private async Task<TroubleReportResponse> GetResponseAsync(int id, CancellationToken ct) =>
        await db.TroubleReports.AsNoTracking()
            .Where(t => t.Id == id).Select(Projection).FirstAsync(ct);

    private static readonly System.Linq.Expressions.Expression<Func<TroubleReport, TroubleReportResponse>> Projection =
        t => new TroubleReportResponse(t.Id, t.OccurredAt, t.Category,
            t.WorkOrderId, t.WorkOrder!.WorkOrderNo, t.EquipmentId, t.Equipment!.Name,
            t.Content, t.ResponseHistory, t.Status,
            t.ReportedByUserId, t.ReportedBy!.DisplayName, t.CreatedAt);
}
