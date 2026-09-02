using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Maintenance;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 保全手順書（E-10-20 作成・管理、E-20-30-04〜06 見直し）。更新のたびに版数を上げる。
/// </summary>
[ApiController]
[Route("api/maintenance-procedures")]
[Authorize]
public class MaintenanceProceduresController(MesAppDbContext db, IAuditLogger auditLogger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<MaintenanceProcedureResponse>>> List(
        [FromQuery] bool includeInactive = false,
        [FromQuery] int? equipmentId = null,
        [FromQuery] int? toolId = null,
        CancellationToken ct = default)
    {
        var query = db.MaintenanceProcedures.AsNoTracking().AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(p => p.IsActive);
        }
        if (equipmentId is not null)
        {
            query = query.Where(p => p.TargetEquipmentId == equipmentId);
        }
        if (toolId is not null)
        {
            query = query.Where(p => p.TargetToolId == toolId);
        }
        return await query.OrderBy(p => p.ProcedureNo).Select(Projection).ToListAsync(ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<MaintenanceProcedureResponse>> Get(int id, CancellationToken ct)
    {
        var procedure = await db.MaintenanceProcedures.AsNoTracking()
            .Where(p => p.Id == id).Select(Projection).FirstOrDefaultAsync(ct);
        return procedure is null ? NotFound() : procedure;
    }

    [HttpPost]
    [Authorize(Roles = MesRoleGroups.MaintenanceManage)]
    public async Task<ActionResult<MaintenanceProcedureResponse>> Create(
        MaintenanceProcedureRequest request, CancellationToken ct)
    {
        if (await db.MaintenanceProcedures.AnyAsync(p => p.ProcedureNo == request.ProcedureNo, ct))
        {
            return Conflict(new ProblemDetails { Title = $"手順書番号 '{request.ProcedureNo}' は既に存在します。" });
        }
        var error = await ValidateTargetsAsync(request, ct);
        if (error is not null)
        {
            return BadRequest(new ProblemDetails { Title = error });
        }

        var procedure = new MaintenanceProcedure
        {
            ProcedureNo = request.ProcedureNo,
            Title = request.Title,
            TargetEquipmentId = request.TargetEquipmentId,
            TargetToolId = request.TargetToolId,
            Steps = request.Steps,
        };
        db.MaintenanceProcedures.Add(procedure);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Maintenance", "ProcedureCreate", nameof(MaintenanceProcedure),
            procedure.Id.ToString(), detail: $"procedureNo={procedure.ProcedureNo}", ct: ct);
        return CreatedAtAction(nameof(Get), new { id = procedure.Id }, await GetResponseAsync(procedure.Id, ct));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MaintenanceManage)]
    public async Task<ActionResult<MaintenanceProcedureResponse>> Update(
        int id, MaintenanceProcedureRequest request, CancellationToken ct)
    {
        var procedure = await db.MaintenanceProcedures.FindAsync([id], ct);
        if (procedure is null)
        {
            return NotFound();
        }
        if (await db.MaintenanceProcedures.AnyAsync(p => p.ProcedureNo == request.ProcedureNo && p.Id != id, ct))
        {
            return Conflict(new ProblemDetails { Title = $"手順書番号 '{request.ProcedureNo}' は既に存在します。" });
        }
        var error = await ValidateTargetsAsync(request, ct);
        if (error is not null)
        {
            return BadRequest(new ProblemDetails { Title = error });
        }

        procedure.ProcedureNo = request.ProcedureNo;
        procedure.Title = request.Title;
        procedure.TargetEquipmentId = request.TargetEquipmentId;
        procedure.TargetToolId = request.TargetToolId;
        procedure.Steps = request.Steps;
        procedure.Version++; // 見直しの版数管理（E-20-30-06）
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Maintenance", "ProcedureUpdate", nameof(MaintenanceProcedure),
            id.ToString(), detail: $"procedureNo={procedure.ProcedureNo}, version={procedure.Version}", ct: ct);
        return await GetResponseAsync(id, ct);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MaintenanceManage)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        var procedure = await db.MaintenanceProcedures.FindAsync([id], ct);
        if (procedure is null)
        {
            return NotFound();
        }
        procedure.IsActive = false;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Maintenance", "ProcedureDeactivate", nameof(MaintenanceProcedure),
            id.ToString(), detail: $"procedureNo={procedure.ProcedureNo}", ct: ct);
        return NoContent();
    }

    private async Task<string?> ValidateTargetsAsync(MaintenanceProcedureRequest request, CancellationToken ct)
    {
        if (request.TargetEquipmentId is int equipmentId
            && !await db.Equipments.AnyAsync(e => e.Id == equipmentId, ct))
        {
            return "存在しない対象設備IDです。";
        }
        if (request.TargetToolId is int toolId && !await db.Tools.AnyAsync(t => t.Id == toolId, ct))
        {
            return "存在しない対象治工具IDです。";
        }
        return null;
    }

    private async Task<MaintenanceProcedureResponse> GetResponseAsync(int id, CancellationToken ct) =>
        await db.MaintenanceProcedures.AsNoTracking()
            .Where(p => p.Id == id).Select(Projection).FirstAsync(ct);

    private static readonly System.Linq.Expressions.Expression<
        Func<MaintenanceProcedure, MaintenanceProcedureResponse>> Projection =
        p => new MaintenanceProcedureResponse(
            p.Id, p.ProcedureNo, p.Title,
            p.TargetEquipmentId, p.TargetEquipment!.Name,
            p.TargetToolId, p.TargetTool!.Name,
            p.Steps, p.Version, p.IsActive);
}
