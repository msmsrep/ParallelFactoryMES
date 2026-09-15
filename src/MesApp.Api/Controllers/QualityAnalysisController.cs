using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Quality;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 品質分析（Spec.md 3.3：C-40-10 不良項目別・工程別・期間別の集計、不適合発生状況のモニタリング）。
/// 期間は製造日（業務日付）基準。SPC・管理図（C-50-10）は将来拡張。
/// </summary>
[ApiController]
[Route("api/quality/summary")]
[Authorize]
public class QualityAnalysisController(MesAppDbContext db, IBusinessDateService businessDate) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<QualitySummaryResponse>> Summary(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken ct = default)
    {
        // SQLiteはDateTimeOffsetの比較を翻訳できないため、期間フィルタはクライアント側で行う
        var fromStart = from is null ? (DateTimeOffset?)null : businessDate.GetRange(from.Value).Start;
        var toEnd = to is null ? (DateTimeOffset?)null : businessDate.GetRange(to.Value).End;

        var records = (await db.ProductionRecords.AsNoTracking()
                .Select(r => new
                {
                    r.CreatedAt,
                    r.GoodQuantity,
                    r.DefectQuantity,
                    r.ScrapQuantity,
                    r.ReworkQuantity,
                    ProductCode = r.WorkOrder!.Product!.Code,
                    ProcessCode = r.WorkOrder!.Process!.Code,
                    // 直は記録時に固定した値。集計のたびに時刻から引き直さない（Spec.md 5.7）
                    ShiftLabel = r.Shift == null ? null : r.Shift.Code + " " + r.Shift.Name,
                })
                .ToListAsync(ct))
            .Where(r => (fromStart is null || r.CreatedAt >= fromStart)
                        && (toEnd is null || r.CreatedAt < toEnd))
            .ToList();

        var byProduct = records
            .GroupBy(r => r.ProductCode)
            .OrderBy(g => g.Key)
            .Select(g => ToRow(g.Key, g.Sum(r => r.GoodQuantity), g.Sum(r => r.DefectQuantity),
                g.Sum(r => r.ScrapQuantity), g.Sum(r => r.ReworkQuantity)))
            .ToList();
        var byProcess = records
            .GroupBy(r => r.ProcessCode)
            .OrderBy(g => g.Key)
            .Select(g => ToRow(g.Key, g.Sum(r => r.GoodQuantity), g.Sum(r => r.DefectQuantity),
                g.Sum(r => r.ScrapQuantity), g.Sum(r => r.ReworkQuantity)))
            .ToList();
        // 直別（C-40-10-03）。3.9節の製造日が夜勤を前提にしているので、
        // 昼勤と夜勤で不良率が違わないかを見られるようにする
        var byShift = records
            .GroupBy(r => r.ShiftLabel ?? "（直なし）")
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => ToRow(g.Key, g.Sum(r => r.GoodQuantity), g.Sum(r => r.DefectQuantity),
                g.Sum(r => r.ScrapQuantity), g.Sum(r => r.ReworkQuantity)))
            .ToList();

        // 不良理由別の集計（C-40-10-01）。改善活動の対象を選ぶための構成比も返す
        var defects = (await db.ProductionDefects.AsNoTracking()
                .Select(d => new
                {
                    d.ProductionRecord!.CreatedAt,
                    d.Quantity,
                    Code = d.DefectReason!.Code,
                    Name = d.DefectReason!.Name,
                    d.DefectReason!.Category,
                })
                .ToListAsync(ct))
            .Where(d => (fromStart is null || d.CreatedAt >= fromStart)
                        && (toEnd is null || d.CreatedAt < toEnd))
            .ToList();
        var defectTotal = defects.Sum(d => d.Quantity);
        var byDefectReason = defects
            .GroupBy(d => (d.Code, d.Name, d.Category))
            .Select(g => new DefectReasonSummaryRow(
                g.Key.Code, g.Key.Name, g.Key.Category,
                g.Sum(d => d.Quantity),
                defectTotal == 0 ? 0 : Math.Round(g.Sum(d => d.Quantity) / defectTotal * 100, 2)))
            .OrderByDescending(r => r.Quantity).ThenBy(r => r.Code)
            .ToList();

        var nonconformances = (await db.NonconformanceReports.AsNoTracking()
                .Select(n => new { n.CreatedAt, n.CauseCategory, n.Status })
                .ToListAsync(ct))
            .Where(n => (fromStart is null || n.CreatedAt >= fromStart)
                        && (toEnd is null || n.CreatedAt < toEnd))
            .ToList();
        var byCause = nonconformances
            .GroupBy(n => n.CauseCategory ?? "（未分類）")
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());

        var inspections = (await db.InspectionOrders.AsNoTracking()
                .Where(i => i.OverallJudgment != null)
                .Select(i => new { i.CreatedAt, i.OverallJudgment })
                .ToListAsync(ct))
            .Where(i => (fromStart is null || i.CreatedAt >= fromStart)
                        && (toEnd is null || i.CreatedAt < toEnd))
            .ToList();

        return new QualitySummaryResponse(
            byProduct,
            byProcess,
            byShift,
            byDefectReason,
            byCause,
            inspections.Count(i => i.OverallJudgment == InspectionJudgment.Pass),
            inspections.Count(i => i.OverallJudgment == InspectionJudgment.Fail),
            nonconformances.Count(n => n.Status != NonconformanceStatus.Closed));
    }

    private static DefectSummaryRow ToRow(
        string key, decimal good, decimal defect, decimal scrap, decimal rework) =>
        new(key, good, defect, scrap, rework,
            good + defect == 0 ? 0 : Math.Round(defect / (good + defect) * 100, 2));
}
