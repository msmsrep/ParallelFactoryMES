using MesApp.Core.Localization;
using MesApp.Api.Localization;
using MesApp.Api.Policies;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Inventory;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// 出荷（D-40-20 出荷指示、D-40-30 出荷実行・完了報告）。
/// 出荷実行はロット・ロケーション指定で在庫を引き落とす。部分出荷可能で、
/// 全明細が出荷済みになると完了になる。出荷には承認済みの出荷判定（H-10-10。<see cref="ShipmentGatePolicy"/>）が必要で、
/// 併せてロットの使用可否（Spec.md 3.9。<see cref="LotUsabilityPolicy"/>）を満たす必要がある。
/// <para>保存と監査ログまで行う。状態は Controller で代入せず本サービス経由で変更する。</para>
/// </summary>
public sealed class ShippingService(
    MesAppDbContext db,
    InventoryService inventory,
    NumberingService numbering,
    IBusinessDateService businessDate,
    IAuditLogger auditLogger)
{
    /// <summary>出荷指示の作成（D-40-20-01）</summary>
    public async Task<Outcome<ShippingOrder>> CreateAsync(
        ShippingOrderCreateRequest request, string? userId, CancellationToken ct)
    {
        if (request.Lines.Count == 0)
        {
            return Outcome<ShippingOrder>.Invalid(ApiText.T("明細がありません。"));
        }
        var productIds = request.Lines.Select(l => l.ProductId).Distinct().ToList();
        if (await db.Products.CountAsync(p => productIds.Contains(p.Id), ct) != productIds.Count)
        {
            return Outcome<ShippingOrder>.Invalid(ApiText.T("存在しない品目IDが含まれています。"));
        }

        var order = new ShippingOrder
        {
            ShippingNo = await numbering.NextShippingNoAsync(ct),
            Destination = request.Destination,
            PlannedDate = request.PlannedDate,
            CreatedByUserId = userId,
            Lines = request.Lines
                .Select(l => new ShippingLine { ProductId = l.ProductId, Quantity = l.Quantity })
                .ToList(),
        };
        db.ShippingOrders.Add(order);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "ShippingCreate", nameof(ShippingOrder), order.Id.ToString(),
            detail: $"shippingNo={order.ShippingNo}, dest={order.Destination}", ct: ct);
        return Outcome<ShippingOrder>.Ok(order);
    }

    /// <summary>
    /// 出荷実行（D-40-30。指定ロットの在庫を引き落とし、明細の出荷済数量を更新。
    /// 部分出荷可。全明細が満たされると完了（D-40-30-05））
    /// </summary>
    public async Task<Outcome<ShippingOrder>> ShipAsync(
        int id, ShipExecuteRequest request, string? userId, CancellationToken ct)
    {
        var order = await db.ShippingOrders.Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (order is null)
        {
            return Outcome<ShippingOrder>.NotFound(ApiText.T("存在しない出荷指示IDです。"));
        }
        if (order.Status != ShippingOrderStatus.Instructed)
        {
            return Outcome<ShippingOrder>.Conflict(ApiText.T("状態 '{0}' の出荷指示は実行できません。", EnumLabels.Of(order.Status)));
        }
        if (request.Lines.Count == 0)
        {
            return Outcome<ShippingOrder>.Invalid(ApiText.T("出荷明細がありません。"));
        }

        // 出荷判定ゲート（H-10-10、Spec.md 5.3 出荷判定参照）：
        // この出荷指示を対象とする承認済みの「可」または「特採」判定が必要
        var hasApprovedJudgment = await db.ShipmentJudgments
            .AnyAsync(ShipmentGatePolicy.ValidJudgment(id), ct);
        if (ShipmentGatePolicy.CheckJudgment(hasApprovedJudgment) is string judgmentReason)
        {
            return Outcome<ShippingOrder>.Conflict(judgmentReason);
        }

        var lotIds = request.Lines.Select(l => l.LotId).Distinct().ToList();
        var lots = await db.Lots.Where(l => lotIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);

        // ロットの品目単位で出荷指示明細との突合を行う
        var shipTotals = new Dictionary<int, decimal>(); // productId -> qty
        var today = businessDate.Today;
        foreach (var line in request.Lines)
        {
            if (!lots.TryGetValue(line.LotId, out var lot))
            {
                return Outcome<ShippingOrder>.Invalid(ApiText.T("存在しないロットIDが含まれています。"));
            }
            // 出荷判定の承認後に保留・不良になったロットを出荷させない（判定書の存在だけでは不十分）。
            // 特採は不適合承認時にステータスが正常へ戻るため、ここでは正常のみを許可すればよい
            if (LotUsabilityPolicy.CheckShippable(lot, today) is string reason)
            {
                return Outcome<ShippingOrder>.Conflict(reason);
            }
            shipTotals[lot.ProductId] = shipTotals.GetValueOrDefault(lot.ProductId) + line.Quantity;
        }
        foreach (var (productId, qty) in shipTotals)
        {
            var orderLine = order.Lines.FirstOrDefault(l => l.ProductId == productId);
            if (orderLine is null)
            {
                return Outcome<ShippingOrder>.Invalid(ApiText.T("出荷指示に含まれない品目のロットが指定されています。"));
            }
            if (orderLine.ShippedQuantity + qty > orderLine.Quantity)
            {
                return Outcome<ShippingOrder>.Invalid(
                    ApiText.T("出荷数量が指示数量を超えています（指示 {0}、出荷済 {1}、今回 {2}）。", orderLine.Quantity, orderLine.ShippedQuantity, qty));
            }
        }

        try
        {
            foreach (var line in request.Lines)
            {
                await inventory.RemoveAsync(lots[line.LotId], line.LocationId, line.Quantity,
                    InventoryTransactionType.Ship, userId,
                    shippingOrderId: order.Id, note: $"出荷 {order.ShippingNo}", ct: ct);
            }
        }
        catch (InventoryException ex)
        {
            return Outcome<ShippingOrder>.Invalid(ex.Message);
        }

        foreach (var (productId, qty) in shipTotals)
        {
            order.Lines.First(l => l.ProductId == productId).ShippedQuantity += qty;
        }
        if (order.Lines.All(l => l.ShippedQuantity >= l.Quantity))
        {
            order.Status = ShippingOrderStatus.Completed;
            order.ShippedAt = DateTimeOffset.UtcNow;
            order.ShippedByUserId = userId;
        }
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "Ship", nameof(ShippingOrder), id.ToString(),
            detail: $"shippingNo={order.ShippingNo}, completed={order.Status == ShippingOrderStatus.Completed}", ct: ct);
        return Outcome<ShippingOrder>.Ok(order);
    }

    /// <summary>出荷指示の取消（実行前のみ）</summary>
    public async Task<Outcome<ShippingOrder>> CancelAsync(int id, CancellationToken ct)
    {
        var order = await db.ShippingOrders.FindAsync([id], ct);
        if (order is null)
        {
            return Outcome<ShippingOrder>.NotFound(ApiText.T("存在しない出荷指示IDです。"));
        }
        if (order.Status != ShippingOrderStatus.Instructed)
        {
            return Outcome<ShippingOrder>.Conflict(ApiText.T("状態 '{0}' の出荷指示は取消できません。", EnumLabels.Of(order.Status)));
        }
        var before = order.Status;
        order.Status = ShippingOrderStatus.Canceled;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "ShippingCancel", nameof(ShippingOrder), id.ToString(),
            detail: new { shippingNo = order.ShippingNo, before, after = order.Status }, ct: ct);
        return Outcome<ShippingOrder>.Ok(order);
    }
}
