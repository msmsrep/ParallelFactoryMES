using MesApp.Api.Localization;
using MesApp.Api.Policies;
using MesApp.Core.Localization;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Production;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// 差立（B-10-20-01〜03）。作業員割当時は必要スキルと照合し（F-20-30-01）、
/// 設備割当時は設備の状態と工順の候補設備を確かめる。保存・監査ログまで行う。
/// </summary>
public sealed class WorkOrderDispatchService(
    MesAppDbContext db,
    WorkOrderStatusService workOrderStatus,
    IBusinessDateService businessDate,
    IAuditLogger auditLogger)
{
    /// <summary>
    /// 作業指示の候補設備。候補は工順マスタの現在値を見る
    /// （設備は差立で決めるためスナップショットに含めない：Spec.md 5.7）
    /// </summary>
    public IQueryable<RoutingEquipment> CandidatesOf(int productId, int processId, int routingSequence) =>
        db.RoutingEquipments.AsNoTracking()
            .Where(c => c.Routing!.ProductId == productId
                        && c.Routing!.ProcessId == processId
                        && c.Routing!.Sequence == routingSequence);

    /// <summary>差立を反映する。<paramref name="workOrder"/> は追跡状態で渡す</summary>
    public async Task<Outcome<WorkOrder>> DispatchAsync(
        WorkOrder workOrder, DispatchRequest request, string? userId, CancellationToken ct)
    {
        if (workOrder.Status is not (WorkOrderStatus.Created or WorkOrderStatus.Dispatched))
        {
            return Outcome<WorkOrder>.Conflict(ApiText.T("状態 '{0}' の作業指示は差立できません。", EnumLabels.Of(workOrder.Status)));
        }

        // 作業員割当：スキル・資格照合（F-20-30-01）
        if (request.AssignedUserId is not null)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.AssignedUserId, ct);
            if (user is null || !user.IsActive)
            {
                return Outcome<WorkOrder>.Invalid(ApiText.T("割当作業者が存在しないか無効です。"));
            }

            // 必要スキルは工順マスタの現在値ではなく、展開時点のスナップショットを使う（Spec.md 5.7）
            if (workOrder.RequiredSkillId is int skillId)
            {
                var skill = await db.Skills.FirstAsync(s => s.Id == skillId, ct);
                var userSkill = await db.UserSkills
                    .FirstOrDefaultAsync(s => s.UserId == user.Id && s.SkillId == skillId, ct);
                if (SkillQualificationPolicy.Check(user.DisplayName, skill, userSkill, businessDate.Today) is { } error)
                {
                    return Outcome<WorkOrder>.Invalid(error);
                }
            }
        }

        // 設備割当（B-10-20-02）
        if (request.AssignedEquipmentId is int equipmentId)
        {
            var equipment = await db.Equipments.FindAsync([equipmentId], ct);
            if (equipment is null || !equipment.IsActive)
            {
                return Outcome<WorkOrder>.Invalid(ApiText.T("割当設備が存在しないか無効です。"));
            }
            // 停止中・保全中・廃棄の設備には新しく割り当てさせない。既に割り当て済みの設備のまま
            // 着手順だけを変える差立は通す（保全に入った設備の作業指示を画面から触れなくしないため）
            if (equipmentId != workOrder.AssignedEquipmentId && equipment.Status != EquipmentStatus.Available)
            {
                return Outcome<WorkOrder>.Conflict(
                    ApiText.T("設備 '{0}' は{1}のため割り当てられません。", equipment.AssetNo, EnumLabels.Of(equipment.Status)));
            }

            // 工順に候補設備が登録されていれば、その中からしか選べない。候補が未登録の工順は従来どおり設備を限定しない
            var candidates = await CandidatesOf(workOrder.ProductId, workOrder.ProcessId, workOrder.RoutingSequence)
                .Select(c => new { c.EquipmentId, c.Equipment!.AssetNo })
                .ToListAsync(ct);
            if (candidates.Count > 0 && candidates.All(c => c.EquipmentId != equipmentId))
            {
                return Outcome<WorkOrder>.Invalid(
                    ApiText.T("設備 '{0}' はこの工程の候補設備ではありません（候補：{1}）。", equipment.AssetNo, string.Join("、", candidates.Select(c => c.AssetNo))));
            }
        }

        workOrder.AssignedUserId = request.AssignedUserId;
        workOrder.AssignedEquipmentId = request.AssignedEquipmentId;
        workOrder.DispatchOrder = request.DispatchOrder;
        workOrderStatus.ChangeStatus(workOrder, WorkOrderStatus.Dispatched,
            WorkOrderStatusChangeSource.Dispatch, userId);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Production", "Dispatch", nameof(WorkOrder), workOrder.Id.ToString(),
            detail: $"workOrderNo={workOrder.WorkOrderNo}, user={request.AssignedUserId}, " +
                    $"equipment={request.AssignedEquipmentId}, order={request.DispatchOrder}", ct: ct);
        return Outcome<WorkOrder>.Ok(workOrder);
    }

}
