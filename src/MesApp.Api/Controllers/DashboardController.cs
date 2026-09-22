using MesApp.Api.Localization;
using MesApp.Api.Services;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MesApp.Api.Controllers;

/// <summary>
/// ダッシュボードの期間別・軸別集計（Spec.md 3.8。B-60-10-05 生産性／C-40-10-05 不適合／E-20-10-03 稼働モニタリング）。
/// 期間は製造日（Spec.md 3.9）の閉区間 from〜to。省略時は当日の製造日。集計は <see cref="DashboardService"/>。
/// </summary>
[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController(DashboardService dashboard, IBusinessDateService businessDate) : ControllerBase
{
    /// <summary>日・週・月の区切りごとの推移</summary>
    [HttpGet("trend")]
    public async Task<ActionResult<DashboardSummaryResponse>> Trend(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] DashboardPeriodUnit unit = DashboardPeriodUnit.Day,
        [FromQuery] int? processId = null,
        [FromQuery] int? productId = null,
        [FromQuery] int? workCenterId = null,
        CancellationToken ct = default)
    {
        var filter = new DashboardService.Filter(
            from ?? businessDate.Today, to ?? businessDate.Today, processId, productId, workCenterId);
        if (Validate(filter) is { } error)
        {
            return error;
        }
        if (DashboardService.CountPeriods(filter.From, filter.To, unit) > DashboardService.MaxPeriods)
        {
            return this.BadRequestProblem(
                ApiText.T("区切りが多すぎます（上限 {0}）。期間を短くするか、週・月の単位で表示してください。", DashboardService.MaxPeriods));
        }
        return await dashboard.GetTrendAsync(filter, unit, ct);
    }

    /// <summary>指定期間の軸別の内訳</summary>
    [HttpGet("breakdown")]
    public async Task<ActionResult<DashboardSummaryResponse>> Breakdown(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] DashboardAxis axis = DashboardAxis.Process,
        [FromQuery] int? processId = null,
        [FromQuery] int? productId = null,
        [FromQuery] int? workCenterId = null,
        CancellationToken ct = default)
    {
        var filter = new DashboardService.Filter(
            from ?? businessDate.Today, to ?? businessDate.Today, processId, productId, workCenterId);
        if (Validate(filter) is { } error)
        {
            return error;
        }
        return await dashboard.GetBreakdownAsync(filter, axis, ct);
    }

    private BadRequestObjectResult? Validate(DashboardService.Filter filter) =>
        filter.From > filter.To
            ? this.BadRequestProblem(ApiText.T("期間の開始日が終了日より後になっています。"))
            : null;
}
