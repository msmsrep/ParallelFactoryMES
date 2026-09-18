using System.Security.Claims;
using MesApp.Api.Services;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Inventory;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 受入（D-10-10）：受入登録・在庫計上（受入ロット採番含む）と受入取消
/// </summary>
[ApiController]
[Route("api/receiving")]
[Authorize]
public class ReceivingController(
    MesAppDbContext db,
    InventoryService inventory,
    ReceivingService receiving,
    LotStatusService lotStatus,
    IAuditLogger auditLogger) : ControllerBase
{
    /// <summary>受入登録（D-10-10-02。ロット生成＋在庫計上）</summary>
    [HttpPost]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<ActionResult<LotResponse>> Receive(ReceivingRequest request, CancellationToken ct)
    {
        // Lot.Idの確定に一度SaveChangesが要るため保存が2回に分かれる。
        // 途中で失敗すると「在庫のないロット」が残るので、明示的なトランザクションでまとめる
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var outcome = await receiving.ReceiveAsync(request, User.FindFirstValue(ClaimTypes.NameIdentifier), ct);
        if (outcome.Error is not null)
        {
            return outcome.IsConflict ? this.ConflictProblem(outcome.Error) : this.BadRequestProblem(outcome.Error);
        }
        await transaction.CommitAsync(ct);

        var (lot, product) = (outcome.Lot!, outcome.Product!);
        return new LotResponse(lot.Id, lot.LotNumber, product.Id, product.Code, product.Name,
            lot.InitialQuantity, lot.OriginType, lot.StockStatus,
            lot.ManufacturedOn, lot.ExpiresOn, lot.Grade, lot.ParentLotId);
    }

    /// <summary>
    /// 受入取消（D-10-10-04）。受入後に在庫が動いていない（受入トランザクション1件のみ・
    /// 在庫数量が受入数量と一致）場合のみ取消できる。
    /// </summary>
    [HttpPost("{lotId:int}/cancel")]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<IActionResult> Cancel(int lotId, CancellationToken ct)
    {
        var lot = await db.Lots.FirstOrDefaultAsync(l => l.Id == lotId, ct);
        if (lot is null)
        {
            return NotFound();
        }
        if (lot.OriginType != LotOriginType.Receiving)
        {
            return this.BadRequestProblem("受入由来のロットではありません。");
        }

        var transactions = await db.InventoryTransactions.Where(t => t.LotId == lotId).ToListAsync(ct);
        if (transactions.Count != 1 || transactions[0].Type != InventoryTransactionType.Receipt)
        {
            return this.ConflictProblem("受入後に在庫が変動しているため取消できません（数量調整で対応してください）。");
        }

        var receipt = transactions[0];
        try
        {
            await inventory.RemoveAsync(lot, receipt.ToLocationId!.Value, receipt.Quantity,
                InventoryTransactionType.Adjust, User.FindFirstValue(ClaimTypes.NameIdentifier),
                note: "受入取消", ct: ct);
        }
        catch (InventoryException ex)
        {
            return this.ConflictProblem(ex.Message);
        }
        lot.InitialQuantity = 0;
        lotStatus.ChangeStatus(lot, LotStockStatus.ToBeDiscarded, LotStatusChangeSource.Receiving,
            "受入取消", User.FindFirstValue(ClaimTypes.NameIdentifier));
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "ReceiveCancel", nameof(Lot), lotId.ToString(),
            detail: $"lot={lot.LotNumber}", ct: ct);
        return NoContent();
    }
}
