using MesApp.Api.Localization;
using MesApp.Api.Policies;
using MesApp.Core.Abstractions;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Dashboard;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// ダッシュボードの期間別（日・週・月）・軸別（工程・品目・直・ライン・作業区・設備）の集計
/// （Spec.md 3.8。B-60-10-05 生産性／C-40-10-05 不適合／E-20-10-03 稼働モニタリング）。
/// <para>
/// 生産実績も稼働ログも<b>開始時刻が属する製造日</b>で振り分ける（Spec.md 3.9。夜勤の日跨ぎを1つの製造日に寄せる。
/// 登録時刻で振り分けると、朝に登録した夜勤の実績が翌日へずれ、過去日の実績をCSVで入れると取込日に寄る）。
/// </para>
/// <para>
/// 稼働ログは作業指示との紐付けが任意なので、<b>工程・品目では稼働を絞らない</b>。
/// 紐付いた区間だけで数えると紐付けの記録漏れが稼働率に化けるため、その軸・絞り込みでは稼働を求めない（null）。
/// 設備は差立で決まり作業指示に固定されないため、設備軸では生産実績を求めない。
/// </para>
/// </summary>
public class DashboardService(MesAppDbContext db, IBusinessDateService businessDate)
{
    /// <summary>1回に返す区切りの上限（日次で約1年）</summary>
    public const int MaxPeriods = 400;

    public static string NoWorkCenter => ApiText.T("（作業区なし）");

    /// <summary>集計の絞り込み条件</summary>
    public record Filter(DateOnly From, DateOnly To, int? ProcessId, int? ProductId, int? WorkCenterId);

    private sealed record ProductionItem(
        DateOnly Date, decimal Good, decimal Defect,
        string ProcessKey, string ProcessLabel, string ProductKey, string ProductLabel,
        string? ShiftKey, int? WorkCenterId);

    private sealed record LogItem(
        DateOnly Date, int EquipmentId, string AssetNo, string EquipmentName, int? WorkCenterId,
        EquipmentLogStatus Status, decimal Hours);

    /// <summary>期間内の区切りの数（上限の判定用）</summary>
    public static int CountPeriods(DateOnly from, DateOnly to, DashboardPeriodUnit unit)
    {
        var count = 0;
        for (var s = DashboardPeriods.Start(from, unit); s <= to && count <= MaxPeriods; s = DashboardPeriods.Shift(s, unit, 1))
        {
            count++;
        }
        return count;
    }

    /// <summary>期間別の推移。データの無い区切りも行として返す（推移の欠けを0件と見分けるため）</summary>
    public async Task<DashboardSummaryResponse> GetTrendAsync(Filter filter, DashboardPeriodUnit unit, CancellationToken ct)
    {
        var (production, logs, productionAvailable, utilizationAvailable) = await LoadAsync(filter, ct);

        var rows = new List<DashboardMetricsRow>();
        for (var start = DashboardPeriods.Start(filter.From, unit); start <= filter.To; start = DashboardPeriods.Shift(start, unit, 1))
        {
            var from = start < filter.From ? filter.From : start;
            var end = DashboardPeriods.End(start, unit);
            var to = end > filter.To ? filter.To : end;
            var label = unit switch
            {
                DashboardPeriodUnit.Month => $"{start:yyyy-MM}",
                DashboardPeriodUnit.Week => $"{from:MM/dd}〜{to:MM/dd}",
                _ => $"{start:MM/dd}({"日月火水木金土"[(int)start.DayOfWeek]})",
            };
            rows.Add(Aggregate($"{start:yyyy-MM-dd}", label, from, to,
                production.Where(p => p.Date >= from && p.Date <= to),
                logs.Where(l => l.Date >= from && l.Date <= to),
                productionAvailable, utilizationAvailable));
        }

        return new DashboardSummaryResponse(filter.From, filter.To,
            Aggregate("total", ApiText.T("合計"), filter.From, filter.To, production, logs, productionAvailable, utilizationAvailable),
            rows, productionAvailable, utilizationAvailable);
    }

