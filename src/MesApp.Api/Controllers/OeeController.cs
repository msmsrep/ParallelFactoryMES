using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Maintenance;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 設備総合効率（OEE。Spec.md 3.5：E-20-30-03 設備パフォーマンス確認）。
/// 稼働サマリ（api/equipment-logs/summary）と分けているのは、あちらが稼働監視の素の時間区分で、
/// こちらが製造実績と突き合わせた評価指標だから（責務が違う）。期間は製造日（業務日付）基準。
/// </summary>
[ApiController]
[Route("api/oee")]
[Authorize]
public class OeeController(MesAppDbContext db, IBusinessDateService businessDate) : ControllerBase
{
    /// <summary>稼働区間（按分の計算に使う）</summary>
    private sealed record Span(DateTimeOffset Start, DateTimeOffset End, int? WorkOrderId);

    /// <summary>作業指示ごとの産出と理論サイクルタイム</summary>
    private sealed record WorkOrderOutput(
        decimal Good, decimal Defect, decimal StandardWorkMinutes, decimal TotalCoveredMinutes);

    [HttpGet]
    public async Task<ActionResult<List<EquipmentOeeRow>>> Summary(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken ct = default)
    {
        var fromStart = from is null ? (DateTimeOffset?)null : businessDate.GetRange(from.Value).Start;
        var toEnd = to is null ? (DateTimeOffset?)null : businessDate.GetRange(to.Value).End;

        // SQLiteはDateTimeOffsetの演算を翻訳できないため、集計は取り出してから行う。
        // 期間は稼働の開始時刻が属する製造日で振り分ける（稼働サマリと同じ規則）
        var logs = (await db.EquipmentLogs.AsNoTracking()
                .Where(l => l.EndedAt != null)
                .Select(l => new
                {
                    l.EquipmentId,
                    l.Equipment!.AssetNo,
                    EquipmentName = l.Equipment!.Name,
                    l.Status,
                    l.StartedAt,
                    EndedAt = l.EndedAt!.Value,
                    l.WorkOrderId,
                })
                .ToListAsync(ct))
            .Where(l => (fromStart is null || l.StartedAt >= fromStart)
                        && (toEnd is null || l.StartedAt < toEnd))
            .ToList();

        if (logs.Count == 0)
        {
            return new List<EquipmentOeeRow>();
        }

        // 設備×作業指示の「紐づいた稼働時間」。同じ設備の同じ時間帯に複数の作業指示が紐づくことがある
        // （まとめ処理。Spec.md 3.9）ため、重なりは按分して同じ時間を二重に数えない
        var coveredMinutes = new Dictionary<(int EquipmentId, int WorkOrderId), decimal>();
        foreach (var group in logs.Where(l => l.Status == EquipmentLogStatus.Running)
                     .GroupBy(l => l.EquipmentId))
        {
            var spans = group.Select(l => new Span(l.StartedAt, l.EndedAt, l.WorkOrderId)).ToList();
            foreach (var (span, minutes) in ApportionOverlaps(spans))
            {
                if (span.WorkOrderId is not int workOrderId)
                {
                    continue;
                }
                var key = (group.Key, workOrderId);
                coveredMinutes[key] = coveredMinutes.GetValueOrDefault(key) + minutes;
            }
        }

        var outputs = await LoadWorkOrderOutputsAsync(coveredMinutes, fromStart, toEnd, ct);

        var rows = logs
            .GroupBy(l => new { l.EquipmentId, l.AssetNo, l.EquipmentName })
            .OrderBy(g => g.Key.AssetNo)
            .Select(g =>
            {
                var load = g.Sum(l => (decimal)(l.EndedAt - l.StartedAt).TotalHours);
                var running = g.Where(l => l.Status == EquipmentLogStatus.Running)
                    .Sum(l => (decimal)(l.EndedAt - l.StartedAt).TotalHours);

                var covered = coveredMinutes
                    .Where(kv => kv.Key.EquipmentId == g.Key.EquipmentId)
                    .ToList();
                var coveredHours = covered.Sum(kv => kv.Value) / 60m;

                decimal produced = 0, good = 0, theoreticalMinutes = 0;
                foreach (var (key, minutes) in covered)
                {
                    if (!outputs.TryGetValue(key.WorkOrderId, out var output)
                        || output.TotalCoveredMinutes == 0)
                    {
                        continue;
                    }
                    // 1つの作業指示が複数の設備で動くことがあるため、産出は稼働時間の比で配分する
                    var share = minutes / output.TotalCoveredMinutes;
                    produced += (output.Good + output.Defect) * share;
                    good += output.Good * share;
                    theoreticalMinutes += output.StandardWorkMinutes * (output.Good + output.Defect) * share;
                }

                decimal? availability = load == 0 ? null : Math.Round(running / load * 100, 2);
                decimal? coverage = running == 0 ? null : Math.Round(coveredHours / running * 100, 2);
                // 算出できないものは0にせずnullのままにする（記録漏れを設備の悪い評価に化けさせない）
                decimal? performance = coveredHours == 0
                    ? null
                    : Math.Round(theoreticalMinutes / (coveredHours * 60m) * 100, 2);
                decimal? quality = produced == 0 ? null : Math.Round(good / produced * 100, 2);
                decimal? oee = availability is null || performance is null || quality is null
                    ? null
                    : Math.Round(availability.Value * performance.Value * quality.Value / 10000m, 2);

                return new EquipmentOeeRow(
                    g.Key.EquipmentId, g.Key.AssetNo, g.Key.EquipmentName,
                    Math.Round(load, 2), Math.Round(running, 2), availability,
                    Math.Round(coveredHours, 2), coverage,
                    Math.Round(produced, 2), Math.Round(good, 2),
                    performance, quality, oee);
            })
            .ToList();

        return rows;
    }

