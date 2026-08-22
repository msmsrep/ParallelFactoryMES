using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Production;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 作業指示（Spec.md 5.2 WorkOrder）と差立（B-10-20：作業員割当（スキル照合 F-20-30-01）・設備割当・着手順）
/// </summary>
[ApiController]
[Route("api/work-orders")]
[Authorize]
public class WorkOrdersController(
    MesAppDbContext db,
    IBusinessDateService businessDate,
    IAuditLogger auditLogger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<WorkOrderResponse>>> List(
        [FromQuery] int? manufacturingOrderId = null,
        [FromQuery] WorkOrderStatus? status = null,
        [FromQuery] int? processId = null,
        [FromQuery] string? assignedUserId = null,
        CancellationToken ct = default)
    {
        var query = BaseQuery();
        if (manufacturingOrderId is not null)
        {
            query = query.Where(w => w.ManufacturingOrderId == manufacturingOrderId);
        }
        if (status is not null)
        {
            query = query.Where(w => w.Status == status);
        }
        if (processId is not null)
        {
            query = query.Where(w => w.ProcessId == processId);
        }
        if (assignedUserId is not null)
        {
            query = query.Where(w => w.AssignedUserId == assignedUserId);
        }

        // 差立で設定した着手順を優先して並べる（B-10-20-03）
        var workOrders = await query
            .OrderBy(w => w.DispatchOrder == null).ThenBy(w => w.DispatchOrder)
            .ThenBy(w => w.ManufacturingOrderId).ThenBy(w => w.RoutingSequence)
            .ToListAsync(ct);
        return workOrders.Select(w => ToResponse(w, w.ManufacturingOrder!)).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<WorkOrderResponse>> Get(int id, CancellationToken ct)
    {
        var workOrder = await BaseQuery().FirstOrDefaultAsync(w => w.Id == id, ct);
        return workOrder is null ? NotFound() : ToResponse(workOrder, workOrder.ManufacturingOrder!);
    }

    /// <summary>
    /// 差立（B-10-20-01〜03）。作業員割当時は工順の必要スキルと照合し、
    /// スキル未保有・期限切れなら割当を拒否する（F-20-30-01）。着手済み以降は変更不可。
    /// </summary>
    [HttpPut("{id:int}/dispatch")]
    [Authorize(Roles = RoleGroups.ProductionManage)]
    public async Task<ActionResult<WorkOrderResponse>> Dispatch(int id, DispatchRequest request, CancellationToken ct)
    {
        var workOrder = await BaseQuery(track: true).FirstOrDefaultAsync(w => w.Id == id, ct);
        if (workOrder is null)
        {
            return NotFound();
        }
        if (workOrder.Status is not (WorkOrderStatus.Created or WorkOrderStatus.Dispatched))
        {
            return Conflict(new ProblemDetails { Title = $"状態 '{workOrder.Status}' の作業指示は差立できません。" });
        }

        // 作業員割当：スキル・資格照合（F-20-30-01）
        if (request.AssignedUserId is not null)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.AssignedUserId, ct);
            if (user is null || !user.IsActive)
            {
                return BadRequest(new ProblemDetails { Title = "割当作業者が存在しないか無効です。" });
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
                    return BadRequest(new ProblemDetails
                    {
                        Title = $"作業者 '{user.DisplayName}' は必要スキル '{skillName}' を保有していません。",
                    });
                }
                if (userSkill.Skill!.RequiresExpiry && (userSkill.ExpiresOn is null || userSkill.ExpiresOn < today))
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = $"作業者 '{user.DisplayName}' のスキル '{skillName}' は有効期限切れです。",
                    });
                }
            }
        }

        // 設備割当（B-10-20-02）
        if (request.AssignedEquipmentId is int equipmentId)
        {
            var equipment = await db.Equipments.FindAsync([equipmentId], ct);
            if (equipment is null || !equipment.IsActive)
            {
                return BadRequest(new ProblemDetails { Title = "割当設備が存在しないか無効です。" });
            }
        }

        workOrder.AssignedUserId = request.AssignedUserId;
        workOrder.AssignedEquipmentId = request.AssignedEquipmentId;
        workOrder.DispatchOrder = request.DispatchOrder;
        workOrder.Status = WorkOrderStatus.Dispatched;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Production", "Dispatch", nameof(WorkOrder), id.ToString(),
            detail: $"workOrderNo={workOrder.WorkOrderNo}, user={request.AssignedUserId}, " +
                    $"equipment={request.AssignedEquipmentId}, order={request.DispatchOrder}", ct: ct);

        var updated = await BaseQuery().FirstAsync(w => w.Id == id, ct);
        return ToResponse(updated, updated.ManufacturingOrder!);
    }

    private IQueryable<WorkOrder> BaseQuery(bool track = false)
    {
        var query = db.WorkOrders
            .Include(w => w.ManufacturingOrder)
            .Include(w => w.Product)
            .Include(w => w.Process)
            .Include(w => w.AssignedUser)
            .Include(w => w.AssignedEquipment)
            .AsQueryable();
        return track ? query : query.AsNoTracking();
    }

    internal static WorkOrderResponse ToResponse(WorkOrder w, ManufacturingOrder order) =>
        new(w.Id, w.WorkOrderNo, w.ManufacturingOrderId, order.OrderNo,
            w.ProductId, w.Product?.Code ?? order.Product!.Code, w.Product?.Name ?? order.Product!.Name,
            w.ProcessId, w.Process!.Code, w.Process!.Name,
            w.RoutingSequence, w.PlannedQuantity, w.DispatchOrder,
            w.AssignedUserId, w.AssignedUser?.DisplayName,
            w.AssignedEquipmentId, w.AssignedEquipment?.Name,
            w.Status,
            w.StandardWorkMinutes, w.StandardSetupMinutes, w.ControlItems);
}
