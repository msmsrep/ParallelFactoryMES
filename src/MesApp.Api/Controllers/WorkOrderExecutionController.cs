using System.Security.Claims;
using MesApp.Api.Policies;
using MesApp.Api.Services;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Execution;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 作業指示の実行系API（Spec.md 3.2 製造実行）：着手（B-30-30-01）、段取り実績（B-20-50、B-40-40）、
/// チェックリスト実施（B-30-10）、部材投入（B-30-20）、生産実績＋バックフラッシュ（B-40-10）、
/// 製造条件データ（B-30-30-04）、製造完了承認（B-40-10-10）。
/// 記録系は現場作業者を含む認証済み全ユーザー、承認は生産管理ロールのみ。
/// </summary>
[ApiController]
[Route("api/work-orders/{id:int}")]
[Authorize]
public class WorkOrderExecutionController(
    MesAppDbContext db,
    WorkOrderExecutionService execution) : ControllerBase
{
    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>着手（B-30-30-01）。未配布でも着手可能（差立を省略する小規模運用を許容）</summary>
    [HttpPost("start")]
    [Authorize(Roles = MesRoleGroups.ShopFloorRecord)]
    public async Task<IActionResult> Start(int id, CancellationToken ct)
    {
        var outcome = await execution.StartAsync(id, CurrentUserId, ct);
        return outcome.Failed ? ToProblem(outcome) : NoContent();
    }

    // ---- 段取り実績（B-20-50 前段取り／B-40-40 後段取り）----

    [HttpGet("setup-records")]
    public async Task<ActionResult<List<SetupRecordResponse>>> GetSetupRecords(int id, CancellationToken ct)
    {
        if (!await db.WorkOrders.AnyAsync(w => w.Id == id, ct))
        {
            return NotFound();
        }
        return await db.SetupRecords.AsNoTracking()
            .Where(r => r.WorkOrderId == id)
            .OrderBy(r => r.Id)
            .Select(r => new SetupRecordResponse(
                r.Id, r.WorkOrderId, r.Type, r.StartedAt, r.EndedAt,
                r.PerformedByUserId, r.PerformedBy!.DisplayName, r.AbnormalityNote))
            .ToListAsync(ct);
    }

    [HttpPost("setup-records")]
    [Authorize(Roles = MesRoleGroups.ShopFloorRecord)]
    public async Task<ActionResult<SetupRecordResponse>> AddSetupRecord(
        int id, SetupRecordRequest request, CancellationToken ct)
    {
        var outcome = await execution.AddSetupRecordAsync(id, request, CurrentUserId!, ct);
        if (outcome.Value is not { } record)
        {
            return ToProblem(outcome);
        }
        var name = await db.Users.Where(u => u.Id == record.PerformedByUserId)
            .Select(u => u.DisplayName).FirstOrDefaultAsync(ct);
        return new SetupRecordResponse(record.Id, id, record.Type, record.StartedAt, record.EndedAt,
            record.PerformedByUserId, name, record.AbnormalityNote);
    }

    // ---- チェックリスト実施（B-30-10）----

    [HttpGet("checklist-records")]
    public async Task<ActionResult<List<ChecklistRecordResponse>>> GetChecklistRecords(int id, CancellationToken ct)
    {
        if (!await db.WorkOrders.AnyAsync(w => w.Id == id, ct))
        {
            return NotFound();
        }
        var records = await db.ChecklistRecords.AsNoTracking()
            .Include(r => r.Checklist)
            .Include(r => r.PerformedBy)
            .Include(r => r.Results).ThenInclude(x => x.ChecklistItem)
            .Where(r => r.WorkOrderId == id)
            .OrderBy(r => r.Id)
            .ToListAsync(ct);
        return records.Select(ToChecklistResponse).ToList();
    }

    /// <summary>チェックリスト実施記録。必須項目が未チェックの場合は登録を拒否する</summary>
    [HttpPost("checklist-records")]
    [Authorize(Roles = MesRoleGroups.ShopFloorRecord)]
    public async Task<ActionResult<ChecklistRecordResponse>> AddChecklistRecord(
        int id, ChecklistRecordRequest request, CancellationToken ct)
    {
        var outcome = await execution.AddChecklistRecordAsync(id, request, CurrentUserId!, ct);
        if (outcome.Value is not { } record)
        {
            return ToProblem(outcome);
        }
        var saved = await db.ChecklistRecords.AsNoTracking()
            .Include(r => r.Checklist)
            .Include(r => r.PerformedBy)
            .Include(r => r.Results).ThenInclude(x => x.ChecklistItem)
            .FirstAsync(r => r.Id == record.Id, ct);
        return ToChecklistResponse(saved);
    }

    // ---- 部材投入（B-30-20-01 手動記録。在庫の払出と投入実績を同時記録）----

    [HttpGet("consumptions")]
    public async Task<ActionResult<List<ConsumptionResponse>>> GetConsumptions(int id, CancellationToken ct)
    {
        if (!await db.WorkOrders.AnyAsync(w => w.Id == id, ct))
        {
            return NotFound();
        }
        return await db.MaterialConsumptions.AsNoTracking()
            .Where(c => c.WorkOrderId == id)
            .OrderBy(c => c.Id)
            .Select(c => new ConsumptionResponse(
                c.Id, c.WorkOrderId, c.ProductId, c.Product!.Code, c.Product!.Name,
                c.LotId, c.Lot!.LotNumber, c.LocationId, c.Quantity, c.ConsumedAt, c.Method,
                c.IsSubstitute, c.SubstituteReason))
            .ToListAsync(ct);
    }

    [HttpPost("consumptions")]
    [Authorize(Roles = MesRoleGroups.ShopFloorRecord)]
    public async Task<ActionResult<ConsumptionResponse>> AddConsumption(
        int id, ConsumptionRequest request, CancellationToken ct)
    {
        var outcome = await execution.AddConsumptionAsync(id, request, CurrentUserId, ct);
        if (outcome.Value is not { } consumption)
        {
            return ToProblem(outcome);
        }
        var (product, lot) = (consumption.Product!, consumption.Lot!);
        return new ConsumptionResponse(consumption.Id, id, product.Id, product.Code, product.Name,
            lot.Id, lot.LotNumber, consumption.LocationId, consumption.Quantity,
            consumption.ConsumedAt, consumption.Method,
            consumption.IsSubstitute, consumption.SubstituteReason);
    }

    // ---- 生産実績（B-40-10-01 出来高、B-40-10-02 在庫計上、B-40-10-09 バックフラッシュ）----

    [HttpGet("production-records")]
    public async Task<ActionResult<List<ProductionRecordResponse>>> GetProductionRecords(int id, CancellationToken ct)
    {
        if (!await db.WorkOrders.AnyAsync(w => w.Id == id, ct))
        {
            return NotFound();
        }
        return await db.ProductionRecords.AsNoTracking()
            .Where(r => r.WorkOrderId == id)
            .OrderBy(r => r.Id)
            .Select(r => new ProductionRecordResponse(
                r.Id, r.WorkOrderId, r.WorkOrder!.WorkOrderNo,
                r.PerformedByUserId, r.PerformedBy!.DisplayName,
                r.GoodQuantity, r.DefectQuantity, r.ScrapQuantity, r.ReworkQuantity,
                r.StartedAt, r.EndedAt,
                r.OutputLotId, r.OutputLot!.LotNumber, r.OutputLocationId,
                r.ApprovedByUserId, r.ApprovedAt,
                r.Defects.Select(d => new ProductionDefectResponse(
                    d.DefectReasonId, d.DefectReason!.Code, d.DefectReason!.Name, d.Quantity, d.Note)).ToList(),
                r.ShiftId, r.Shift!.Code, r.Shift!.Name))
            .ToListAsync(ct);
    }

    /// <summary>
    /// 実績入力。作業指示は完了状態になる（B-30-30-06）。同じ作業指示へ複数回呼べる（分割報告）。
    /// 判定と在庫計上・バックフラッシュは <see cref="WorkOrderExecutionService.AddProductionRecordAsync"/> を参照
    /// </summary>
    [HttpPost("production-records")]
    [Authorize(Roles = MesRoleGroups.ShopFloorRecord)]
    public async Task<ActionResult<ProductionRecordResponse>> AddProductionRecord(
        int id, ProductionRecordRequest request, CancellationToken ct)
    {
        var outcome = await execution.AddProductionRecordAsync(id, request, CurrentUserId!, ct);
        if (outcome.Value is not var (record, workOrder, outputLot, shift))
        {
            return ToProblem(outcome);
        }
        var name = await db.Users.Where(u => u.Id == record.PerformedByUserId)
            .Select(u => u.DisplayName).FirstOrDefaultAsync(ct);
        return new ProductionRecordResponse(record.Id, id, workOrder.WorkOrderNo,
            record.PerformedByUserId, name, record.GoodQuantity, record.DefectQuantity,
            record.ScrapQuantity, record.ReworkQuantity,
            record.StartedAt, record.EndedAt, record.OutputLotId, outputLot?.LotNumber,
            record.OutputLocationId, null, null,
            await db.ProductionDefects.AsNoTracking()
                .Where(d => d.ProductionRecordId == record.Id)
                .Select(d => new ProductionDefectResponse(
                    d.DefectReasonId, d.DefectReason!.Code, d.DefectReason!.Name, d.Quantity, d.Note))
                .ToListAsync(ct),
            shift?.Id, shift?.Code, shift?.Name);
    }

    /// <summary>製造完了承認（B-40-10-10）。全作業指示が承認/取消済みになると指図も完了になる</summary>
    [HttpPost("approve")]
    [Authorize(Roles = MesRoleGroups.ProductionManage)]
    public async Task<IActionResult> Approve(int id, CancellationToken ct)
    {
        var outcome = await execution.ApproveAsync(id, CurrentUserId, ct);
        return outcome.Failed ? ToProblem(outcome) : NoContent();
    }

    // ---- 製造条件データ（B-30-30-04。手入力/CSV由来の値を記録）----

    [HttpGet("data-records")]
    public async Task<ActionResult<List<DataRecordResponse>>> GetDataRecords(int id, CancellationToken ct)
    {
        if (!await db.WorkOrders.AnyAsync(w => w.Id == id, ct))
        {
            return NotFound();
        }
        return await db.ProductionDataRecords.AsNoTracking()
            .Where(r => r.WorkOrderId == id)
            .OrderBy(r => r.Id)
            .Select(r => new DataRecordResponse(
                r.Id, r.WorkOrderId, r.Item, r.Value, r.RecordedAt,
                r.WorkOrderControlItemId, r.NumericValue, r.IsDeviation,
                r.WorkOrderControlItem != null ? r.WorkOrderControlItem.TargetValue : null,
                r.WorkOrderControlItem != null ? r.WorkOrderControlItem.LowerLimit : null,
                r.WorkOrderControlItem != null ? r.WorkOrderControlItem.UpperLimit : null))
            .ToListAsync(ct);
    }

    [HttpPost("data-records")]
    [Authorize(Roles = MesRoleGroups.ShopFloorRecord)]
    public async Task<ActionResult<List<DataRecordResponse>>> AddDataRecords(
        int id, List<DataRecordRequest> requests, CancellationToken ct)
    {
        var outcome = await execution.AddDataRecordsAsync(id, requests, CurrentUserId, ct);
        return outcome.Failed ? ToProblem(outcome) : await GetDataRecords(id, ct);
    }

    private ActionResult ToProblem<T>(Outcome<T> outcome) => outcome.Kind switch
    {
        OutcomeError.NotFound => NotFound(),
        OutcomeError.Conflict => this.ConflictProblem(outcome.Error),
        _ => this.BadRequestProblem(outcome.Error),
    };

    private static ChecklistRecordResponse ToChecklistResponse(ChecklistRecord r) =>
        new(r.Id, r.ChecklistId, r.Checklist!.Code, r.Checklist!.Name,
            r.WorkOrderId, r.EquipmentId,
            r.PerformedByUserId, r.PerformedBy?.DisplayName, r.PerformedAt,
            r.Results.OrderBy(x => x.ChecklistItem!.Sequence)
                .Select(x => new ChecklistResultResponse(
                    x.ChecklistItemId, x.ChecklistItem!.Text, x.ChecklistItem!.IsRequired,
                    x.IsChecked, x.Note))
                .ToList());
}
