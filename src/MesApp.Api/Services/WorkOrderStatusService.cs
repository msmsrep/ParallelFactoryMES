using MesApp.Api.Services.Webhooks;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Integration;
using MesApp.Core.Entities;
using MesApp.Core.Localization;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// 作業指示の状態変更の一元管理（Spec.md 5.2 WorkOrderStatusHistory）。
/// <para>
/// 状態を直接代入すると、配布・着手・完了・承認・取消がいつ誰の操作で起きたかが残らない。
/// 変更は必ず本サービス経由で行い、遷移を <see cref="WorkOrderStatusHistory"/> として残す。
/// </para>
/// SaveChangesは呼び出し側が行う（LotStatusServiceと同じ規約）。
/// 変更のたびに Webhook <c>work-order.status-changed</c> を同じトランザクションで積む（Spec.md 3.8 Webhook）。
/// </summary>
public class WorkOrderStatusService(MesAppDbContext db, WebhookPublisher webhooks)
{
    /// <summary>
    /// 状態を変更し、遷移を履歴に残す。現在値と同じ場合は何もしない
    /// </summary>
    public async Task ChangeStatusAsync(
        WorkOrder workOrder, WorkOrderStatus to, WorkOrderStatusChangeSource source,
        string? userId, string? note = null, CancellationToken ct = default)
    {
        if (workOrder.Status == to)
        {
            return;
        }
        var from = workOrder.Status;
        db.WorkOrderStatusHistories.Add(new WorkOrderStatusHistory
        {
            WorkOrderId = workOrder.Id,
            FromStatus = workOrder.Status,
            ToStatus = to,
            Source = source,
            Note = note,
            ChangedByUserId = userId,
        });
        workOrder.Status = to;

        await webhooks.EnqueueAsync(WebhookEventTypes.WorkOrderStatusChanged, async c =>
        {
            var codes = await db.WorkOrders.AsNoTracking()
                .Where(w => w.Id == workOrder.Id)
                .Select(w => new { w.ManufacturingOrder!.OrderNo, ProductCode = w.Product!.Code, ProcessCode = w.Process!.Code })
                .FirstAsync(c);
            // 送る文は保存される値なので訳さない（区分は原文の表示名）
            return ($"作業指示 {workOrder.WorkOrderNo} の状態が「{EnumLabels.OriginalOf(from)}」から「{EnumLabels.OriginalOf(to)}」になりました。",
                new WorkOrderStatusChangedData(codes.OrderNo, workOrder.WorkOrderNo, workOrder.RoutingSequence,
                    codes.ProductCode, codes.ProcessCode, from, to));
        }, ct);
    }
}