    /// <summary>
    /// 作業指示ごとの産出数・標準作業時間と、その作業指示に紐づいた全設備の稼働時間合計を返す。
    /// 合計は、複数設備で動いた作業指示の産出を設備へ配分するための分母になる
    /// </summary>
    private async Task<Dictionary<int, WorkOrderOutput>> LoadWorkOrderOutputsAsync(
        Dictionary<(int EquipmentId, int WorkOrderId), decimal> coveredMinutes,
        DateTimeOffset? fromStart, DateTimeOffset? toEnd, CancellationToken ct)
    {
        var workOrderIds = coveredMinutes.Keys.Select(k => k.WorkOrderId).Distinct().ToList();
        if (workOrderIds.Count == 0)
        {
            return [];
        }

        var records = (await db.ProductionRecords.AsNoTracking()
                .Where(r => workOrderIds.Contains(r.WorkOrderId))
                .Select(r => new { r.WorkOrderId, r.CreatedAt, r.GoodQuantity, r.DefectQuantity })
                .ToListAsync(ct))
            .Where(r => (fromStart is null || r.CreatedAt >= fromStart)
                        && (toEnd is null || r.CreatedAt < toEnd))
            .GroupBy(r => r.WorkOrderId)
            .ToDictionary(g => g.Key,
                g => (Good: g.Sum(r => r.GoodQuantity), Defect: g.Sum(r => r.DefectQuantity)));

        // 理論サイクルタイムは工順マスタの現在値ではなく、指図展開時に固定した標準作業時間（1個あたり）
        var standards = await db.WorkOrders.AsNoTracking()
            .Where(w => workOrderIds.Contains(w.Id))
            .Select(w => new { w.Id, w.StandardWorkMinutes })
            .ToDictionaryAsync(w => w.Id, w => w.StandardWorkMinutes, ct);

        return workOrderIds.ToDictionary(
            id => id,
            id =>
            {
                var output = records.GetValueOrDefault(id);
                var totalCovered = coveredMinutes
                    .Where(kv => kv.Key.WorkOrderId == id)
                    .Sum(kv => kv.Value);
                return new WorkOrderOutput(
                    output.Good, output.Defect, standards.GetValueOrDefault(id), totalCovered);
            });
    }

    /// <summary>
    /// 重なり合う稼働区間を按分する。境界で刻み、その時間帯に重なっている本数で割って配分する。
    /// 単純に足すと、同じ設備の同じ時間を作業指示の数だけ数えてしまう
    /// </summary>
    private static List<(Span Span, decimal Minutes)> ApportionOverlaps(List<Span> spans)
    {
        var boundaries = spans.SelectMany(s => new[] { s.Start, s.End })
            .Distinct()
            .OrderBy(t => t)
            .ToList();
        var result = spans.ToDictionary(s => s, _ => 0m);

        for (var i = 0; i < boundaries.Count - 1; i++)
        {
            var (start, end) = (boundaries[i], boundaries[i + 1]);
            var covering = spans.Where(s => s.Start <= start && s.End >= end).ToList();
            if (covering.Count == 0)
            {
                continue;
            }
            var share = (decimal)(end - start).TotalMinutes / covering.Count;
            foreach (var span in covering)
            {
                result[span] += share;
            }
        }

        return result.Select(kv => (kv.Key, kv.Value)).ToList();
    }
}