    /// <summary>軸別の内訳（指定期間の合計を軸の値ごとに分ける）</summary>
    public async Task<DashboardSummaryResponse> GetBreakdownAsync(Filter filter, DashboardAxis axis, CancellationToken ct)
    {
        var (production, logs, productionAvailable, utilizationAvailable) = await LoadAsync(filter, ct);
        productionAvailable &= axis != DashboardAxis.Equipment;
        utilizationAvailable &= axis is DashboardAxis.Line or DashboardAxis.WorkCenter or DashboardAxis.Equipment;

        var workCenters = await db.WorkCenters.AsNoTracking().ToListAsync(ct);
        var byId = workCenters.ToDictionary(w => w.Id);

        // ライン軸は最下段の作業区を祖先のライン段へまとめる（ラインの下に無い作業区はそのまま）
        (string Key, string Label) WorkCenterKey(int? id, bool toLine)
        {
            if (id is null || !byId.TryGetValue(id.Value, out var wc))
            {
                return (NoWorkCenter, NoWorkCenter);
            }
            if (toLine)
            {
                var cursor = wc;
                while (cursor.Level != WorkCenterLevel.Line && cursor.ParentId is { } parentId
                       && byId.TryGetValue(parentId, out var parent))
                {
                    cursor = parent;
                }
                if (cursor.Level == WorkCenterLevel.Line)
                {
                    wc = cursor;
                }
            }
            return (wc.Code, $"{wc.Code} {wc.Name}");
        }

        (string Key, string Label) ProductionKey(ProductionItem p) => axis switch
        {
            DashboardAxis.Process => (p.ProcessKey, p.ProcessLabel),
            DashboardAxis.Product => (p.ProductKey, p.ProductLabel),
            DashboardAxis.Shift => (p.ShiftKey ?? ShiftLabels.NoShift, p.ShiftKey ?? ShiftLabels.NoShift),
            DashboardAxis.Line => WorkCenterKey(p.WorkCenterId, toLine: true),
            _ => WorkCenterKey(p.WorkCenterId, toLine: false),
        };

        (string Key, string Label) LogKey(LogItem l) => axis switch
        {
            DashboardAxis.Equipment => (l.AssetNo, $"{l.AssetNo} {l.EquipmentName}"),
            DashboardAxis.Line => WorkCenterKey(l.WorkCenterId, toLine: true),
            _ => WorkCenterKey(l.WorkCenterId, toLine: false),
        };

        var productionGroups = productionAvailable
            ? production.GroupBy(ProductionKey).ToDictionary(g => g.Key, g => g.ToList())
            : [];
        var logGroups = utilizationAvailable
            ? logs.GroupBy(LogKey).ToDictionary(g => g.Key, g => g.ToList())
            : [];

        var rows = productionGroups.Keys.Union(logGroups.Keys)
            .OrderBy(k => k.Key == NoWorkCenter || k.Key == ShiftLabels.NoShift) // 「なし」は末尾
            .ThenBy(k => k.Key, StringComparer.Ordinal)
            .Select(k => Aggregate(k.Key, k.Label, null, null,
                productionGroups.GetValueOrDefault(k) ?? [],
                logGroups.GetValueOrDefault(k) ?? [],
                productionAvailable, utilizationAvailable))
            .ToList();

        return new DashboardSummaryResponse(filter.From, filter.To,
            Aggregate("total", ApiText.T("合計"), filter.From, filter.To, production, logs, productionAvailable, utilizationAvailable),
            rows, productionAvailable, utilizationAvailable);
    }

