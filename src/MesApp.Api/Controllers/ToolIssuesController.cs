using System.Linq.Expressions;
using System.Security.Claims;
using MesApp.Api.Policies;
using MesApp.Core.Abstractions;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Maintenance;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 治工具の引当・払出指示・受領確認（Spec.md 3.2 前段取り。B-20-30-01〜03）。
/// 現物の所在を扱うものであり、寿命の累計（<c>api/tool-usages</c>。E-60-20-01）とは別物。
/// </summary>
[ApiController]
[Route("api/tool-issues")]
[Authorize]
public class ToolIssuesController(MesAppDbContext db, IAuditLogger auditLogger) : ControllerBase
{
    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>引当・払出の一覧（作業指示・治工具・状態で絞り込む）</summary>
    [HttpGet]
    public async Task<ActionResult<List<ToolIssueResponse>>> List(
        [FromQuery] int? workOrderId = null,
        [FromQuery] int? toolId = null,
        [FromQuery] bool openOnly = false,
        CancellationToken ct = default)
    {
        var query = db.ToolIssues.AsNoTracking().AsQueryable();
        if (workOrderId is not null)
        {
            query = query.Where(i => i.WorkOrderId == workOrderId);
        }
        if (toolId is not null)
        {
            query = query.Where(i => i.ToolId == toolId);
        }
        if (openOnly)
        {
            // 「いま現場に出ている治工具」を見るための絞り込み
            query = query.Where(i => i.Status == ToolIssueStatus.Allocated
                                     || i.Status == ToolIssueStatus.Issued);
        }
        return await query
            .OrderByDescending(i => i.Id)
            .Select(Projection)
            .ToListAsync(ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ToolIssueResponse>> Get(int id, CancellationToken ct)
    {
        var issue = await db.ToolIssues.AsNoTracking()
            .Where(i => i.Id == id)
            .Select(Projection)
            .FirstOrDefaultAsync(ct);
        return issue is null ? NotFound() : issue;
    }

    /// <summary>引当（B-20-30-01）。使えない治工具は ToolIssuePolicy が弾く</summary>
    [HttpPost]
    [Authorize(Roles = MesRoleGroups.ProductionManage)]
    public async Task<ActionResult<ToolIssueResponse>> Allocate(
        ToolAllocateRequest request, CancellationToken ct)
    {
        var tool = await db.Tools.FindAsync([request.ToolId], ct);
        if (tool is null)
        {
            return NotFound(new ProblemDetails { Title = $"治工具ID {request.ToolId} は登録されていません。" });
        }
        var workOrder = await db.WorkOrders.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == request.WorkOrderId, ct);
        if (workOrder is null)
        {
            return NotFound(new ProblemDetails { Title = $"作業指示ID {request.WorkOrderId} は登録されていません。" });
        }

        if (await CheckIssuableAsync(tool, ct) is { } reason)
        {
            return Conflict(new ProblemDetails { Title = reason });
        }

        var issue = new ToolIssue
        {
            ToolId = tool.Id,
            WorkOrderId = workOrder.Id,
            Status = ToolIssueStatus.Allocated,
            AllocatedByUserId = CurrentUserId,
            Note = request.Note,
        };
        db.ToolIssues.Add(issue);
        // 引当た時点で他の作業指示から引けなくする（現物は1つしかない）
        tool.Status = ToolStatus.InUse;
        await db.SaveChangesAsync(ct);

        await auditLogger.LogAsync("Execution", "ToolAllocate", nameof(ToolIssue), issue.Id.ToString(),
            detail: $"tool={tool.Code} workOrder={workOrder.WorkOrderNo}", ct: ct);
        return CreatedAtAction(nameof(Get), new { id = issue.Id }, await LoadAsync(issue.Id, ct));
    }

    /// <summary>払出・受領確認（B-20-30-02〜03）。渡す直前にもう一度使えるかを見る</summary>
    [HttpPost("{id:int}/issue")]
    [Authorize(Roles = MesRoleGroups.ShopFloorRecord)]
    public async Task<ActionResult<ToolIssueResponse>> Issue(
        int id, ToolIssueReceiveRequest request, CancellationToken ct)
    {
        var issue = await db.ToolIssues.Include(i => i.Tool).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (issue is null)
        {
            return NotFound();
        }
        if (issue.Status != ToolIssueStatus.Allocated)
        {
            return Conflict(new ProblemDetails { Title = $"状態 '{issue.Status}' の引当は払い出せません。" });
        }

        // 引当てから払出までの間に寿命へ達している／メンテへ入っていることがある
        if (await CheckIssuableAsync(issue.Tool!, ct, ignoreIssueId: issue.Id) is { } reason)
        {
            return Conflict(new ProblemDetails { Title = reason });
        }

        var receivedBy = request.IssuedToUserId ?? CurrentUserId;
        if (receivedBy is not null && !await db.Users.AnyAsync(u => u.Id == receivedBy, ct))
        {
            return BadRequest(new ProblemDetails { Title = "受領者が登録されていません。" });
        }

        issue.Status = ToolIssueStatus.Issued;
        issue.IssuedAt = DateTimeOffset.UtcNow;
        issue.IssuedToUserId = receivedBy;
        await db.SaveChangesAsync(ct);

        await auditLogger.LogAsync("Execution", "ToolIssue", nameof(ToolIssue), id.ToString(),
            detail: $"tool={issue.Tool!.Code} receivedBy={receivedBy}", ct: ct);
        return await LoadAsync(id, ct);
    }

    /// <summary>返却（使用を終えて戻す）。治工具は再び引当可能になる</summary>
    [HttpPost("{id:int}/return")]
    [Authorize(Roles = MesRoleGroups.ShopFloorRecord)]
    public async Task<ActionResult<ToolIssueResponse>> Return(
        int id, ToolIssueCloseRequest request, CancellationToken ct)
    {
        var issue = await db.ToolIssues.Include(i => i.Tool).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (issue is null)
        {
            return NotFound();
        }
        if (issue.Status is not (ToolIssueStatus.Allocated or ToolIssueStatus.Issued))
        {
            return Conflict(new ProblemDetails { Title = $"状態 '{issue.Status}' の引当は返却できません。" });
        }

        issue.Status = ToolIssueStatus.Returned;
        issue.ReturnedAt = DateTimeOffset.UtcNow;
        issue.ReturnedByUserId = CurrentUserId;
        issue.Note = request.Note ?? issue.Note;
        await RestoreToolStatusAsync(issue, ct);
        await db.SaveChangesAsync(ct);

        await auditLogger.LogAsync("Execution", "ToolReturn", nameof(ToolIssue), id.ToString(),
            detail: $"tool={issue.Tool!.Code}", ct: ct);
        return await LoadAsync(id, ct);
    }

    /// <summary>引当の取消（払出前のみ）</summary>
    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = MesRoleGroups.ProductionManage)]
    public async Task<ActionResult<ToolIssueResponse>> Cancel(
        int id, ToolIssueCloseRequest request, CancellationToken ct)
    {
        var issue = await db.ToolIssues.Include(i => i.Tool).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (issue is null)
        {
            return NotFound();
        }
        if (issue.Status != ToolIssueStatus.Allocated)
        {
            return Conflict(new ProblemDetails
            {
                Title = "払出済みの引当は取り消せません。返却で戻してください。",
            });
        }

        issue.Status = ToolIssueStatus.Canceled;
        issue.Note = request.Note ?? issue.Note;
        await RestoreToolStatusAsync(issue, ct);
        await db.SaveChangesAsync(ct);

        await auditLogger.LogAsync("Execution", "ToolAllocateCancel", nameof(ToolIssue), id.ToString(),
            detail: $"tool={issue.Tool!.Code} reason={request.Note}", ct: ct);
        return await LoadAsync(id, ct);
    }

    /// <summary>
    /// 治工具の状態を引当前へ戻す。メンテナンス中・廃棄へ変わっている場合はそのまま
    /// （返却が状態を上書きして、メンテ中の治工具を使える状態に戻してしまわないようにする）
    /// </summary>
    private async Task RestoreToolStatusAsync(ToolIssue issue, CancellationToken ct)
    {
        var tool = issue.Tool ?? await db.Tools.FindAsync([issue.ToolId], ct);
        if (tool is not null && tool.Status == ToolStatus.InUse)
        {
            tool.Status = ToolStatus.Available;
        }
    }

    /// <summary>引当・払出の可否（寿命の累計と他の引当を見る）</summary>
    private async Task<string?> CheckIssuableAsync(Tool tool, CancellationToken ct, int? ignoreIssueId = null)
    {
        // 寿命の累計はリセット（メンテ完了）以降だけを数える。E-60-20-02 と同じ条件
        var usages = await db.ToolUsages.AsNoTracking()
            .Where(u => u.ToolId == tool.Id)
            .Select(u => new { u.UsageCount, u.UsageHours, u.RecordedAt })
            .ToListAsync(ct);
        var effective = usages
            .Where(u => tool.LifeResetAt is null || u.RecordedAt > tool.LifeResetAt)
            .ToList();
        var lifeReached = ToolIssuePolicy.IsLifeReached(
            tool, effective.Sum(u => u.UsageCount), effective.Sum(u => u.UsageHours ?? 0));

        var hasOpen = await db.ToolIssues.AsNoTracking()
            .AnyAsync(i => i.ToolId == tool.Id
                           && (ignoreIssueId == null || i.Id != ignoreIssueId)
                           && (i.Status == ToolIssueStatus.Allocated || i.Status == ToolIssueStatus.Issued), ct);

        return ToolIssuePolicy.CheckIssuable(tool, lifeReached, hasOpen);
    }

    private async Task<ToolIssueResponse> LoadAsync(int id, CancellationToken ct) =>
        await db.ToolIssues.AsNoTracking()
            .Where(i => i.Id == id)
            .Select(Projection)
            .FirstAsync(ct);

    /// <summary>
    /// 一覧・単票で共通の射影。**メソッドではなく式として持つ**
    /// （EF Core はメソッド呼び出しをSQLへ翻訳できず、実行時に落ちるため）
    /// </summary>
    private static readonly Expression<Func<ToolIssue, ToolIssueResponse>> Projection =
        i => new ToolIssueResponse(
            i.Id, i.ToolId, i.Tool!.Code, i.Tool!.Name,
            i.WorkOrderId, i.WorkOrder!.WorkOrderNo, i.WorkOrder!.Product!.Code, i.WorkOrder!.Process!.Code,
            i.Status, i.AllocatedAt,
            i.IssuedAt, i.IssuedToUserId, i.IssuedTo == null ? null : i.IssuedTo.DisplayName,
            i.ReturnedAt, i.Note);
}
