using MesApp.Core.Entities;
using MesApp.Infrastructure;

namespace MesApp.Api.Services;

/// <summary>
/// 作業指示の状態変更の一元管理（Spec.md 5.2 WorkOrderStatusHistory）。
/// <para>
/// 状態を直接代入すると、配布・着手・完了・承認・取消がいつ誰の操作で起きたかが残らない。
/// 変更は必ず本サービス経由で行い、遷移を <see cref="WorkOrderStatusHistory"/> として残す。
/// </para>
/// SaveChangesは呼び出し側が行う（LotStatusServiceと同じ規約）。
/// </summary>
public class WorkOrderStatusService(MesAppDbContext db)
{
    /// <summary>
    /// 状態を変更し、遷移を履歴に残す。現在値と同じ場合は何もしない
    /// </summary>
    public void ChangeStatus(
        WorkOrder workOrder, WorkOrderStatus to, WorkOrderStatusChangeSource source,
        string? userId, string? note = null)
    {
        if (workOrder.Status == to)
        {
            return;
        }
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
    }
}
