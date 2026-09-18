using System.Linq.Expressions;
using System.Security.Claims;
using MesApp.Core.Constants;
using MesApp.Api.Services;
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
public class ToolIssuesController(MesAppDbContext db, ToolIssueService toolIssues) : ControllerBase
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
        var outcome = await toolIssues.AllocateAsync(request, CurrentUserId, ct);
        if (outcome.Failed)
        {
            return ToProblem(outcome);
        }
        var id = outcome.Value!.Id;
        return CreatedAtAction(nameof(Get), new { id }, await LoadAsync(id, ct));
    }

    /// <summary>払出・受領確認（B-20-30-02〜03）。渡す直前にもう一度使えるかを見る</summary>
    [HttpPost("{id:int}/issue")]
    [Authorize(Roles = MesRoleGroups.ShopFloorRecord)]
    public async Task<ActionResult<ToolIssueResponse>> Issue(
        int id, ToolIssueReceiveRequest request, CancellationToken ct) =>
        await ToResponseAsync(await toolIssues.IssueAsync(id, request, CurrentUserId, ct), id, ct);

    /// <summary>返却（使用を終えて戻す）。治工具は再び引当可能になる</summary>
    [HttpPost("{id:int}/return")]
    [Authorize(Roles = MesRoleGroups.ShopFloorRecord)]
    public async Task<ActionResult<ToolIssueResponse>> Return(
        int id, ToolIssueCloseRequest request, CancellationToken ct) =>
        await ToResponseAsync(await toolIssues.ReturnAsync(id, request, CurrentUserId, ct), id, ct);

    /// <summary>引当の取消（払出前のみ）</summary>
    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = MesRoleGroups.ProductionManage)]
    public async Task<ActionResult<ToolIssueResponse>> Cancel(
        int id, ToolIssueCloseRequest request, CancellationToken ct) =>
        await ToResponseAsync(await toolIssues.CancelAsync(id, request, ct), id, ct);

    private async Task<ActionResult<ToolIssueResponse>> ToResponseAsync(
        Outcome<ToolIssue> outcome, int id, CancellationToken ct) =>
        outcome.Failed ? ToProblem(outcome) : await LoadAsync(id, ct);

    private ActionResult ToProblem(Outcome<ToolIssue> outcome) => outcome.Kind switch
    {
        OutcomeError.NotFound => this.NotFoundProblem(outcome.Error),
        OutcomeError.Conflict => this.ConflictProblem(outcome.Error),
        _ => this.BadRequestProblem(outcome.Error),
    };

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
