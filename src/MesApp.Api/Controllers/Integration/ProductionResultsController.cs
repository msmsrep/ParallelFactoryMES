using MesApp.Api.Localization;
using MesApp.Core.Contracts.Integration;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers.Integration;

/// <summary>
/// 連携API の生産実績の取得（Spec.md 3.8 連携API。docs-dev/IntegrationApiTasks.md INT-03。B-40-10 → ERP）。
/// <para>
/// ERP が完成実績を<b>取りに来る</b>形にする（MES から送り出す仕組みを持たずに済む。ERP 側は定期実行するだけ）。
/// 実績と訂正をそれぞれ ID の昇順で「前回の続きから」返す。日時で区切らないのは、記録の時刻が前後しても
/// （過去日の実績のCSV取込など）取りこぼさないため。参照は認証済みなら誰でも（既存の参照系と同じ）
/// </para>
/// </summary>
[ApiController]
[Route("api/integration/v1")]
[Authorize(AuthenticationSchemes = ApiKeyAuthentication.IntegrationSchemes)]
public class ProductionResultsController(MesAppDbContext db) : ControllerBase
{
    public const int DefaultLimit = 500;
    public const int MaxLimit = 5000;

    /// <summary>生産実績を、afterId より後から ID の昇順で返す（値は訂正後の今の値）</summary>
    [HttpGet("production-results")]
    public async Task<ActionResult<IntegrationFeed<ProductionResultResponse>>> ProductionResults(
        [FromQuery] long afterId = 0, [FromQuery] int limit = DefaultLimit, CancellationToken ct = default)
    {
        if (Validate(afterId, limit) is { } invalid)
        {
            return invalid;
        }
        var rows = await db.ProductionRecords.AsNoTracking()
            .Where(r => r.Id > afterId)
            .OrderBy(r => r.Id)
            .Take(limit + 1)
            .Select(r => new
            {
                r.Id,
                r.WorkOrder!.ManufacturingOrder!.OrderNo,
                r.WorkOrder.ManufacturingOrder.OrderType,
                r.WorkOrder.WorkOrderNo,
                r.WorkOrder.RoutingSequence,
                r.WorkOrder.Status,
                ProductCode = r.WorkOrder.Product!.Code,
                ProcessCode = r.WorkOrder.Process!.Code,
                WorkCenterCode = r.WorkOrder.WorkCenter != null ? r.WorkOrder.WorkCenter.Code : null,
                EquipmentAssetNo = r.WorkOrder.AssignedEquipment != null ? r.WorkOrder.AssignedEquipment.AssetNo : null,
                r.GoodQuantity,
                r.DefectQuantity,
                r.ScrapQuantity,
                r.ReworkQuantity,
                r.StartedAt,
                r.EndedAt,
                OutputLotNumber = r.OutputLot != null ? r.OutputLot.LotNumber : null,
                // 入庫先は参照の関連を持たないので、ID で引く
                OutputLocationCode = db.Locations.Where(l => l.Id == r.OutputLocationId).Select(l => l.Code).FirstOrDefault(),
                ShiftCode = r.Shift != null ? r.Shift.Code : null,
                PerformedBy = r.PerformedBy!.UserName,
                r.ApprovedAt,
                r.CreatedAt,
            })
            .ToListAsync(ct);

        var items = rows.Take(limit).Select(r => new ProductionResultResponse(
            r.Id, r.OrderNo, r.OrderType.ToString(), r.WorkOrderNo, r.RoutingSequence, r.Status.ToString(),
            r.ProductCode, r.ProcessCode, r.WorkCenterCode, r.EquipmentAssetNo,
            r.GoodQuantity, r.DefectQuantity, r.ScrapQuantity, r.ReworkQuantity,
            r.StartedAt, r.EndedAt, r.OutputLotNumber, r.OutputLocationCode, r.ShiftCode,
            r.PerformedBy ?? string.Empty, r.ApprovedAt, r.CreatedAt)).ToList();
        return Feed(items, afterId, rows.Count > limit, i => i.Id);
    }

    /// <summary>生産実績の訂正を、afterId より後から ID の昇順で返す</summary>
    [HttpGet("production-result-corrections")]
    public async Task<ActionResult<IntegrationFeed<ProductionResultCorrectionResponse>>> Corrections(
        [FromQuery] long afterId = 0, [FromQuery] int limit = DefaultLimit, CancellationToken ct = default)
    {
        if (Validate(afterId, limit) is { } invalid)
        {
            return invalid;
        }
        var rows = await db.ProductionRecordCorrections.AsNoTracking()
            .Where(c => c.Id > afterId)
            .OrderBy(c => c.Id)
            .Take(limit + 1)
            .Select(c => new ProductionResultCorrectionResponse(
                c.Id, c.ProductionRecordId,
                c.WorkOrder!.ManufacturingOrder!.OrderNo, c.WorkOrder.RoutingSequence,
                c.BeforeGoodQuantity, c.BeforeDefectQuantity, c.BeforeScrapQuantity, c.BeforeReworkQuantity,
                c.AfterGoodQuantity, c.AfterDefectQuantity, c.AfterScrapQuantity, c.AfterReworkQuantity,
                c.Reason, c.CorrectedBy != null ? c.CorrectedBy.UserName : null, c.CorrectedAt))
            .ToListAsync(ct);
        return Feed([.. rows.Take(limit)], afterId, rows.Count > limit, i => i.Id);
    }

    private ActionResult? Validate(long afterId, int limit)
    {
        if (afterId < 0)
        {
            return this.BadRequestProblem(ApiText.T("afterId は0以上で指定してください。"));
        }
        if (limit is < 1 or > MaxLimit)
        {
            return this.BadRequestProblem(ApiText.T("limit は1以上{0}以下で指定してください。", MaxLimit));
        }
        return null;
    }

    private static IntegrationFeed<T> Feed<T>(List<T> items, long afterId, bool hasMore, Func<T, long> idOf) =>
        new(items, items.Count == 0 ? afterId : idOf(items[^1]), hasMore);
}
