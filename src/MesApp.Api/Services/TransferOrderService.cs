using MesApp.Core.Localization;
using MesApp.Api.Localization;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Execution;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// 搬送・移動指示の作成・実行・取消（B-50-10-01〜02、D-30-10-04）。
/// 単票API（<c>TransferOrdersController</c>）と実績CSV取込の両方から呼ぶ。搬送指示の状態はここでだけ変更する。
/// 保存と監査ログまで行う。トランザクションは呼び出し側が張る。
/// </summary>
public sealed class TransferOrderService(MesAppDbContext db, InventoryService inventory, IAuditLogger auditLogger)
{
    /// <summary>搬送指示の作成（B-50-10-01）</summary>
    public async Task<Outcome<TransferOrder>> CreateAsync(
        TransferOrderRequest request, string? userId, CancellationToken ct)
    {
        if (!await db.Lots.AnyAsync(l => l.Id == request.LotId, ct))
        {
            return Outcome<TransferOrder>.Invalid(ApiText.T("存在しないロットIDです。"));
        }
        if (request.FromLocationId == request.ToLocationId)
        {
            return Outcome<TransferOrder>.Invalid(ApiText.T("移動元と移動先が同一です。"));
        }
        var locationIds = new[] { request.FromLocationId, request.ToLocationId };
        if (await db.Locations.CountAsync(l => locationIds.Contains(l.Id) && l.IsActive, ct) != 2)
        {
            return Outcome<TransferOrder>.Invalid(ApiText.T("存在しない（または無効な）ロケーションが含まれています。"));
        }

        var order = new TransferOrder
        {
            LotId = request.LotId,
            Quantity = request.Quantity,
            FromLocationId = request.FromLocationId,
            ToLocationId = request.ToLocationId,
            CreatedByUserId = userId,
        };
        db.TransferOrders.Add(order);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "TransferCreate", nameof(TransferOrder), order.Id.ToString(),
            detail: new { lotId = order.LotId, quantity = order.Quantity,
                from = order.FromLocationId, to = order.ToLocationId }, ct: ct);
        return Outcome<TransferOrder>.Ok(order);
    }

    /// <summary>移動実行（B-50-10-02。在庫を移動して完了にする）</summary>
    public async Task<Outcome<TransferOrder>> ExecuteAsync(int id, string? userId, CancellationToken ct)
    {
        var order = await db.TransferOrders.Include(t => t.Lot).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (order is null)
        {
            return Outcome<TransferOrder>.NotFound(ApiText.T("搬送指示が見つかりません。"));
        }
        if (order.Status != TransferOrderStatus.Instructed)
        {
            return Outcome<TransferOrder>.Conflict(ApiText.T("状態 '{0}' の搬送指示は実行できません。", EnumLabels.Of(order.Status)));
        }

        try
        {
            await inventory.MoveAsync(order.Lot!, order.FromLocationId, order.ToLocationId, order.Quantity,
                InventoryTransactionType.Move, userId, note: $"搬送指示 #{order.Id}", ct: ct);
        }
        catch (InventoryException ex)
        {
            return Outcome<TransferOrder>.Invalid(ex.Message);
        }

        order.Status = TransferOrderStatus.Completed;
        order.ExecutedByUserId = userId;
        order.ExecutedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "Transfer", nameof(TransferOrder), id.ToString(), ct: ct);
        return Outcome<TransferOrder>.Ok(order);
    }

    /// <summary>搬送指示の取消（指示のままのものだけ）</summary>
    public async Task<Outcome<TransferOrder>> CancelAsync(int id, CancellationToken ct)
    {
        var order = await db.TransferOrders.FindAsync([id], ct);
        if (order is null)
        {
            return Outcome<TransferOrder>.NotFound(ApiText.T("搬送指示が見つかりません。"));
        }
        if (order.Status != TransferOrderStatus.Instructed)
        {
            return Outcome<TransferOrder>.Conflict(ApiText.T("状態 '{0}' の搬送指示は取消できません。", EnumLabels.Of(order.Status)));
        }
        var before = order.Status;
        order.Status = TransferOrderStatus.Canceled;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "TransferCancel", nameof(TransferOrder), id.ToString(),
            detail: new { before, after = order.Status }, ct: ct);
        return Outcome<TransferOrder>.Ok(order);
    }
}
