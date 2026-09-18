using MesApp.Core.Entities;
using MesApp.Infrastructure;

namespace MesApp.Api.Services;

/// <summary>
/// ロットの在庫ステータス変更の一元管理（Spec.md 5.3 LotStatusHistory）。
/// <para>
/// 在庫ステータスは現在状態しか持たないため、直接代入すると「誰が・いつ・なぜ止め、
/// どの判断で解除したか」が残らない。ステータスの変更は必ず本サービス経由で行い、
/// 遷移を <see cref="LotStatusHistory"/> として残す。
/// </para>
/// SaveChangesは呼び出し側が行う（InventoryServiceと同じ規約。1操作＝1トランザクション）。
/// </summary>
public class LotStatusService(MesAppDbContext db)
{
    /// <summary>
    /// ステータスを変更し、遷移を履歴に残す。現在値と同じ場合は何もしない（無意味な履歴を作らない）。
    /// </summary>
    /// <param name="lot">対象ロット（追跡中のエンティティ）</param>
    /// <param name="to">変更後のステータス</param>
    /// <param name="source">変更の契機（手動操作／検査／不適合／受入取消）</param>
    /// <param name="reason">理由（保留理由・解除理由など）</param>
    /// <param name="userId">操作ユーザー</param>
    /// <param name="inspectionOrderId">契機となった検査指示</param>
    /// <param name="nonconformanceReportId">契機となった不適合</param>
    public void ChangeStatus(
        Lot lot, LotStockStatus to, LotStatusChangeSource source, string? reason, string? userId,
        int? inspectionOrderId = null, int? nonconformanceReportId = null)
    {
        if (lot.StockStatus == to)
        {
            return;
        }
        db.LotStatusHistories.Add(new LotStatusHistory
        {
            LotId = lot.Id,
            FromStatus = lot.StockStatus,
            ToStatus = to,
            Source = source,
            Reason = reason,
            InspectionOrderId = inspectionOrderId,
            NonconformanceReportId = nonconformanceReportId,
            ChangedByUserId = userId,
        });
        lot.StockStatus = to;
    }
}
