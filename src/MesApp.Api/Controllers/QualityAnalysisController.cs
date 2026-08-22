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
