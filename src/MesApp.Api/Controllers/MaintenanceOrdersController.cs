using System.Security.Claims;
using MesApp.Api.Services;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Common;
using MesApp.Core.Contracts.Maintenance;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 保全指示・実績（E-30-20 指示の作成・発行、E-30-30 計画外の保全依頼、E-40 保全実施、
/// E-60-30 治工具メンテナンス）。計画保全の作成は保全ロール、突発依頼は現場からも起票できる。
/// 実績登録（E-40-30-01）で指示は完了し、元計画・治工具寿命カウンタへ連動する。
/// </summary>
[ApiController]
[Route("api/maintenance-orders")]
[Authorize]
public class MaintenanceOrdersController(
    MesAppDbContext db,
    NumberingService numbering,
    IAuditLogger auditLogger) : ControllerBase
{
    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet]
    public async Task<ActionResult<PagedResult<MaintenanceOrderResponse>>> List(
        [FromQuery] PageQuery paging,
        [FromQuery] MaintenanceOrderStatus? status = null,
        [FromQuery] int? equipmentId = null,
        [FromQuery] int? toolId = null,
        CancellationToken ct = default)
    {
        var query = BaseQuery();
        if (status is not null)
        {
            query = query.Where(o => o.Status == status);
        }
        if (equipmentId is not null)
        {
            query = query.Where(o => o.EquipmentId == equipmentId);
        }
        if (toolId is not null)
        {
            query = query.Where(o => o.ToolId == toolId);
        }
        var orders = await query.OrderByDescending(o => o.Id).ToPagedResultAsync(paging, ct);
        return orders.Map(ToResponse);
    }

    /// <summary>保全履歴の詳細（E-20-30-01〜02：いつ・誰が・どう保全したか）</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<MaintenanceOrderResponse>> Get(int id, CancellationToken ct)
    {
        var order = await BaseQuery().FirstOrDefaultAsync(o => o.Id == id, ct);
        return order is null ? NotFound() : ToResponse(order);
    }

    /// <summary>
    /// 保全指示の作成（計画保全 E-30-20-01 は保全ロール、突発依頼 E-30-30-01 は全ユーザー可）
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<MaintenanceOrderResponse>> Create(
        MaintenanceOrderCreateRequest request, CancellationToken ct)
    {
        // 計画保全の作成は保全ロールのみ。突発依頼（Spot）は現場からも起票できる
        if (request.RequestType == MaintenanceRequestType.Planned
            && !User.IsInRole(Core.Constants.MesRoles.SystemAdmin)
            && !User.IsInRole(Core.Constants.MesRoles.Maintenance))
        {
            return Forbid();
        }
        if ((request.EquipmentId is null) == (request.ToolId is null))
        {
            return BadRequest(new ProblemDetails { Title = "対象設備IDまたは対象治工具IDのどちらか一方を指定してください。" });
        }
        if (request.EquipmentId is int equipmentId
            && !await db.Equipments.AnyAsync(e => e.Id == equipmentId, ct))
        {
            return BadRequest(new ProblemDetails { Title = "存在しない設備IDです。" });
        }
        if (request.ToolId is int toolId && !await db.Tools.AnyAsync(t => t.Id == toolId, ct))
        {
            return BadRequest(new ProblemDetails { Title = "存在しない治工具IDです。" });
        }
        if (request.ProcedureId is int procedureId
            && !await db.MaintenanceProcedures.AnyAsync(p => p.Id == procedureId && p.IsActive, ct))
        {
            return BadRequest(new ProblemDetails { Title = "存在しない（または無効な）手順書IDです。" });
        }

        MaintenancePlan? plan = null;
        if (request.MaintenancePlanId is int planId)
        {
            plan = await db.MaintenancePlans.FirstOrDefaultAsync(p => p.Id == planId, ct);
            if (plan is null)
            {
                return BadRequest(new ProblemDetails { Title = "存在しない保全計画IDです。" });
            }
            if (plan.Status is not MaintenancePlanStatus.Planned)
            {
                return Conflict(new ProblemDetails { Title = $"状態 '{plan.Status}' の保全計画からは指示を作成できません。" });
            }
            plan.Status = MaintenancePlanStatus.Ordered;
        }

        var order = new MaintenanceOrder
        {
            OrderNo = await numbering.NextMaintenanceNoAsync(ct),
            EquipmentId = request.EquipmentId,
            ToolId = request.ToolId,
            MaintenancePlanId = plan?.Id,
            ProcedureId = request.ProcedureId,
            ScheduledDate = request.ScheduledDate,
            RequestType = request.RequestType,
            Note = request.Note,
            CreatedByUserId = CurrentUserId,
        };
        db.MaintenanceOrders.Add(order);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Maintenance", "OrderCreate", nameof(MaintenanceOrder), order.Id.ToString(),
            detail: $"orderNo={order.OrderNo}, type={order.RequestType}", ct: ct);
        var saved = await BaseQuery().FirstAsync(o => o.Id == order.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, ToResponse(saved));
    }

    /// <summary>
    /// 保全実績の登録（E-40-30-01）。登録と同時に指示は完了。元計画は完了、
    /// 治工具メンテでresetToolLife指定時は寿命カウンタをリセットする（E-60-30）。
    /// </summary>
    [HttpPost("{id:int}/record")]
    public async Task<ActionResult<MaintenanceOrderResponse>> AddRecord(
        int id, MaintenanceRecordRequest request, CancellationToken ct)
    {
        var order = await db.MaintenanceOrders
            .Include(o => o.MaintenancePlan)
            .Include(o => o.Tool)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return NotFound();
        }
        if (order.Status != MaintenanceOrderStatus.Instructed)
        {
            return Conflict(new ProblemDetails { Title = $"状態 '{order.Status}' の保全指示には実績を登録できません。" });
        }
        if (request.ResetToolLife && order.Tool is null)
        {
            return BadRequest(new ProblemDetails { Title = "寿命リセットは治工具メンテナンスの指示でのみ指定できます。" });
        }

        db.MaintenanceRecords.Add(new MaintenanceRecord
        {
            MaintenanceOrderId = id,
            PerformedByUserId = CurrentUserId!,
            StartedAt = request.StartedAt,
            EndedAt = request.EndedAt,
            PartsUsed = request.PartsUsed,
            Result = request.Result,
            Note = request.Note,
        });
        order.Status = MaintenanceOrderStatus.Completed;
        if (order.MaintenancePlan is not null)
        {
            order.MaintenancePlan.Status = MaintenancePlanStatus.Completed;
        }
        if (request.ResetToolLife && order.Tool is not null)
        {
            order.Tool.LifeResetAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Maintenance", "RecordAdd", nameof(MaintenanceOrder), id.ToString(),
            detail: $"orderNo={order.OrderNo}, resetToolLife={request.ResetToolLife}", ct: ct);
        var saved = await BaseQuery().FirstAsync(o => o.Id == id, ct);
        return ToResponse(saved);
    }

    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = RoleGroups.MaintenanceManage)]
    public async Task<ActionResult<MaintenanceOrderResponse>> Cancel(int id, CancellationToken ct)
    {
        var order = await db.MaintenanceOrders.Include(o => o.MaintenancePlan)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return NotFound();
        }
        if (order.Status != MaintenanceOrderStatus.Instructed)
        {
            return Conflict(new ProblemDetails { Title = $"状態 '{order.Status}' の保全指示は取消できません。" });
        }
        order.Status = MaintenanceOrderStatus.Canceled;
        // 元計画を計画中に戻す（再指示できるように）
        if (order.MaintenancePlan is { Status: MaintenancePlanStatus.Ordered })
        {
            order.MaintenancePlan.Status = MaintenancePlanStatus.Planned;
        }
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Maintenance", "OrderCancel", nameof(MaintenanceOrder), id.ToString(), ct: ct);
        var saved = await BaseQuery().FirstAsync(o => o.Id == id, ct);
        return ToResponse(saved);
    }

    private IQueryable<MaintenanceOrder> BaseQuery() =>
        db.MaintenanceOrders.AsNoTracking()
            .Include(o => o.Equipment)
            .Include(o => o.Tool)
            .Include(o => o.Procedure)
            .Include(o => o.Records).ThenInclude(r => r.PerformedBy);

    private static MaintenanceOrderResponse ToResponse(MaintenanceOrder o) =>
        new(o.Id, o.OrderNo,
            o.EquipmentId, o.Equipment?.Name, o.ToolId, o.Tool?.Name,
            o.MaintenancePlanId, o.ProcedureId, o.Procedure?.ProcedureNo,
            o.ScheduledDate, o.RequestType, o.Status, o.Note, o.CreatedAt,
            o.Records.OrderBy(r => r.Id).Select(r => new MaintenanceRecordResponse(
                r.Id, r.PerformedByUserId, r.PerformedBy?.DisplayName,
                r.StartedAt, r.EndedAt, r.PartsUsed, r.Result, r.Note)).ToList());
}
