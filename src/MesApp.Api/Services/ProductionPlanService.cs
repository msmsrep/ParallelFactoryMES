using MesApp.Api.Policies;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Planning;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// 生産計画と工程ごとの出来高の突き合わせ（Spec.md 3.1。A-30-10-01 生産進捗管理モニタリング）。
/// 予実の集計はここだけで行う（画面では数えない。Spec.md 7.5）。
/// <para>
/// 実績の数え方はダッシュボード・生産性とそろえる：生産実績の良品数を<b>開始時刻が属する製造日</b>で振り分け
/// （Spec.md 3.9）、<b>リワーク指図の産出は数えない</b>（B-60-10-05 と同じ規則。同じ材料を2度数えないため）。
/// </para>
/// <para>
/// 作業区で絞るときは、計画・実績とも「作業区が配下にあるもの」だけに揃える。作業区なしの計画も、
/// 作業区なしの作業指示の実績も外す（片側だけが減ると達成率がずれるため）。実績側の作業区は作業指示に展開時点で固定した値。
/// </para>
/// </summary>
public class ProductionPlanService(MesAppDbContext db, IBusinessDateService businessDate)
{
    /// <summary>期間は製造日の from〜to（両端を含む）。作業区は上位の段を指定すると配下へ展開する</summary>
    public record Filter(DateOnly From, DateOnly To, int? ProductId, int? ProcessId, int? WorkCenterId);

    private record Item(DateOnly Date, int ProductId, string ProductCode, string ProductName,
        int ProcessId, string ProcessCode, string ProcessName, decimal Quantity);

    public async Task<List<ProductionPlanActualRow>> GetPlanActualAsync(Filter filter, CancellationToken ct)
    {
        HashSet<int>? workCenterIds = null;
        if (filter.WorkCenterId is { } workCenterId)
        {
            var all = await db.WorkCenters.AsNoTracking().ToListAsync(ct);
            workCenterIds = WorkCenterHierarchyPolicy.SelfAndDescendantIds(workCenterId, all);
        }

        var plans = await LoadPlansAsync(filter, workCenterIds, ct);
        var actuals = await LoadActualsAsync(filter, workCenterIds, ct);

        // 計画だけの日・実績だけの日も1行にする（無い側は 0）
        var planned = plans.GroupBy(p => (p.Date, p.ProductId, p.ProcessId))
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Quantity));
        var actual = actuals.GroupBy(a => (a.Date, a.ProductId, a.ProcessId))
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Quantity));

        return plans.Concat(actuals)
            .DistinctBy(x => (x.Date, x.ProductId, x.ProcessId))
            .OrderBy(x => x.Date)
            .ThenBy(x => x.ProductCode, StringComparer.Ordinal)
            .ThenBy(x => x.ProcessCode, StringComparer.Ordinal)
            .Select(x =>
            {
                var key = (x.Date, x.ProductId, x.ProcessId);
                var p = planned.GetValueOrDefault(key);
                var a = actual.GetValueOrDefault(key);
                return new ProductionPlanActualRow(x.Date,
                    x.ProductId, x.ProductCode, x.ProductName,
                    x.ProcessId, x.ProcessCode, x.ProcessName,
                    p, a, p == 0 ? null : Math.Round(a / p * 100, 2));
            })
            .ToList();
    }

    private async Task<List<Item>> LoadPlansAsync(Filter filter, HashSet<int>? workCenterIds, CancellationToken ct)
    {
        var query = db.ProductionPlans.AsNoTracking()
            .Where(p => p.BusinessDate >= filter.From && p.BusinessDate <= filter.To);
        if (filter.ProductId is { } productId)
        {
            query = query.Where(p => p.ProductId == productId);
        }
        if (filter.ProcessId is { } processId)
        {
            query = query.Where(p => p.ProcessId == processId);
        }
        if (workCenterIds is not null)
        {
            query = query.Where(p => p.WorkCenterId != null && workCenterIds.Contains(p.WorkCenterId.Value));
        }
        return await query
            .Select(p => new Item(p.BusinessDate,
                p.ProductId, p.Product!.Code, p.Product.Name,
                p.ProcessId, p.Process!.Code, p.Process.Name, p.PlannedQuantity))
            .ToListAsync(ct);
    }

    private async Task<List<Item>> LoadActualsAsync(Filter filter, HashSet<int>? workCenterIds, CancellationToken ct)
    {
        var query = db.ProductionRecords.AsNoTracking()
            .Where(r => r.WorkOrder!.ManufacturingOrder!.OrderType != ManufacturingOrderType.Rework);
        if (filter.ProductId is { } productId)
        {
            query = query.Where(r => r.WorkOrder!.ProductId == productId);
        }
        if (filter.ProcessId is { } processId)
        {
            query = query.Where(r => r.WorkOrder!.ProcessId == processId);
        }
        if (workCenterIds is not null)
        {
            query = query.Where(r =>
                r.WorkOrder!.WorkCenterId != null && workCenterIds.Contains(r.WorkOrder.WorkCenterId.Value));
        }

        // SQLiteはDateTimeOffsetの比較を翻訳できないため、期間は取り出してから半開区間で絞る
        var start = businessDate.GetRange(filter.From).Start;
        var end = businessDate.GetRange(filter.To).End;
        return (await query
                .Select(r => new
                {
                    r.StartedAt,
                    r.GoodQuantity,
                    r.WorkOrder!.ProductId,
                    ProductCode = r.WorkOrder.Product!.Code,
                    ProductName = r.WorkOrder.Product.Name,
                    r.WorkOrder.ProcessId,
                    ProcessCode = r.WorkOrder.Process!.Code,
                    ProcessName = r.WorkOrder.Process.Name,
                })
                .ToListAsync(ct))
            .Where(r => r.StartedAt >= start && r.StartedAt < end)
            .Select(r => new Item(businessDate.GetBusinessDate(r.StartedAt),
                r.ProductId, r.ProductCode, r.ProductName,
                r.ProcessId, r.ProcessCode, r.ProcessName, r.GoodQuantity))
            .ToList();
    }
}
