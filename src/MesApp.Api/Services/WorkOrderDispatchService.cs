using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Production;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>差立の結果（Errorがあれば未反映。IsConflictは状態・設備の都合で受け付けられないもの）</summary>
public sealed record DispatchOutcome(string? Error, bool IsConflict = false)
{
    public static readonly DispatchOutcome Ok = new((string?)null);
    public static DispatchOutcome Invalid(string error) => new(error);
    public static DispatchOutcome Conflict(string error) => new(error, IsConflict: true);
}

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
    public async Task<DispatchOutcome> DispatchAsync(
        WorkOrder workOrder, DispatchRequest request, string? userId, CancellationToken ct)
    {
        if (workOrder.Status is not (WorkOrderStatus.Created or WorkOrderStatus.Dispatched))
        {
            return DispatchOutcome.Conflict($"状態 '{workOrder.Status}' の作業指示は差立できません。");
        }

        // 作業員割当：スキル・資格照合（F-20-30-01）
        if (request.AssignedUserId is not null)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.AssignedUserId, ct);
            if (user is null || !user.IsActive)
            {
                return DispatchOutcome.Invalid("割当作業者が存在しないか無効です。");
            }

            // 必要スキルは工順マスタの現在値ではなく、展開時点のスナップショットを使う（Spec.md 5.7）
            if (workOrder.RequiredSkillId is int skillId)
            {
                var today = businessDate.Today;
                var userSkill = await db.UserSkills.Include(s => s.Skill)
                    .FirstOrDefaultAsync(s => s.UserId == user.Id && s.SkillId == skillId, ct);
                var skillName = userSkill?.Skill?.Name
                    ?? await db.Skills.Where(s => s.Id == skillId).Select(s => s.Name).FirstAsync(ct);
                if (userSkill is null)
                {
                    return DispatchOutcome.Invalid(
                        $"作業者 '{user.DisplayName}' は必要スキル '{skillName}' を保有していません。");
                }
                if (userSkill.Skill!.RequiresExpiry && (userSkill.ExpiresOn is null || userSkill.ExpiresOn < today))
                {
                    return DispatchOutcome.Invalid(
                        $"作業者 '{user.DisplayName}' のスキル '{skillName}' は有効期限切れです。");
                }
            }
        }

        // 設備割当（B-10-20-02）
        if (request.AssignedEquipmentId is int equipmentId)
        {
            var equipment = await db.Equipments.FindAsync([equipmentId], ct);
            if (equipment is null || !equipment.IsActive)
            {
                return DispatchOutcome.Invalid("割当設備が存在しないか無効です。");
            }
            // 停止中・保全中・廃棄の設備には新しく割り当てさせない。既に割り当て済みの設備のまま
            // 着手順だけを変える差立は通す（保全に入った設備の作業指示を画面から触れなくしないため）
            if (equipmentId != workOrder.AssignedEquipmentId && equipment.Status != EquipmentStatus.Available)
            {
                return DispatchOutcome.Conflict(
                    $"設備 '{equipment.AssetNo}' は{EquipmentStatusLabel(equipment.Status)}のため割り当てられません。");
            }

            // 工順に候補設備が登録されていれば、その中からしか選べない。候補が未登録の工順は従来どおり設備を限定しない
            var candidates = await CandidatesOf(workOrder.ProductId, workOrder.ProcessId, workOrder.RoutingSequence)
                .Select(c => new { c.EquipmentId, c.Equipment!.AssetNo })
                .ToListAsync(ct);
            if (candidates.Count > 0 && candidates.All(c => c.EquipmentId != equipmentId))
            {
                return DispatchOutcome.Invalid(
                    $"設備 '{equipment.AssetNo}' はこの工程の候補設備ではありません" +
                    $"（候補：{string.Join("、", candidates.Select(c => c.AssetNo))}）。");
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
        return DispatchOutcome.Ok;
    }

    private static string EquipmentStatusLabel(EquipmentStatus status) => status switch
    {
        EquipmentStatus.Available => "稼働可能",
        EquipmentStatus.Stopped => "停止中",
        EquipmentStatus.UnderMaintenance => "保全中",
        EquipmentStatus.Retired => "廃棄・除却",
        _ => status.ToString(),
    };
}
