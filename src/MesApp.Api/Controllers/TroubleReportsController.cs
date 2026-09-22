using MesApp.Core.Localization;
using System.Security.Claims;
using MesApp.Api.Services;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Common;
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
public class TroubleReportsController(MesAppDbContext db, ShopFloorReportService reports, IAuditLogger auditLogger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<TroubleReportResponse>>> List(
        [FromQuery] PageQuery paging,
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
            .ToPagedResultAsync(paging, ct);
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
        var outcome = await reports.AddTroubleReportAsync(request, User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct);
        if (outcome.Value is not { } report)
        {
            return this.BadRequestProblem(outcome.Error);
        }
        return CreatedAtAction(nameof(Get), new { id = report.Id }, await GetResponseAsync(report.Id, ct));
    }

    /// <summary>
    /// 対応履歴の追記と状態更新（B-60-10-03〜04。履歴は追記式でタイムスタンプ付き）。
    /// 状態は前進のみ（発生→対応中→完了、発生→完了）。完了からは理由を対応履歴に書いたときだけ対応中へ戻せる
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<TroubleReportResponse>> Update(
        int id, TroubleUpdateRequest request, CancellationToken ct)
    {
        var report = await db.TroubleReports.FindAsync([id], ct);
        if (report is null)
        {
            return NotFound();
        }

        var before = report.Status;
        var reopen = before == TroubleStatus.Closed && request.Status == TroubleStatus.InProgress;
        var allowed = before == request.Status || reopen || (before, request.Status) switch
        {
            (TroubleStatus.Open, TroubleStatus.InProgress or TroubleStatus.Closed) => true,
            (TroubleStatus.InProgress, TroubleStatus.Closed) => true,
            _ => false,
        };
        if (!allowed)
        {
            // 「発生」へ戻すと対応を始めた事実が消えるため、戻しは完了からの再オープンだけに限る
            return this.ConflictProblem(
                $"トラブル報告の状態を「{EnumLabels.Of(before)}」から「{EnumLabels.Of(request.Status)}」へは変更できません。");
        }
        if (reopen && string.IsNullOrWhiteSpace(request.ResponseNote))
        {
            return this.BadRequestProblem("完了したトラブル報告を対応中へ戻すときは、理由を対応履歴に入力してください。");
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
            detail: new { before, after = request.Status, reason = request.ResponseNote }, ct: ct);
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
