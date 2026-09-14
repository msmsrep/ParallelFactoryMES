using MesApp.Api.Policies;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>設備台帳/BOE（Spec.md 5.1 Equipment。E-10-10、I-10-20）</summary>
[ApiController]
[Route("api/equipments")]
[Authorize]
public class EquipmentsController(MesAppDbContext db, IAuditLogger auditLogger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<EquipmentResponse>>> List(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var query = db.Equipments.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(e => e.IsActive);
        }
        return await query.OrderBy(e => e.AssetNo).Include(e => e.WorkCenter)
            .Select(e => ToResponse(e)).ToListAsync(ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EquipmentResponse>> Get(int id, CancellationToken ct)
    {
        var e = await db.Equipments.AsNoTracking().Include(x => x.WorkCenter)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return e is null ? NotFound() : ToResponse(e);
    }

    [HttpPost]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<EquipmentResponse>> Create(EquipmentRequest request, CancellationToken ct)
    {
        if (await db.Equipments.AnyAsync(e => e.AssetNo == request.AssetNo, ct))
        {
            return Conflict(new ProblemDetails { Title = $"資産番号 '{request.AssetNo}' は既に存在します。" });
        }
        var workCenter = await FindWorkCenterAsync(request.WorkCenterId, ct);
        if (request.WorkCenterId is { } missing && workCenter is null)
        {
            return BadRequest(new ProblemDetails { Title = $"作業区（ID {missing}）が見つかりません。" });
        }
        if (WorkCenterHierarchyPolicy.CheckEquipmentPlacement(workCenter) is { } reason)
        {
            return BadRequest(new ProblemDetails { Title = reason });
        }
        var e = new Equipment
        {
            AssetNo = request.AssetNo,
            WorkCenterId = request.WorkCenterId,
            Name = request.Name,
            Site = request.Site,
            Status = request.Status,
            MaintenanceType = request.MaintenanceType,
            MaintenanceThreshold = request.MaintenanceThreshold,
            MaintenanceParts = request.MaintenanceParts,
        };
        db.Equipments.Add(e);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Create", nameof(Equipment), e.Id.ToString(),
            detail: $"assetNo={e.AssetNo}", ct: ct);
        return CreatedAtAction(nameof(Get), new { id = e.Id }, ToResponse(e, workCenter));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<EquipmentResponse>> Update(int id, EquipmentRequest request, CancellationToken ct)
    {
        var e = await db.Equipments.FindAsync([id], ct);
        if (e is null)
        {
            return NotFound();
        }
        if (await db.Equipments.AnyAsync(x => x.AssetNo == request.AssetNo && x.Id != id, ct))
        {
            return Conflict(new ProblemDetails { Title = $"資産番号 '{request.AssetNo}' は既に存在します。" });
        }
        var workCenter = await FindWorkCenterAsync(request.WorkCenterId, ct);
        if (request.WorkCenterId is { } missing && workCenter is null)
        {
            return BadRequest(new ProblemDetails { Title = $"作業区（ID {missing}）が見つかりません。" });
        }
        if (WorkCenterHierarchyPolicy.CheckEquipmentPlacement(workCenter) is { } reason)
        {
            return BadRequest(new ProblemDetails { Title = reason });
        }
        e.AssetNo = request.AssetNo;
        e.Name = request.Name;
        e.WorkCenterId = request.WorkCenterId;
        e.Site = request.Site;
        e.Status = request.Status;
        e.MaintenanceType = request.MaintenanceType;
        e.MaintenanceThreshold = request.MaintenanceThreshold;
        e.MaintenanceParts = request.MaintenanceParts;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Update", nameof(Equipment), id.ToString(),
            detail: $"assetNo={e.AssetNo}", ct: ct);
        return ToResponse(e, workCenter);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        var e = await db.Equipments.FindAsync([id], ct);
        if (e is null)
        {
            return NotFound();
        }
        e.IsActive = false;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Deactivate", nameof(Equipment), id.ToString(),
            detail: $"assetNo={e.AssetNo}", ct: ct);
        return NoContent();
    }

    private async Task<WorkCenter?> FindWorkCenterAsync(int? id, CancellationToken ct) =>
        id is { } value
            ? await db.WorkCenters.AsNoTracking().FirstOrDefaultAsync(w => w.Id == value, ct)
            : null;

    /// <summary>作業区は未設定でもよいため、コード・名称はnull許容のまま返す</summary>
    private static EquipmentResponse ToResponse(Equipment e, WorkCenter? workCenter = null)
    {
        var wc = workCenter ?? e.WorkCenter;
        return new(e.Id, e.AssetNo, e.Name, e.Site, e.Status,
            e.MaintenanceType, e.MaintenanceThreshold, e.MaintenanceParts, e.IsActive,
            e.WorkCenterId, wc?.Code, wc?.Name);
    }
}