    private async Task<(List<ProductionItem> Production, List<LogItem> Logs, bool ProductionAvailable, bool UtilizationAvailable)>
        LoadAsync(Filter filter, CancellationToken ct)
    {
        HashSet<int>? workCenterIds = null;
        if (filter.WorkCenterId is { } workCenterId)
        {
            // 作業指示・設備が持つのは最下段なので、ラインや工場で絞るときは配下へ展開する
            var all = await db.WorkCenters.AsNoTracking().ToListAsync(ct);
            workCenterIds = WorkCenterHierarchyPolicy.SelfAndDescendantIds(workCenterId, all);
        }

        var productionQuery = db.ProductionRecords.AsNoTracking();
        if (filter.ProcessId is { } processId)
        {
            productionQuery = productionQuery.Where(r => r.WorkOrder!.ProcessId == processId);
        }
        if (filter.ProductId is { } productId)
        {
            productionQuery = productionQuery.Where(r => r.WorkOrder!.ProductId == productId);
        }
        if (workCenterIds is not null)
        {
            productionQuery = productionQuery.Where(r =>
                r.WorkOrder!.WorkCenterId != null && workCenterIds.Contains(r.WorkOrder.WorkCenterId.Value));
        }

        // SQLiteはDateTimeOffsetの比較を翻訳できないため、期間は取り出してから製造日で絞る
        var production = (await productionQuery
                .Select(r => new
                {
                    r.StartedAt,
                    r.GoodQuantity,
                    r.DefectQuantity,
                    ProcessCode = r.WorkOrder!.Process!.Code,
                    ProcessName = r.WorkOrder!.Process!.Name,
                    ProductCode = r.WorkOrder!.Product!.Code,
                    ProductName = r.WorkOrder!.Product!.Name,
                    // 直は記録時に固定した値。集計のたびに時刻から引き直さない（Spec.md 5.7）
                    ShiftLabel = r.Shift == null ? null : r.Shift.Code + " " + r.Shift.Name,
                    r.WorkOrder!.WorkCenterId,
                })
                .ToListAsync(ct))
            .Select(r => new ProductionItem(
                businessDate.GetBusinessDate(r.StartedAt), r.GoodQuantity, r.DefectQuantity,
                r.ProcessCode, $"{r.ProcessCode} {r.ProcessName}",
                r.ProductCode, $"{r.ProductCode} {r.ProductName}",
                r.ShiftLabel, r.WorkCenterId))
            .Where(p => p.Date >= filter.From && p.Date <= filter.To)
            .ToList();

        var utilizationAvailable = filter.ProcessId is null && filter.ProductId is null;
        List<LogItem> logs = [];
        if (utilizationAvailable)
        {
            var logQuery = db.EquipmentLogs.AsNoTracking().Where(l => l.EndedAt != null);
            if (workCenterIds is not null)
            {
                logQuery = logQuery.Where(l =>
                    l.Equipment!.WorkCenterId != null && workCenterIds.Contains(l.Equipment.WorkCenterId.Value));
            }
            logs = (await logQuery
                    .Select(l => new
                    {
                        l.EquipmentId,
                        l.Equipment!.AssetNo,
                        EquipmentName = l.Equipment!.Name,
                        l.Equipment!.WorkCenterId,
                        l.Status,
                        l.StartedAt,
                        EndedAt = l.EndedAt!.Value,
                    })
                    .ToListAsync(ct))
                .Select(l => new LogItem(
                    businessDate.GetBusinessDate(l.StartedAt), l.EquipmentId, l.AssetNo, l.EquipmentName,
                    l.WorkCenterId, l.Status, (decimal)(l.EndedAt - l.StartedAt).TotalHours))
                .Where(l => l.Date >= filter.From && l.Date <= filter.To)
                .ToList();
        }

        return (production, logs, true, utilizationAvailable);
    }

    private static DashboardMetricsRow Aggregate(
        string key, string label, DateOnly? from, DateOnly? to,
        IEnumerable<ProductionItem> production, IEnumerable<LogItem> logs,
        bool productionAvailable, bool utilizationAvailable)
    {
        decimal? good = null, defect = null, defectRate = null;
        if (productionAvailable)
        {
            var items = production.ToList();
            good = items.Sum(p => p.Good);
            defect = items.Sum(p => p.Defect);
            defectRate = good + defect == 0 ? null : Math.Round(defect.Value / (good.Value + defect.Value) * 100, 2);
        }

        decimal? running = null, recorded = null, utilization = null;
        int? failures = null;
        if (utilizationAvailable)
        {
            var items = logs.ToList();
            running = Math.Round(items.Where(l => l.Status == EquipmentLogStatus.Running).Sum(l => l.Hours), 2);
            recorded = Math.Round(items.Sum(l => l.Hours), 2);
            utilization = recorded == 0 ? null : Math.Round(running.Value / recorded.Value * 100, 2);
            failures = items.Count(l => l.Status == EquipmentLogStatus.Failure);
        }

        return new DashboardMetricsRow(key, label, from, to,
            good, defect, defectRate, running, recorded, utilization, failures);
    }
}
