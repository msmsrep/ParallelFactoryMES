using System.Security.Claims;
using MesApp.Core.Contracts.Maintenance;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 治工具の利用実績記録と寿命管理（E-60-20：作業指示との紐付けによる利用実績（-01）、
/// 閾値到達前の交換・廃棄通知（-02）、寿命分析（-03））
/// </summary>
[ApiController]
[Route("api/tool-usages")]
[Authorize]
public class ToolUsagesController(MesAppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ToolUsageResponse>>> List(
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
        return await query.OrderByDescending(u => u.Id).Take(500)
            .Select(u => new ToolUsageResponse(
                u.Id, u.ToolId, u.Tool!.Code, u.WorkOrderId, u.WorkOrder!.WorkOrderNo,
                u.UsageCount, u.UsageHours, u.RecordedAt))
            .ToListAsync(ct);
    }

    /// <summary>利用実績の記録（E-60-20-01。現場作業者も記録できる）</summary>
    [HttpPost]
    public async Task<ActionResult<ToolUsageResponse>> Create(ToolUsageRequest request, CancellationToken ct)
    {
        var tool = await db.Tools.FirstOrDefaultAsync(t => t.Id == request.ToolId, ct);
        if (tool is null || !tool.IsActive)
        {
            return BadRequest(new ProblemDetails { Title = "存在しない（または無効な）治工具IDです。" });
        }
        if (request.WorkOrderId is int workOrderId
            && !await db.WorkOrders.AnyAsync(w => w.Id == workOrderId, ct))
        {
            return BadRequest(new ProblemDetails { Title = "存在しない作業指示IDです。" });
        }
        if (request.UsageCount <= 0 && (request.UsageHours is null or <= 0))
        {
            return BadRequest(new ProblemDetails { Title = "使用回数または使用時間のどちらかを記録してください。" });
        }

        var usage = new ToolUsage
        {
            ToolId = request.ToolId,
            WorkOrderId = request.WorkOrderId,
            UsageCount = request.UsageCount,
            UsageHours = request.UsageHours,
            RecordedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
        };
        db.ToolUsages.Add(usage);
        await db.SaveChangesAsync(ct);

        return await db.ToolUsages.AsNoTracking()
            .Where(u => u.Id == usage.Id)
            .Select(u => new ToolUsageResponse(
                u.Id, u.ToolId, u.Tool!.Code, u.WorkOrderId, u.WorkOrder!.WorkOrderNo,
                u.UsageCount, u.UsageHours, u.RecordedAt))
            .FirstAsync(ct);
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
