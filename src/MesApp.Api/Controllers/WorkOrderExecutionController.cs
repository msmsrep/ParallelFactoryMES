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
    InventoryService inventory,
    IBusinessDateService businessDate,
    IAuditLogger auditLogger) : ControllerBase
{
    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>着手（B-30-30-01）。未配布でも着手可能（差立を省略する小規模運用を許容）</summary>
    [HttpPost("start")]
    public async Task<IActionResult> Start(int id, CancellationToken ct)
    {
        var workOrder = await db.WorkOrders.FindAsync([id], ct);
        if (workOrder is null)
        {
            return NotFound();
        }
        if (workOrder.Status is not (WorkOrderStatus.Created or WorkOrderStatus.Dispatched))
        {
            return Conflict(new ProblemDetails { Title = $"状態 '{workOrder.Status}' の作業指示は着手できません。" });
        }
        workOrder.Status = WorkOrderStatus.Started;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Execution", "Start", nameof(WorkOrder), id.ToString(),
            detail: $"workOrderNo={workOrder.WorkOrderNo}", ct: ct);
        return NoContent();
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
    public async Task<ActionResult<SetupRecordResponse>> AddSetupRecord(
        int id, SetupRecordRequest request, CancellationToken ct)
    {
        var workOrder = await GetActiveWorkOrderAsync(id, ct);
        if (workOrder is null)
        {
            return NotFound();
        }
        if (workOrder.Status is WorkOrderStatus.Approved or WorkOrderStatus.Canceled)
        {
            return Conflict(new ProblemDetails { Title = $"状態 '{workOrder.Status}' の作業指示には記録できません。" });
        }

        var record = new SetupRecord
        {
            WorkOrderId = id,
            Type = request.Type,
            StartedAt = request.StartedAt,
            EndedAt = request.EndedAt,
            PerformedByUserId = CurrentUserId!,
            AbnormalityNote = request.AbnormalityNote,
        };
        db.SetupRecords.Add(record);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Execution", "SetupRecord", nameof(WorkOrder), id.ToString(),
            detail: $"type={request.Type}", ct: ct);
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
    public async Task<ActionResult<ChecklistRecordResponse>> AddChecklistRecord(
        int id, ChecklistRecordRequest request, CancellationToken ct)
    {
        var workOrder = await GetActiveWorkOrderAsync(id, ct);
        if (workOrder is null)
        {
            return NotFound();
        }
        var checklist = await db.Checklists.AsNoTracking().Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == request.ChecklistId, ct);
        if (checklist is null)
        {
            return BadRequest(new ProblemDetails { Title = "存在しないチェックリストIDです。" });
        }

        var resultByItem = request.Results.ToDictionary(r => r.ChecklistItemId);
        var itemIds = checklist.Items.Select(i => i.Id).ToHashSet();
        if (request.Results.Any(r => !itemIds.Contains(r.ChecklistItemId)))
        {
            return BadRequest(new ProblemDetails { Title = "チェックリストに存在しない項目IDが含まれています。" });
        }
        var missingRequired = checklist.Items
            .Where(i => i.IsRequired &&
                        (!resultByItem.TryGetValue(i.Id, out var r) || !r.IsChecked))
            .Select(i => i.Text)
            .ToList();
        if (missingRequired.Count > 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = $"必須項目が未チェックです: {string.Join("、", missingRequired)}",
            });
        }

        var record = new ChecklistRecord
        {
            ChecklistId = checklist.Id,
            WorkOrderId = id,
            PerformedByUserId = CurrentUserId!,
            Results = checklist.Items.Select(i => new ChecklistResultItem
            {
                ChecklistItemId = i.Id,
                IsChecked = resultByItem.TryGetValue(i.Id, out var r) && r.IsChecked,
                Note = resultByItem.TryGetValue(i.Id, out var r2) ? r2.Note : null,
            }).ToList(),
        };
        db.ChecklistRecords.Add(record);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Execution", "ChecklistRecord", nameof(WorkOrder), id.ToString(),
            detail: $"checklist={checklist.Code}", ct: ct);

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
                c.LotId, c.Lot!.LotNumber, c.LocationId, c.Quantity, c.ConsumedAt, c.Method))
            .ToListAsync(ct);
    }

    [HttpPost("consumptions")]
    public async Task<ActionResult<ConsumptionResponse>> AddConsumption(
        int id, ConsumptionRequest request, CancellationToken ct)
    {
        var workOrder = await GetActiveWorkOrderAsync(id, ct);
        if (workOrder is null)
        {
            return NotFound();
        }
        if (workOrder.Status is WorkOrderStatus.Approved or WorkOrderStatus.Canceled)
        {
            return Conflict(new ProblemDetails { Title = $"状態 '{workOrder.Status}' の作業指示には記録できません。" });
        }
        var lot = await db.Lots.FirstOrDefaultAsync(l => l.Id == request.LotId, ct);
        if (lot is null)
        {
            return BadRequest(new ProblemDetails { Title = "存在しないロットIDです。" });
        }
        // 投入可否（ステータス・有効期限）の判定は LotUsabilityPolicy に集約している
        if (LotUsabilityPolicy.CheckIssuable(lot, businessDate.Today) is string reason)
        {
            return BadRequest(new ProblemDetails { Title = reason });
        }

        try
        {
            await inventory.RemoveAsync(lot, request.LocationId, request.Quantity,
                InventoryTransactionType.ProcessIssue, CurrentUserId, workOrderId: id,
                note: "部材投入", ct: ct);
        }
        catch (InventoryException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message });
        }

        var consumption = new MaterialConsumption
        {
            WorkOrderId = id,
            ProductId = lot.ProductId,
            LotId = lot.Id,
            LocationId = request.LocationId,
            Quantity = request.Quantity,
            Method = ConsumptionMethod.Manual,
            RecordedByUserId = CurrentUserId,
        };
        db.MaterialConsumptions.Add(consumption);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Execution", "Consumption", nameof(WorkOrder), id.ToString(),
            detail: $"lot={lot.LotNumber}, qty={request.Quantity}", ct: ct);

        var product = await db.Products.FirstAsync(p => p.Id == lot.ProductId, ct);
        return new ConsumptionResponse(consumption.Id, id, product.Id, product.Code, product.Name,
            lot.Id, lot.LotNumber, request.LocationId, request.Quantity,
            consumption.ConsumedAt, consumption.Method);
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
                r.GoodQuantity, r.DefectQuantity, r.StartedAt, r.EndedAt,
                r.OutputLotId, r.OutputLot!.LotNumber, r.OutputLocationId,
                r.ApprovedByUserId, r.ApprovedAt))
            .ToListAsync(ct);
    }

    /// <summary>
    /// 実績入力。作業指示は完了状態になる（B-30-30-06）。最終工程の実績では産出ロットへ在庫計上
    /// （outputLocationId必須）。backflush=trueでMBOM×(良品+不良)の部材を先入れ先出しで自動消費。
    /// </summary>
    [HttpPost("production-records")]
    public async Task<ActionResult<ProductionRecordResponse>> AddProductionRecord(
        int id, ProductionRecordRequest request, CancellationToken ct)
    {
        var workOrder = await db.WorkOrders
            .Include(w => w.ManufacturingOrder).ThenInclude(o => o!.OutputLot)
            .FirstOrDefaultAsync(w => w.Id == id, ct);
        if (workOrder is null)
        {
            return NotFound();
        }
        if (workOrder.Status is WorkOrderStatus.Approved or WorkOrderStatus.Canceled)
        {
            return Conflict(new ProblemDetails { Title = $"状態 '{workOrder.Status}' の作業指示には実績を記録できません。" });
        }
        if (request.GoodQuantity + request.DefectQuantity <= 0)
        {
            return BadRequest(new ProblemDetails { Title = "良品数と不良数の合計は0より大きい必要があります。" });
        }

        var order = workOrder.ManufacturingOrder!;
        var isFinalStep = workOrder.RoutingSequence == await db.WorkOrders
            .Where(w => w.ManufacturingOrderId == order.Id && w.Status != WorkOrderStatus.Canceled)
            .MaxAsync(w => w.RoutingSequence, ct);

        Lot? outputLot = null;
        if (isFinalStep && request.GoodQuantity > 0)
        {
            if (request.OutputLocationId is null)
            {
                return BadRequest(new ProblemDetails { Title = "最終工程の実績には入庫先ロケーション（outputLocationId）が必要です。" });
            }
            if (!await db.Locations.AnyAsync(l => l.Id == request.OutputLocationId && l.IsActive, ct))
            {
                return BadRequest(new ProblemDetails { Title = "存在しない（または無効な）入庫先ロケーションです。" });
            }
            outputLot = order.OutputLot;
            if (outputLot is null)
            {
                return Conflict(new ProblemDetails { Title = "産出ロットが未採番です（指図が正しく展開されていません）。" });
            }
        }

        try
        {
            // バックフラッシュ（B-40-10-09）：MBOM×完了数量の部材を自動消費
            if (request.Backflush)
            {
                var bom = await db.BomItems.Include(b => b.ChildProduct)
                    .Where(b => b.ParentProductId == workOrder.ProductId)
                    .ToListAsync(ct);
                if (bom.Count == 0)
                {
                    return BadRequest(new ProblemDetails { Title = "MBOMが未登録のためバックフラッシュできません。" });
                }
                var totalProduced = request.GoodQuantity + request.DefectQuantity;
                foreach (var bomItem in bom)
                {
                    var required = bomItem.QuantityPer * totalProduced;
                    var allocations = await inventory.AllocateFefoAsync(bomItem.ChildProductId, required, ct);
                    foreach (var (lot, locationId, quantity) in allocations)
                    {
                        await inventory.RemoveAsync(lot, locationId, quantity,
                            InventoryTransactionType.ProcessIssue, CurrentUserId, workOrderId: id,
                            note: "バックフラッシュ", ct: ct);
                        db.MaterialConsumptions.Add(new MaterialConsumption
                        {
                            WorkOrderId = id,
                            ProductId = bomItem.ChildProductId,
                            LotId = lot.Id,
                            LocationId = locationId,
                            Quantity = quantity,
                            Method = ConsumptionMethod.Backflush,
                            RecordedByUserId = CurrentUserId,
                        });
                    }
                }
            }

            // 最終工程：産出ロットへの在庫計上（B-40-10-02）
            if (outputLot is not null)
            {
                outputLot.InitialQuantity += request.GoodQuantity;
                outputLot.SourceWorkOrderId = id;
                outputLot.ManufacturedOn = businessDate.Today;
                await inventory.AddAsync(outputLot, request.OutputLocationId!.Value, request.GoodQuantity,
                    InventoryTransactionType.PutAway, CurrentUserId, workOrderId: id,
                    note: "完成品在庫計上", ct: ct);
            }
        }
        catch (InventoryException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message });
        }

        var record = new ProductionRecord
        {
            WorkOrderId = id,
            PerformedByUserId = CurrentUserId!,
            GoodQuantity = request.GoodQuantity,
            DefectQuantity = request.DefectQuantity,
            StartedAt = request.StartedAt,
            EndedAt = request.EndedAt,
            OutputLotId = outputLot?.Id,
            OutputLocationId = outputLot is not null ? request.OutputLocationId : null,
        };
        db.ProductionRecords.Add(record);
        workOrder.Status = WorkOrderStatus.Completed;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Execution", "ProductionRecord", nameof(WorkOrder), id.ToString(),
            detail: $"good={request.GoodQuantity}, defect={request.DefectQuantity}, backflush={request.Backflush}", ct: ct);

        var name = await db.Users.Where(u => u.Id == record.PerformedByUserId)
            .Select(u => u.DisplayName).FirstOrDefaultAsync(ct);
        return new ProductionRecordResponse(record.Id, id, workOrder.WorkOrderNo,
            record.PerformedByUserId, name, record.GoodQuantity, record.DefectQuantity,
            record.StartedAt, record.EndedAt, record.OutputLotId, outputLot?.LotNumber,
            record.OutputLocationId, null, null);
    }

    /// <summary>製造完了承認（B-40-10-10）。全作業指示が承認/取消済みになると指図も完了になる</summary>
    [HttpPost("approve")]
    [Authorize(Roles = RoleGroups.ProductionManage)]
    public async Task<IActionResult> Approve(int id, CancellationToken ct)
    {
        var workOrder = await db.WorkOrders.Include(w => w.ManufacturingOrder)
            .FirstOrDefaultAsync(w => w.Id == id, ct);
        if (workOrder is null)
        {
            return NotFound();
        }
        if (workOrder.Status != WorkOrderStatus.Completed)
        {
            return Conflict(new ProblemDetails { Title = $"状態 '{workOrder.Status}' の作業指示は承認できません（完了済みのみ）。" });
        }

        workOrder.Status = WorkOrderStatus.Approved;
        var now = DateTimeOffset.UtcNow;
        await db.ProductionRecords
            .Where(r => r.WorkOrderId == id && r.ApprovedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ApprovedByUserId, CurrentUserId)
                .SetProperty(r => r.ApprovedAt, now), ct);

        // 指図の完了判定：全作業指示が承認済み（または取消）なら指図完了
        var allDone = !await db.WorkOrders.AnyAsync(w =>
            w.ManufacturingOrderId == workOrder.ManufacturingOrderId
            && w.Id != id
            && w.Status != WorkOrderStatus.Approved && w.Status != WorkOrderStatus.Canceled, ct);
        if (allDone)
        {
            workOrder.ManufacturingOrder!.Status = ManufacturingOrderStatus.Completed;
            workOrder.ManufacturingOrder.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Execution", "ApproveWorkOrder", nameof(WorkOrder), id.ToString(),
            detail: $"workOrderNo={workOrder.WorkOrderNo}, orderCompleted={allDone}", ct: ct);
        return NoContent();
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
            .Select(r => new DataRecordResponse(r.Id, r.WorkOrderId, r.Item, r.Value, r.RecordedAt))
            .ToListAsync(ct);
    }

    [HttpPost("data-records")]
    public async Task<ActionResult<List<DataRecordResponse>>> AddDataRecords(
        int id, List<DataRecordRequest> requests, CancellationToken ct)
    {
        var workOrder = await GetActiveWorkOrderAsync(id, ct);
        if (workOrder is null)
        {
            return NotFound();
        }
        if (requests.Count == 0)
        {
            return BadRequest(new ProblemDetails { Title = "記録する項目がありません。" });
        }
        db.ProductionDataRecords.AddRange(requests.Select(r => new ProductionDataRecord
        {
            WorkOrderId = id,
            Item = r.Item,
            Value = r.Value,
            RecordedByUserId = CurrentUserId,
        }));
        await db.SaveChangesAsync(ct);
        return await GetDataRecords(id, ct);
    }

    private Task<WorkOrder?> GetActiveWorkOrderAsync(int id, CancellationToken ct) =>
        db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id, ct);

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
