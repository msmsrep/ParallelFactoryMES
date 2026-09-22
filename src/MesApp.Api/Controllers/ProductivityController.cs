using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Production;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 生産性モニタリング（Spec.md 3.2：B-60-10-05 歩留まり・直行率・標準時間予実）。
/// 期間は製造日（業務日付）基準。
/// </summary>
[ApiController]
[Route("api/productivity")]
[Authorize]
public class ProductivityController(MesAppDbContext db, IBusinessDateService businessDate) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ProductivitySummaryResponse>> Summary(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken ct = default)
    {
        // SQLiteはDateTimeOffsetの比較を翻訳できないため、期間フィルタは取り出してから行う
        var fromStart = from is null ? (DateTimeOffset?)null : businessDate.GetRange(from.Value).Start;
        var toEnd = to is null ? (DateTimeOffset?)null : businessDate.GetRange(to.Value).End;

        var records = (await db.ProductionRecords.AsNoTracking()
                .Select(r => new
                {
                    // 開始時刻が属する製造日で振り分ける（Spec.md 3.9。登録時刻だと朝に登録した夜勤の実績が翌日へずれる）
                    r.StartedAt,
                    r.WorkOrderId,
                    r.GoodQuantity,
                    r.DefectQuantity,
                    ProductCode = r.WorkOrder!.Product!.Code,
                    ProcessCode = r.WorkOrder!.Process!.Code,
                    OrderType = r.WorkOrder!.ManufacturingOrder!.OrderType,
                })
                .ToListAsync(ct))
            .Where(r => (fromStart is null || r.StartedAt >= fromStart)
                        && (toEnd is null || r.StartedAt < toEnd))
            .ToList();

        ProductivityRow ToRow(string key, IEnumerable<decimal> good, IEnumerable<decimal> defect,
            IEnumerable<decimal> reworkGood)
        {
            var g = good.Sum();
            var d = defect.Sum();
            var r = reworkGood.Sum();
            var produced = g + d;
            return new ProductivityRow(key, g, d, r,
                produced == 0 ? 0 : Math.Round(g / produced * 100, 2),
                produced == 0 ? 0 : Math.Round((g + r) / produced * 100, 2));
        }

        // リワーク指図の産出は分母に入れない（同じ材料を2度数えることになるため）。
        // 救済された良品として歩留まりの分子にだけ足す
        static bool IsRework(ManufacturingOrderType type) => type == ManufacturingOrderType.Rework;

        ProductivityRow Aggregate(string key, IReadOnlyCollection<
            (ManufacturingOrderType OrderType, decimal GoodQuantity, decimal DefectQuantity)> rows) =>
            ToRow(key,
                rows.Where(x => !IsRework(x.OrderType)).Select(x => x.GoodQuantity),
                rows.Where(x => !IsRework(x.OrderType)).Select(x => x.DefectQuantity),
                rows.Where(x => IsRework(x.OrderType)).Select(x => x.GoodQuantity));

        var all = records
            .Select(x => (x.OrderType, x.GoodQuantity, x.DefectQuantity))
            .ToList();
        var total = Aggregate("合計", all);

        var byProduct = records
            .GroupBy(r => r.ProductCode)
            .OrderBy(g => g.Key)
            .Select(g => Aggregate(g.Key,
                g.Select(x => (x.OrderType, x.GoodQuantity, x.DefectQuantity)).ToList()))
            .ToList();

        var byProcess = records
            .GroupBy(r => r.ProcessCode)
            .OrderBy(g => g.Key)
            .Select(g => Aggregate(g.Key,
                g.Select(x => (x.OrderType, x.GoodQuantity, x.DefectQuantity)).ToList()))
            .ToList();

        var timeVariances = await BuildTimeVariancesAsync(
            records.Select(r => r.WorkOrderId).Distinct().ToList(), ct);

        return new ProductivitySummaryResponse(total, byProduct, byProcess, timeVariances);
    }

    /// <summary>
    /// 標準時間の予実（B-60-10-05）。予定は指図展開時に固定した工順の値で、
    /// 実績は直接作業時間（F-30-20-01）と段取り実績（B-20-50）の合計。
    /// </summary>
    private async Task<List<StandardTimeVarianceRow>> BuildTimeVariancesAsync(
        List<int> workOrderIds, CancellationToken ct)
    {
        if (workOrderIds.Count == 0)
        {
            return [];
        }

        var workOrders = await db.WorkOrders.AsNoTracking()
            .Where(w => workOrderIds.Contains(w.Id))
            .Select(w => new
            {
                w.Id,
                w.WorkOrderNo,
                ProductCode = w.Product!.Code,
                ProcessCode = w.Process!.Code,
                w.PlannedQuantity,
                w.StandardSetupMinutes,
                w.StandardWorkMinutes,
            })
            .ToListAsync(ct);

        // 所要時間の算出はSQLiteに載らないため、時刻のまま取り出してから分に直す
        var directTimes = (await db.WorkTimeRecords.AsNoTracking()
                .Where(t => t.Type == WorkTimeType.Direct
                            && t.WorkOrderId != null && workOrderIds.Contains(t.WorkOrderId.Value)
                            && t.EndedAt != null)
                .Select(t => new { WorkOrderId = t.WorkOrderId!.Value, t.StartedAt, EndedAt = t.EndedAt!.Value })
                .ToListAsync(ct))
            .GroupBy(t => t.WorkOrderId)
            .ToDictionary(g => g.Key, g => g.Sum(t => (decimal)(t.EndedAt - t.StartedAt).TotalMinutes));

        var setupTimes = (await db.SetupRecords.AsNoTracking()
                .Where(s => workOrderIds.Contains(s.WorkOrderId) && s.EndedAt != null)
                .Select(s => new { s.WorkOrderId, s.StartedAt, EndedAt = s.EndedAt!.Value })
                .ToListAsync(ct))
            .GroupBy(s => s.WorkOrderId)
            .ToDictionary(g => g.Key, g => g.Sum(s => (decimal)(s.EndedAt - s.StartedAt).TotalMinutes));

        return workOrders
            .Select(w =>
            {
                // 標準作業時間は1個あたり、標準段取り時間は1回あたり（Spec.md 5.1）
                var planned = w.StandardSetupMinutes + w.StandardWorkMinutes * w.PlannedQuantity;
                var actual = Math.Round(
                    directTimes.GetValueOrDefault(w.Id) + setupTimes.GetValueOrDefault(w.Id), 2);
                return new StandardTimeVarianceRow(
                    w.Id, w.WorkOrderNo, w.ProductCode, w.ProcessCode, w.PlannedQuantity,
                    planned, actual,
                    planned == 0 ? null : Math.Round((actual - planned) / planned * 100, 2));
            })
            .OrderByDescending(r => r.VarianceRate ?? decimal.MinValue)
            .ThenBy(r => r.WorkOrderNo)
            .ToList();
    }
}
