using System.Security.Claims;
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
/// 製造履歴訂正（B-70-30-01）。権限制御（生産管理ロール）＋監査ログ（変更前後の値を記録）による
/// 改ざん防止付き。産出ロットに計上済みの実績を訂正した場合は在庫・ロット数量も差分調整する。
/// </summary>
[ApiController]
[Route("api/production-records")]
[Authorize]
public class ProductionRecordsController(
    MesAppDbContext db,
    InventoryService inventory,
    IAuditLogger auditLogger) : ControllerBase
{
    [HttpPut("{id:int}")]
    [Authorize(Roles = MesRoleGroups.ProductionManage)]
    public async Task<ActionResult<ProductionRecordResponse>> Correct(
        int id, ProductionRecordCorrectionRequest request, CancellationToken ct)
    {
        var record = await db.ProductionRecords
            .Include(r => r.WorkOrder)
            .Include(r => r.OutputLot)
            .Include(r => r.PerformedBy)
            .Include(r => r.Shift)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (record is null)
        {
            return NotFound();
        }

        if (request.ScrapQuantity + request.ReworkQuantity > request.DefectQuantity)
        {
            return BadRequest(new ProblemDetails
            {
                Title = $"廃棄数と再作業待ち数の合計（{request.ScrapQuantity + request.ReworkQuantity}）が" +
                        $"不良数（{request.DefectQuantity}）を超えています。",
            });
        }

        var before = new
        {
            good = record.GoodQuantity,
            defect = record.DefectQuantity,
            scrap = record.ScrapQuantity,
            rework = record.ReworkQuantity,
        };
        var goodDelta = request.GoodQuantity - record.GoodQuantity;

        // 在庫計上済み（最終工程）の実績訂正は在庫・ロット数量へ差分を反映
        if (record.OutputLot is not null && record.OutputLocationId is not null && goodDelta != 0)
        {
            try
            {
                if (goodDelta > 0)
                {
                    await inventory.AddAsync(record.OutputLot, record.OutputLocationId.Value, goodDelta,
                        InventoryTransactionType.Adjust, User.FindFirstValue(ClaimTypes.NameIdentifier),
                        workOrderId: record.WorkOrderId, note: $"履歴訂正: {request.Reason}", ct: ct);
                }
                else
                {
                    await inventory.RemoveAsync(record.OutputLot, record.OutputLocationId.Value, -goodDelta,
                        InventoryTransactionType.Adjust, User.FindFirstValue(ClaimTypes.NameIdentifier),
                        workOrderId: record.WorkOrderId, note: $"履歴訂正: {request.Reason}", ct: ct);
                }
            }
            catch (InventoryException ex)
            {
                return BadRequest(new ProblemDetails { Title = ex.Message });
            }
            record.OutputLot.InitialQuantity += goodDelta;
        }

        // 訂正前の値を業務履歴として残す（B-70-30-01）。実績自体は上書きされるため、
        // これがないと製造記録・トレースから「何をどう直したか」を説明できない
        db.ProductionRecordCorrections.Add(new ProductionRecordCorrection
        {
            ProductionRecordId = record.Id,
            WorkOrderId = record.WorkOrderId,
            BeforeGoodQuantity = record.GoodQuantity,
            BeforeDefectQuantity = record.DefectQuantity,
            BeforeScrapQuantity = record.ScrapQuantity,
            BeforeReworkQuantity = record.ReworkQuantity,
            AfterGoodQuantity = request.GoodQuantity,
            AfterDefectQuantity = request.DefectQuantity,
            AfterScrapQuantity = request.ScrapQuantity,
            AfterReworkQuantity = request.ReworkQuantity,
            Reason = request.Reason,
            CorrectedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
        });

        record.GoodQuantity = request.GoodQuantity;
        record.DefectQuantity = request.DefectQuantity;
        record.ScrapQuantity = request.ScrapQuantity;
        record.ReworkQuantity = request.ReworkQuantity;
        await db.SaveChangesAsync(ct);
        // 訂正の証跡は後から追跡・検索できるよう構造化して残す（Spec.md 7.6）
        await auditLogger.LogAsync("Execution", "Correct", nameof(ProductionRecord), id.ToString(),
            detail: new
            {
                workOrderNo = record.WorkOrder!.WorkOrderNo,
                before,
                after = new
                {
                    good = request.GoodQuantity,
                    defect = request.DefectQuantity,
                    scrap = request.ScrapQuantity,
                    rework = request.ReworkQuantity,
                },
                reason = request.Reason,
            }, ct: ct);

        return new ProductionRecordResponse(record.Id, record.WorkOrderId, record.WorkOrder!.WorkOrderNo,
            record.PerformedByUserId, record.PerformedBy?.DisplayName,
            record.GoodQuantity, record.DefectQuantity, record.ScrapQuantity, record.ReworkQuantity,
            record.StartedAt, record.EndedAt,
            record.OutputLotId, record.OutputLot?.LotNumber, record.OutputLocationId,
            record.ApprovedByUserId, record.ApprovedAt,
            // 訂正しても直は動かない（記録時に固定した値。Spec.md 5.7）
            ShiftId: record.ShiftId, ShiftCode: record.Shift?.Code, ShiftName: record.Shift?.Name);
    }
}
