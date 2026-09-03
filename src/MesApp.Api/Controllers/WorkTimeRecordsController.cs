using MesApp.Core.Abstractions;
using System.Security.Claims;
using MesApp.Core.Contracts.Execution;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 作業時間記録（B-30-30-02 直接作業／F-30-20-02 間接時間管理）。
/// 本人が自分の時間を記録する。参照は本人＋管理系ロール。
/// </summary>
[ApiController]
[Route("api/work-time-records")]
[Authorize]
public class WorkTimeRecordsController(MesAppDbContext db, IAuditLogger auditLogger) : ControllerBase
{
    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<ActionResult<List<WorkTimeResponse>>> List(
        [FromQuery] string? userId = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        // 一般ユーザーは自分の記録のみ参照可能
        var isManager = User.IsInRole(Core.Constants.MesRoles.SystemAdmin)
                        || User.IsInRole(Core.Constants.MesRoles.ProductionManager);
        var targetUserId = isManager ? userId : CurrentUserId;

        var query = db.WorkTimeRecords.AsNoTracking().AsQueryable();
        if (targetUserId is not null)
        {
            query = query.Where(r => r.UserId == targetUserId);
        }
        // SQLiteはDateTimeOffsetの比較・並べ替えを翻訳できないため、期間フィルタはクライアント側で行う
        var records = await query.OrderBy(r => r.Id)
            .Select(r => new WorkTimeResponse(
                r.Id, r.UserId, r.User!.DisplayName, r.Type, r.IndirectCategory,
                r.WorkOrderId, r.WorkOrder!.WorkOrderNo, r.StartedAt, r.EndedAt, r.Note))
            .ToListAsync(ct);
        return records
            .Where(r => (from is null || r.StartedAt >= from) && (to is null || r.StartedAt < to))
            .ToList();
    }

    [HttpPost]
    [Authorize(Roles = MesRoleGroups.ShopFloorRecord)]
    public async Task<ActionResult<WorkTimeResponse>> Create(WorkTimeRequest request, CancellationToken ct)
    {
        if (request.Type == WorkTimeType.Direct && request.WorkOrderId is null)
        {
            return BadRequest(new ProblemDetails { Title = "直接作業には作業指示ID（workOrderId）が必要です。" });
        }
        if (request.WorkOrderId is int workOrderId
            && !await db.WorkOrders.AnyAsync(w => w.Id == workOrderId, ct))
        {
            return BadRequest(new ProblemDetails { Title = "存在しない作業指示IDです。" });
        }

        var record = new WorkTimeRecord
        {
            UserId = CurrentUserId,
            Type = request.Type,
            IndirectCategory = request.IndirectCategory,
            WorkOrderId = request.WorkOrderId,
            StartedAt = request.StartedAt,
            EndedAt = request.EndedAt,
            Note = request.Note,
        };
        db.WorkTimeRecords.Add(record);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Execution", "WorkTime", nameof(WorkTimeRecord), record.Id.ToString(),
            detail: new { type = record.Type, workOrderId = record.WorkOrderId }, ct: ct);

        var saved = await db.WorkTimeRecords.AsNoTracking()
            .Where(r => r.Id == record.Id)
            .Select(r => new WorkTimeResponse(
                r.Id, r.UserId, r.User!.DisplayName, r.Type, r.IndirectCategory,
                r.WorkOrderId, r.WorkOrder!.WorkOrderNo, r.StartedAt, r.EndedAt, r.Note))
            .FirstAsync(ct);
        return CreatedAtAction(nameof(List), null, saved);
    }
}
