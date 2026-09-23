using MesApp.Api.Services;
using MesApp.Core.Abstractions;
using System.Security.Claims;
using MesApp.Core.Contracts.Common;
using MesApp.Core.Contracts.Maintenance;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 治工具の利用実績記録と寿命管理（E-60-20：作業指示との紐付けによる利用実績（-01）、
/// 閾値到達前の交換・廃棄通知（-02）、寿命分析（-03））
/// <para>
/// 治工具の使用実績の記録はロールで絞らない（Spec.md 7.4 の意図的な例外）。
/// 気づいた人がその場で上げられることを優先する。指示・承認は別途ロールで絞る。
/// </para>
/// </summary>
[ApiController]
[Route("api/tool-usages")]
[Authorize]
public class ToolUsagesController(
    MesAppDbContext db, ShopFloorReportService reports, IBusinessDateService businessDate) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ToolUsageResponse>>> List(
        [FromQuery] PageQuery paging,
        [FromQuery] int? toolId = null,
        [FromQuery] int? workOrderId = null,
        CancellationToken ct = default)
    {
        var query = db.ToolUsages.AsNoTracking().AsQueryable();
        if (toolId is not null)
        {
            query = query.Where(u => u.ToolId == toolId);
        }
        if (workOrderId is not null)
        {
            query = query.Where(u => u.WorkOrderId == workOrderId);
        }
        return await query.OrderByDescending(u => u.Id)
            .Select(u => new ToolUsageResponse(
                u.Id, u.ToolId, u.Tool!.Code, u.WorkOrderId, u.WorkOrder!.WorkOrderNo,
                u.UsageCount, u.UsageHours, u.RecordedAt))
            .ToPagedResultAsync(paging, ct);
    }

    /// <summary>利用実績の記録（E-60-20-01。現場作業者も記録できる）</summary>
    [HttpPost]
    public async Task<ActionResult<ToolUsageResponse>> Create(ToolUsageRequest request, CancellationToken ct)
    {
        // 判定・保存は実績CSV取込と共通（ShopFloorReportService）
        var outcome = await reports.AddToolUsageAsync(
            request, null, User.FindFirstValue(ClaimTypes.NameIdentifier), ct);
        if (outcome.Failed)
        {
            return this.BadRequestProblem(outcome.Error!);
        }
        var usage = outcome.Value!;

        return await db.ToolUsages.AsNoTracking()
            .Where(u => u.Id == usage.Id)
            .Select(u => new ToolUsageResponse(
                u.Id, u.ToolId, u.Tool!.Code, u.WorkOrderId, u.WorkOrder!.WorkOrderNo,
                u.UsageCount, u.UsageHours, u.RecordedAt))
            .FirstAsync(ct);
    }

    /// <summary>
    /// 寿命分析（E-60-20-03）。期間内の使用ペースから寿命へ達する見込みを出し、交換の準備に使う。
    /// <para>
    /// 期間は製造日（業務日付）基準。見込みが立たないもの（寿命閾値が未設定・期間内に使用が無い・
    /// すでに寿命到達）は予測日を null で返す。適当な日付を置くと交換計画が実態とずれるため。
    /// </para>
    /// </summary>
    [HttpGet("life-analysis")]
    public async Task<ActionResult<List<ToolLifeAnalysisRow>>> LifeAnalysis(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken ct = default)
    {
        var fromStart = from is null ? (DateTimeOffset?)null : businessDate.GetRange(from.Value).Start;
        var toEnd = to is null ? (DateTimeOffset?)null : businessDate.GetRange(to.Value).End;
        var today = businessDate.Today;

        var tools = await db.Tools.AsNoTracking().Where(t => t.IsActive).ToListAsync(ct);
        // SQLiteはDateTimeOffsetの比較を翻訳できないため、期間とリセットの判定は取り出してから行う
        var usages = await db.ToolUsages.AsNoTracking()
            .Select(u => new { u.ToolId, u.WorkOrderId, u.UsageCount, u.UsageHours, u.RecordedAt })
            .ToListAsync(ct);

        return tools
            .OrderBy(t => t.Code, StringComparer.Ordinal)
            .Select(t =>
            {
                // 寿命の累計はリセット（メンテ完了）以降だけを数える（寿命ステータスと同じ条件）
                var effective = usages
                    .Where(u => u.ToolId == t.Id && (t.LifeResetAt is null || u.RecordedAt > t.LifeResetAt))
                    .ToList();
                var cumulative = effective.Sum(u => u.UsageCount);

                var period = effective
                    .Where(u => (fromStart is null || u.RecordedAt >= fromStart)
                                && (toEnd is null || u.RecordedAt < toEnd))
                    .ToList();
                var periodCount = period.Sum(u => u.UsageCount);
                // 使用のあった日で割る（稼働しない日を含めると、まだ余裕があるように見える）
                var usageDays = period
                    .Select(u => businessDate.GetBusinessDate(u.RecordedAt))
                    .Distinct()
                    .Count();
                var workOrderCount = period
                    .Where(u => u.WorkOrderId != null)
                    .Select(u => u.WorkOrderId!.Value)
                    .Distinct()
                    .Count();

                int? remaining = t.LifeThresholdCount is > 0
                    ? Math.Max(0, t.LifeThresholdCount.Value - cumulative)
                    : null;
                decimal? perDay = usageDays == 0 ? null : Math.Round((decimal)periodCount / usageDays, 2);
                decimal? perWorkOrder = workOrderCount == 0
                    ? null
                    : Math.Round((decimal)periodCount / workOrderCount, 2);

                DateOnly? estimated = remaining is > 0 && perDay is > 0
                    ? today.AddDays((int)Math.Ceiling(remaining.Value / perDay.Value))
                    : null;

                return new ToolLifeAnalysisRow(
                    t.Id, t.Code, t.Name, t.Status,
                    t.LifeThresholdCount, cumulative, remaining,
                    periodCount, usageDays, perDay, workOrderCount, perWorkOrder, estimated);
            })
            .ToList();
    }

    /// <summary>
    /// 寿命ステータス一覧（E-60-20-02）。寿命カウンタのリセット（治工具メンテ完了）以降の
    /// 累計と閾値を比較し、80%で警告・100%で要交換を通知する。
    /// </summary>
    [HttpGet("life-status")]
    public async Task<ActionResult<List<ToolLifeStatusRow>>> LifeStatus(
        [FromQuery] bool alertOnly = false, CancellationToken ct = default)
    {
        var tools = await db.Tools.AsNoTracking().Where(t => t.IsActive).ToListAsync(ct);
        // SQLiteはDateTimeOffset比較を翻訳できないため、リセット日時のフィルタはクライアント側で行う
        var usages = await db.ToolUsages.AsNoTracking()
            .Select(u => new { u.ToolId, u.UsageCount, u.UsageHours, u.RecordedAt })
            .ToListAsync(ct);

        var rows = tools
            .OrderBy(t => t.Code)
            .Select(t =>
            {
                var effective = usages
                    .Where(u => u.ToolId == t.Id && (t.LifeResetAt is null || u.RecordedAt > t.LifeResetAt))
                    .ToList();
                var count = effective.Sum(u => u.UsageCount);
                var hours = effective.Sum(u => u.UsageHours ?? 0);

                decimal? countRate = t.LifeThresholdCount is > 0
                    ? Math.Round((decimal)count / t.LifeThresholdCount.Value * 100, 1)
                    : null;
                decimal? hoursRate = t.LifeThresholdHours is > 0
                    ? Math.Round(hours / t.LifeThresholdHours.Value * 100, 1)
                    : null;
                var rate = (countRate, hoursRate) switch
                {
                    (decimal c, decimal h) => Math.Max(c, h),
                    (decimal c, null) => c,
                    (null, decimal h) => h,
                    _ => (decimal?)null,
                };
                return new ToolLifeStatusRow(
                    t.Id, t.Code, t.Name, t.Status,
                    t.LifeThresholdCount, count, t.LifeThresholdHours, hours,
                    rate,
                    IsWarning: rate is >= 80 and < 100,
                    IsLifeReached: rate is >= 100,
                    t.LifeResetAt);
            })
            .Where(r => !alertOnly || r.IsWarning || r.IsLifeReached)
            .ToList();
        return rows;
    }
}
