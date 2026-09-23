using MesApp.Core.Localization;
using MesApp.Api.Localization;
using System.Security.Claims;
using MesApp.Api.Services;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Maintenance;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 保全計画（E-30-10：中長期・年次保全計画の作成・変更）
/// </summary>
[ApiController]
[Route("api/maintenance-plans")]
[Authorize]
public class MaintenancePlansController(
    MesAppDbContext db, IAuditLogger auditLogger, MaintenanceOrderService maintenanceOrders) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<MaintenancePlanResponse>>> List(
        [FromQuery] int? equipmentId = null,
        [FromQuery] int? planYear = null,
        [FromQuery] MaintenancePlanStatus? status = null,
        CancellationToken ct = default)
    {
        var query = db.MaintenancePlans.AsNoTracking().AsQueryable();
        if (equipmentId is not null)
        {
            query = query.Where(p => p.EquipmentId == equipmentId);
        }
        if (planYear is not null)
        {
            query = query.Where(p => p.PlanYear == planYear);
        }
        if (status is not null)
        {
            query = query.Where(p => p.Status == status);
        }
        return await query
            .OrderBy(p => p.ScheduledDate == null).ThenBy(p => p.ScheduledDate).ThenBy(p => p.Id)
            .Select(Projection)
            .ToListAsync(ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<MaintenancePlanResponse>> Get(int id, CancellationToken ct)
    {
        var plan = await db.MaintenancePlans.AsNoTracking()
            .Where(p => p.Id == id).Select(Projection).FirstOrDefaultAsync(ct);
        return plan is null ? NotFound() : plan;
    }

    [HttpPost]
    [Authorize(Roles = MesRoleGroups.MaintenanceManage)]
    public async Task<ActionResult<MaintenancePlanResponse>> Create(
        MaintenancePlanRequest request, CancellationToken ct)
    {
        // 判定・保存は実績CSV取込と共通（MaintenanceOrderService）
        var outcome = await maintenanceOrders.CreatePlanAsync(
            request, User.FindFirstValue(ClaimTypes.NameIdentifier), ct);
        if (outcome.Failed)
        {
            return this.BadRequestProblem(outcome.Error!);
        }
        var id = outcome.Value!.Id;
        return CreatedAtAction(nameof(Get), new { id }, await GetResponseAsync(id, ct));
    }

    /// <summary>計画の変更（E-30-10-03。指示発行前のみ）</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MaintenanceManage)]
    public async Task<ActionResult<MaintenancePlanResponse>> Update(
        int id, MaintenancePlanRequest request, CancellationToken ct)
    {
        var plan = await db.MaintenancePlans.FindAsync([id], ct);
        if (plan is null)
        {
            return NotFound();
        }
        if (plan.Status is not MaintenancePlanStatus.Planned)
        {
            return this.ConflictProblem(ApiText.T("状態 '{0}' の保全計画は変更できません。", EnumLabels.Of(plan.Status)));
        }
        if (!await db.Equipments.AnyAsync(e => e.Id == request.EquipmentId && e.IsActive, ct))
        {
            return this.BadRequestProblem(ApiText.T("存在しない（または無効な）設備IDです。"));
        }

        plan.EquipmentId = request.EquipmentId;
        plan.Category = request.Category;
        plan.PlanYear = request.PlanYear;
        plan.ScheduledDate = request.ScheduledDate;
        plan.CycleDays = request.CycleDays;
        plan.Note = request.Note;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Maintenance", "PlanUpdate", nameof(MaintenancePlan), id.ToString(), ct: ct);
        return await GetResponseAsync(id, ct);
    }

    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = MesRoleGroups.MaintenanceManage)]
    public async Task<ActionResult<MaintenancePlanResponse>> Cancel(int id, CancellationToken ct)
    {
        var outcome = await maintenanceOrders.CancelPlanAsync(id, ct);
        return outcome.Kind switch
        {
            OutcomeError.None => await GetResponseAsync(id, ct),
            OutcomeError.NotFound => NotFound(),
            _ => this.ConflictProblem(outcome.Error),
        };
    }

    private async Task<MaintenancePlanResponse> GetResponseAsync(int id, CancellationToken ct) =>
        await db.MaintenancePlans.AsNoTracking()
            .Where(p => p.Id == id).Select(Projection).FirstAsync(ct);

    private static readonly System.Linq.Expressions.Expression<
        Func<MaintenancePlan, MaintenancePlanResponse>> Projection =
        p => new MaintenancePlanResponse(
            p.Id, p.EquipmentId, p.Equipment!.AssetNo, p.Equipment!.Name,
            p.Category, p.PlanYear, p.ScheduledDate, p.CycleDays,
            p.Status, p.Note, p.CreatedAt);
}
