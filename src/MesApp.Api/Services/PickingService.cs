using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Inventory;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// 出庫・ピッキング（D-20-10 出庫指示・ピッキング指示、D-20-20 ピッキング実行・払出）。
/// 明細のロット・ロケーションは作成時に先入れ先出し（有効期限優先）で自動引当する（D-20-10-02）。
/// <para>保存と監査ログまで行う。状態は Controller で代入せず本サービス経由で変更する。</para>
/// </summary>
public sealed class PickingService(
    MesAppDbContext db,
    InventoryService inventory,
    NumberingService numbering,
    IAuditLogger auditLogger)
{
    /// <summary>ピッキング指示の作成（払出先＝作業指示または出荷指示。FEFOで自動引当）</summary>
    public async Task<Outcome<PickingOrder>> CreateAsync(
        PickingOrderCreateRequest request, string? userId, CancellationToken ct)
    {
        if (request.Lines.Count == 0)
        {
            return Outcome<PickingOrder>.Invalid("明細がありません。");
        }
        if (request.Type == PickingOrderType.ProcessIssue)
        {
            if (request.WorkOrderId is null
                || !await db.WorkOrders.AnyAsync(w => w.Id == request.WorkOrderId, ct))
            {
                return Outcome<PickingOrder>.Invalid("工程払出には有効な作業指示ID（workOrderId）が必要です。");
            }
        }
        else if (request.ShippingOrderId is null
                 || !await db.ShippingOrders.AnyAsync(s => s.Id == request.ShippingOrderId, ct))
        {
            return Outcome<PickingOrder>.Invalid("出荷ピッキングには有効な出荷指示ID（shippingOrderId）が必要です。");
        }

        var order = new PickingOrder
        {
            OrderNo = await numbering.NextPickingNoAsync(ct),
            Type = request.Type,
            WorkOrderId = request.Type == PickingOrderType.ProcessIssue ? request.WorkOrderId : null,
            ShippingOrderId = request.Type == PickingOrderType.Shipping ? request.ShippingOrderId : null,
            CreatedByUserId = userId,
        };

        try
        {
            foreach (var line in request.Lines)
            {
                var allocations = await inventory.AllocateFefoAsync(line.ProductId, line.Quantity, ct);
                order.Lines.AddRange(allocations.Select(a => new PickingLine
                {
                    ProductId = line.ProductId,
                    LotId = a.Lot.Id,
                    LocationId = a.LocationId,
                    Quantity = a.Quantity,
                }));
            }
        }
        catch (InventoryException ex)
        {
            return Outcome<PickingOrder>.Invalid(ex.Message);
        }

        db.PickingOrders.Add(order);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "PickingCreate", nameof(PickingOrder), order.Id.ToString(),
            detail: $"orderNo={order.OrderNo}, type={order.Type}", ct: ct);
        return Outcome<PickingOrder>.Ok(order);
    }

    /// <summary>ピッキング実行・払出（D-20-20-01〜02。在庫を引き落として完了にする）</summary>
    public async Task<Outcome<PickingOrder>> ExecuteAsync(int id, string? userId, CancellationToken ct)
    {
        var order = await db.PickingOrders
            .Include(p => p.Lines).ThenInclude(l => l.Lot)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (order is null)
        {
            return Outcome<PickingOrder>.NotFound("存在しないピッキング指示IDです。");
        }
        if (order.Status != PickingOrderStatus.Instructed)
        {
            return Outcome<PickingOrder>.Conflict($"状態 '{order.Status}' のピッキング指示は実行できません。");
        }

        var type = order.Type == PickingOrderType.ProcessIssue
            ? InventoryTransactionType.ProcessIssue
            : InventoryTransactionType.Issue;
        try
        {
            foreach (var line in order.Lines)
            {
                await inventory.RemoveAsync(line.Lot!, line.LocationId, line.Quantity, type, userId,
                    workOrderId: order.WorkOrderId, pickingOrderId: order.Id,
                    shippingOrderId: order.ShippingOrderId,
                    note: $"ピッキング {order.OrderNo}", ct: ct);
            }
        }
        catch (InventoryException ex)
        {
            return Outcome<PickingOrder>.Invalid(ex.Message);
        }

        order.Status = PickingOrderStatus.Completed;
        order.ExecutedByUserId = userId;
        order.ExecutedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "PickingExecute", nameof(PickingOrder), id.ToString(),
            detail: $"orderNo={order.OrderNo}", ct: ct);
        return Outcome<PickingOrder>.Ok(order);
    }

    /// <summary>ピッキング指示の取消（実行前のみ）</summary>
    public async Task<Outcome<PickingOrder>> CancelAsync(int id, CancellationToken ct)
    {
        var order = await db.PickingOrders.FindAsync([id], ct);
        if (order is null)
        {
            return Outcome<PickingOrder>.NotFound("存在しないピッキング指示IDです。");
        }
        if (order.Status != PickingOrderStatus.Instructed)
        {
            return Outcome<PickingOrder>.Conflict($"状態 '{order.Status}' のピッキング指示は取消できません。");
        }
        var before = order.Status;
        order.Status = PickingOrderStatus.Canceled;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "PickingCancel", nameof(PickingOrder), id.ToString(),
            detail: new { orderNo = order.OrderNo, before, after = order.Status }, ct: ct);
        return Outcome<PickingOrder>.Ok(order);
    }
}
