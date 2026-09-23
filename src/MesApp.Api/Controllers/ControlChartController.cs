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
/// 管理図と工程能力指数（Spec.md 3.3 品質傾向管理。C-50-10-01〜02）。
/// 検査実績の測定値を検査指示ごとの群にまとめ、X̄-R（群の大きさ1なら X-Rs）管理図を返す。
/// 期間は測定日時が属する製造日（業務日付）。計算は <see cref="ControlChartCalculator"/>。
/// 本格的なSPC・予兆管理（C-50-10-03）は将来拡張。
/// </summary>
[ApiController]
[Route("api/quality/control-chart")]
[Authorize]
public class ControlChartController(MesAppDbContext db, IBusinessDateService businessDate) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ControlChartResponse>> Get(
        [FromQuery] int inspectionItemId,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken ct = default)
    {
        var item = await db.InspectionItems.AsNoTracking()
            .Where(i => i.Id == inspectionItemId)
            .Select(i => new { i.Id, i.Code, i.Name })
            .FirstOrDefaultAsync(ct);
        if (item is null)
        {
            return NotFound();
        }

        // SQLiteはDateTimeOffsetの比較を翻訳できないため、期間フィルタはクライアント側で行う
        var fromStart = from is null ? (DateTimeOffset?)null : businessDate.GetRange(from.Value).Start;
        var toEnd = to is null ? (DateTimeOffset?)null : businessDate.GetRange(to.Value).End;

        // 取消した検査の測定値は使わない。規格値は指示発行時のスナップショット（Spec.md 5.7）
        var results = (await db.InspectionResults.AsNoTracking()
                .Where(r => r.InspectionItemId == inspectionItemId && r.MeasuredValue != null)
                .Join(db.InspectionOrders.Where(o => o.Status != InspectionOrderStatus.Canceled),
                    r => r.InspectionOrderId, o => o.Id,
                    (r, o) => new { r.InspectionOrderId, o.OrderNo, r.InspectedAt, Value = r.MeasuredValue!.Value })
                .ToListAsync(ct))
            .Where(r => (fromStart is null || r.InspectedAt >= fromStart)
                        && (toEnd is null || r.InspectedAt < toEnd))
            .ToList();

        var orderIds = results.Select(r => r.InspectionOrderId).Distinct().ToList();
        var specs = await db.InspectionOrders.AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .SelectMany(o => o.Items.Where(i => i.InspectionItemId == inspectionItemId))
            .Select(i => new { i.InspectionOrderId, i.LowerLimit, i.UpperLimit })
            .ToListAsync(ct);
        var specByOrder = specs.GroupBy(s => s.InspectionOrderId).ToDictionary(g => g.Key, g => g.First());

        var subgroups = results
            .GroupBy(r => (r.InspectionOrderId, r.OrderNo))
            .Select(g =>
            {
                var first = g.Min(r => r.InspectedAt);
                specByOrder.TryGetValue(g.Key.InspectionOrderId, out var spec);
                return new ControlChartSubgroup(g.Key.OrderNo, first, businessDate.GetBusinessDate(first),
                    g.Select(r => r.Value).ToList(), spec?.LowerLimit, spec?.UpperLimit);
            })
            .ToList();

        return ControlChartCalculator.Calculate(item.Id, item.Code, item.Name, subgroups);
    }
}
