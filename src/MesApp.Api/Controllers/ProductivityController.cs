using MesApp.Api.Localization;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Production;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 生産性モニタリング（Spec.md 3.2：B-60-10-05 歩留まり・直行率・標準時間予実・製造リードタイム）。
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
        var total = Aggregate(ApiText.T("合計"), all);

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
    /// 製造リードタイムの分布（B-60-10-05。ガイド 8.3.2 の納期実績の見える化）。
    /// <para>
    /// 着手・完了は状態履歴ではなく作業の記録の時刻で決める（実績CSVで後から取り込むと、
    /// 状態が変わった時刻は取込の時刻になり、実際の作業日から外れるため）。
    /// 着手＝最初の段取り・生産実績の開始、完了＝最後の生産実績の終了（終了が無ければ開始）。
    /// 対象は取消以外の作業指示がすべて完了・承認済みの指図で、完了の製造日で期間を絞る。
    /// リワーク指図は元の指図と同じ品物を2度数えることになるため含めない（歩留まりと同じ扱い）。
    /// </para>
    /// </summary>
    [HttpGet("lead-time")]
    public async Task<ActionResult<LeadTimeResponse>> LeadTime(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] int? productId = null,
        CancellationToken ct = default)
    {
        var orders = await db.ManufacturingOrders.AsNoTracking()
            .Where(o => o.OrderType != ManufacturingOrderType.Rework
                        && (productId == null || o.ProductId == productId)
                        && o.WorkOrders.Any(w => w.Status != WorkOrderStatus.Canceled)
                        && o.WorkOrders.All(w => w.Status == WorkOrderStatus.Canceled
                                                 || w.Status == WorkOrderStatus.Completed
                                                 || w.Status == WorkOrderStatus.Approved))
            .Select(o => new
            {
                o.Id, o.OrderNo, ProductCode = o.Product!.Code, ProductName = o.Product!.Name,
                o.Quantity, o.DueDate,
            })
            .ToListAsync(ct);
        var orderIds = orders.Select(o => o.Id).ToList();

        // 時刻の最小・最大はSQLiteでDateTimeOffsetの集計が翻訳できないため、取り出してから求める
        var records = await db.ProductionRecords.AsNoTracking()
            .Where(r => orderIds.Contains(r.WorkOrder!.ManufacturingOrderId))
            .Select(r => new { r.WorkOrder!.ManufacturingOrderId, r.StartedAt, r.EndedAt })
            .ToListAsync(ct);
        var setups = await db.SetupRecords.AsNoTracking()
            .Where(s => orderIds.Contains(s.WorkOrder!.ManufacturingOrderId))
            .Select(s => new { s.WorkOrder!.ManufacturingOrderId, s.StartedAt })
            .ToListAsync(ct);
        var recordsByOrder = records.ToLookup(r => r.ManufacturingOrderId);
        var setupStartByOrder = setups.GroupBy(s => s.ManufacturingOrderId)
            .ToDictionary(g => g.Key, g => g.Min(s => s.StartedAt));

        var rows = new List<LeadTimeOrderRow>();
        foreach (var order in orders)
        {
            var orderRecords = recordsByOrder[order.Id].ToList();
            if (orderRecords.Count == 0)
            {
                continue; // 実績の無い指図（数量0で完了扱いにしたもの等）は日数を決められない
            }
            var started = orderRecords.Min(r => r.StartedAt);
            if (setupStartByOrder.TryGetValue(order.Id, out var setupStart) && setupStart < started)
            {
                started = setupStart;
            }
            var completed = orderRecords.Max(r => r.EndedAt ?? r.StartedAt);
            var startedOn = businessDate.GetBusinessDate(started);
            var completedOn = businessDate.GetBusinessDate(completed);
            if ((from is not null && completedOn < from) || (to is not null && completedOn > to))
            {
                continue;
            }
            rows.Add(new LeadTimeOrderRow(order.Id, order.OrderNo, order.ProductCode, order.ProductName,
                order.Quantity, started, completed, startedOn, completedOn,
                completedOn.DayNumber - startedOn.DayNumber,
                Math.Round((decimal)(completed - started).TotalHours, 1),
                order.DueDate,
                order.DueDate is { } due ? completedOn.DayNumber - due.DayNumber : null,
                IsOutlier: false));
        }

        return LeadTimeStatistics(rows);
    }

    /// <summary>
    /// 標準時間の見直し候補（B-60-10-05。ガイド 8.3.2(4)）。
    /// <para>
    /// 工順の工程ごとに、期間内の作業指示の実績（1個あたり実作業時間・1回あたり段取り時間）の中央値を
    /// 工順マスタの現在値と比べ、件数が <paramref name="minSamples"/> 以上でずれが <paramref name="threshold"/>% 以上なら候補にする。
    /// 比べる相手は作業指示のスナップショットではなくマスタの現在値（見直す対象そのものであり、
    /// 期間中に改訂済みなら、改訂後の値に対してまだずれているかを見たい）。
    /// マスタは書き換えない——標準を実績に寄せるか、作業のほうを直すかは業務の判断。
    /// </para>
    /// <para>
    /// 作業時間を記録していない作業指示は0分として混ぜず、件数から外す（標準時間の予実と違い、
    /// ここで0分を混ぜると標準が過大に見えてしまう）。期間は生産実績の開始時刻の製造日。リワーク指図は含めない。
    /// </para>
    /// </summary>
    [HttpGet("standard-time-review")]
    public async Task<ActionResult<StandardTimeReviewResponse>> StandardTimeReview(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] decimal threshold = 20m,
        [FromQuery] int minSamples = 3,
        CancellationToken ct = default)
    {
        if (threshold <= 0 || minSamples < 1)
        {
            return this.BadRequestProblem(ApiText.T("しきい値は0より大きく、件数は1以上で指定してください。"));
        }
        var fromStart = from is null ? (DateTimeOffset?)null : businessDate.GetRange(from.Value).Start;
        var toEnd = to is null ? (DateTimeOffset?)null : businessDate.GetRange(to.Value).End;

        var produced = (await db.ProductionRecords.AsNoTracking()
                .Where(r => r.WorkOrder!.ManufacturingOrder!.OrderType != ManufacturingOrderType.Rework)
                .Select(r => new { r.WorkOrderId, r.StartedAt, Quantity = r.GoodQuantity + r.DefectQuantity })
                .ToListAsync(ct))
            .Where(r => (fromStart is null || r.StartedAt >= fromStart)
                        && (toEnd is null || r.StartedAt < toEnd))
            .GroupBy(r => r.WorkOrderId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));
        var workOrderIds = produced.Keys.ToList();

        var workOrders = await db.WorkOrders.AsNoTracking()
            .Where(w => workOrderIds.Contains(w.Id))
            .Select(w => new { w.Id, w.ProductId, w.ProcessId, w.RoutingSequence })
            .ToListAsync(ct);
        // 所要時間の算出はSQLiteに載らないため、時刻のまま取り出してから分に直す
        var directMinutes = (await db.WorkTimeRecords.AsNoTracking()
                .Where(t => t.Type == WorkTimeType.Direct && t.EndedAt != null
                            && t.WorkOrderId != null && workOrderIds.Contains(t.WorkOrderId.Value))
                .Select(t => new { WorkOrderId = t.WorkOrderId!.Value, t.StartedAt, EndedAt = t.EndedAt!.Value })
                .ToListAsync(ct))
            .GroupBy(t => t.WorkOrderId)
            .ToDictionary(g => g.Key, g => g.Sum(t => (decimal)(t.EndedAt - t.StartedAt).TotalMinutes));
        var setupMinutes = (await db.SetupRecords.AsNoTracking()
                .Where(s => s.EndedAt != null && workOrderIds.Contains(s.WorkOrderId))
                .Select(s => new { s.WorkOrderId, s.StartedAt, EndedAt = s.EndedAt!.Value })
                .ToListAsync(ct))
            .GroupBy(s => s.WorkOrderId)
            .ToDictionary(g => g.Key, g => g.Sum(s => (decimal)(s.EndedAt - s.StartedAt).TotalMinutes));

        var productIds = workOrders.Select(w => w.ProductId).Distinct().ToList();
        var routings = await db.Routings.AsNoTracking()
            .Where(r => productIds.Contains(r.ProductId))
            .Select(r => new
            {
                r.Id, r.ProductId, r.Sequence, r.ProcessId,
                ProductCode = r.Product!.Code, ProductName = r.Product!.Name,
                ProcessCode = r.Process!.Code, ProcessName = r.Process!.Name,
                r.StandardWorkMinutes, r.StandardSetupMinutes,
            })
            .ToListAsync(ct);

        static decimal? Median(List<decimal> values)
        {
            if (values.Count == 0)
            {
                return null;
            }
            values.Sort();
            var mid = values.Count / 2;
            return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2;
        }
        static decimal? Deviation(decimal? median, decimal standard) =>
            median is { } m && standard > 0 ? Math.Round((m - standard) / standard * 100, 1) : null;
        bool Off(int samples, decimal? median, decimal? deviation, decimal standard) =>
            samples >= minSamples && median is { } m
            && (deviation is { } d ? Math.Abs(d) >= threshold : standard == 0 && m > 0);

        var rows = routings
            .Select(r =>
            {
                // 工順が変わって工程順序と工程が一致しない作業指示は、この工程の実績として数えない
                var mine = workOrders
                    .Where(w => w.ProductId == r.ProductId && w.RoutingSequence == r.Sequence && w.ProcessId == r.ProcessId)
                    .ToList();
                var work = mine
                    .Where(w => directMinutes.GetValueOrDefault(w.Id) > 0 && produced[w.Id] > 0)
                    .Select(w => directMinutes[w.Id] / produced[w.Id])
                    .ToList();
                var setup = mine
                    .Where(w => setupMinutes.ContainsKey(w.Id))
                    .Select(w => setupMinutes[w.Id])
                    .ToList();
                var workMedian = Median(work);
                var setupMedian = Median(setup);
                var workDeviation = Deviation(workMedian, r.StandardWorkMinutes);
                var setupDeviation = Deviation(setupMedian, r.StandardSetupMinutes);
                return new StandardTimeReviewRow(r.Id, r.ProductCode, r.ProductName, r.Sequence,
                    r.ProcessCode, r.ProcessName,
                    r.StandardWorkMinutes, work.Count, workMedian is { } wm ? Math.Round(wm, 2) : null, workDeviation,
                    r.StandardSetupMinutes, setup.Count, setupMedian is { } sm ? Math.Round(sm, 2) : null, setupDeviation,
                    Off(work.Count, workMedian, workDeviation, r.StandardWorkMinutes)
                    || Off(setup.Count, setupMedian, setupDeviation, r.StandardSetupMinutes));
            })
            .Where(r => r.WorkSampleCount + r.SetupSampleCount > 0)
            .OrderByDescending(r => r.IsCandidate)
            .ThenByDescending(r => Math.Max(Math.Abs(r.WorkDeviationRate ?? 0), Math.Abs(r.SetupDeviationRate ?? 0)))
            .ThenBy(r => r.ProductCode).ThenBy(r => r.Sequence)
            .ToList();

        return new StandardTimeReviewResponse(threshold, minSamples, rows);
    }

    /// <summary>
    /// リードタイムの要約・度数分布・異常値（四分位は最近順位法。4件未満では異常値を判定しない。
    /// 四分位範囲は1日を下限にする）
    /// </summary>
    private static LeadTimeResponse LeadTimeStatistics(List<LeadTimeOrderRow> rows)
    {
        if (rows.Count == 0)
        {
            return new LeadTimeResponse(0, null, null, null, null, null, 0, 0, 0, [], []);
        }
        var days = rows.Select(r => r.LeadTimeDays).Order().ToList();
        int Rank(decimal p) => days[Math.Max(0, (int)Math.Ceiling(p * days.Count) - 1)];
        var median = days.Count % 2 == 1
            ? days[days.Count / 2]
            : (days[days.Count / 2 - 1] + days[days.Count / 2]) / 2m;
        // 日数は整数なので、四分位範囲が1日未満（多くの指図が同じ日数）でも1日として扱う。
        // 0のままだと、そろった日数から1日長いだけの指図まで異常値になる
        decimal? threshold = days.Count >= 4
            ? Rank(0.75m) + 1.5m * Math.Max(Rank(0.75m) - Rank(0.25m), 1)
            : null;

        var marked = rows
            .Select(r => r with { IsOutlier = threshold is { } t && r.LeadTimeDays > t })
            .OrderByDescending(r => r.LeadTimeDays).ThenByDescending(r => r.LeadTimeHours).ThenBy(r => r.OrderNo)
            .ToList();
        // 0日から最大日数まで、件数0の日も並べる（分布の切れ目が見えるように）
        var distribution = Enumerable.Range(0, days[^1] + 1)
            .Select(d => new LeadTimeBucket(d,
                rows.Count(r => r.LeadTimeDays == d),
                rows.Count(r => r.LeadTimeDays == d && r.DelayDays > 0)))
            .ToList();

        return new LeadTimeResponse(rows.Count,
            Math.Round((decimal)days.Average(), 2), median, Rank(0.9m), days[^1], threshold,
            OnTimeCount: rows.Count(r => r.DelayDays <= 0),
            LateCount: rows.Count(r => r.DelayDays > 0),
            NoDueDateCount: rows.Count(r => r.DelayDays is null),
            distribution, marked);
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
