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
/// 不適合・逸脱管理（Spec.md 3.3：B-40-30 逸脱・品質不具合の記録、C-30 不適合対応・特別採用）。
/// 対応指示は不適合の連鎖（Spec.md 5.7）に従い、保留/廃棄はロットの在庫ステータスへ、
/// リワークはリワーク指図の自動起票へ、特採は承認記録付きのロット解放へ接続する。
/// <para>
/// 不適合の発見・対応実績の記録はロールで絞らない（Spec.md 7.4 の意図的な例外）。
/// 気づいた人がその場で上げられることを優先する。指示・承認は別途ロールで絞る。
/// </para>
/// </summary>
[ApiController]
[Route("api/nonconformances")]
[Authorize]
public class NonconformanceController(
    MesAppDbContext db,
    NonconformanceService nonconformances) : ControllerBase
{
    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>一覧（C-30-10-01。状態・発生元でフィルタ可能）</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<NonconformanceResponse>>> List(
        [FromQuery] PageQuery paging,
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
        var reports = await query.OrderByDescending(n => n.Id).ToPagedResultAsync(paging, ct);
        return reports.Map(ToResponse);
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
        var outcome = await nonconformances.CreateAsync(request, null, CurrentUserId, ct);
        if (outcome.Failed)
        {
            return ToProblem(outcome);
        }
        var id = outcome.Value!.Id;
        return CreatedAtAction(nameof(Get), new { id }, await ToResponseAsync(id, ct));
    }

    /// <summary>
    /// 対応指示（C-30-20-01）。保留→ロット保留、廃棄→ロット廃棄予定、
    /// リワーク→リワーク指図の自動起票（対象ロットの由来指図が特定できる場合）、特採→承認待ち。
    /// </summary>
    [HttpPost("{id:int}/instruct")]
    [Authorize(Roles = MesRoleGroups.QualityManage)]
    public async Task<ActionResult<NonconformanceResponse>> InstructAction(
        int id, NonconformanceActionRequest request, CancellationToken ct) =>
        await ToResponseAsync(await nonconformances.InstructAsync(id, request, CurrentUserId, ct), id, ct);

    /// <summary>対応実行記録（C-30-20-02）</summary>
    [HttpPost("{id:int}/record-action")]
    public async Task<ActionResult<NonconformanceResponse>> RecordAction(
        int id, NonconformanceActionRecordRequest request, CancellationToken ct) =>
        await ToResponseAsync(await nonconformances.RecordActionAsync(id, request, CurrentUserId, ct), id, ct);

    /// <summary>
    /// 逸脱承認・特別採用の承認（C-30-20-03）。特採の場合は対象ロットを正常へ戻し次工程進行を許可する。
    /// </summary>
    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = MesRoleGroups.QualityManage)]
    public async Task<ActionResult<NonconformanceResponse>> Approve(int id, CancellationToken ct) =>
        await ToResponseAsync(await nonconformances.ApproveAsync(id, CurrentUserId, ct), id, ct);

    private async Task<ActionResult<NonconformanceResponse>> ToResponseAsync(
        Outcome<NonconformanceReport> outcome, int id, CancellationToken ct) =>
        outcome.Failed ? ToProblem(outcome) : await ToResponseAsync(id, ct);

    /// <summary>応答は関連を読み直して返す（サービスが返すのは追跡中の本体のみのため）</summary>
    private async Task<NonconformanceResponse> ToResponseAsync(int id, CancellationToken ct) =>
        ToResponse(await BaseQuery().FirstAsync(n => n.Id == id, ct));

    private ActionResult ToProblem(Outcome<NonconformanceReport> outcome) => outcome.Kind switch
    {
        OutcomeError.NotFound => NotFound(),
        OutcomeError.Conflict => this.ConflictProblem(outcome.Error),
        _ => this.BadRequestProblem(outcome.Error),
    };

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
