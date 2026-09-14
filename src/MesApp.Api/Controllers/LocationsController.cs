using MesApp.Api.Policies;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>ロケーションマスタ（Spec.md 5.1 Location。D-50-20-01）</summary>
[ApiController]
[Route("api/locations")]
[Authorize]
public class LocationsController(MesAppDbContext db, IAuditLogger auditLogger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<LocationResponse>>> List(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var query = db.Locations.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(l => l.IsActive);
        }
        return await query.OrderBy(l => l.Code).Include(l => l.WorkCenter)
            .Select(l => ToResponse(l))
            .ToListAsync(ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<LocationResponse>> Get(int id, CancellationToken ct)
    {
        var l = await db.Locations.AsNoTracking().Include(x => x.WorkCenter)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return l is null ? NotFound() : ToResponse(l);
    }

    [HttpPost]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<LocationResponse>> Create(LocationRequest request, CancellationToken ct)
    {
        if (await db.Locations.AnyAsync(l => l.Code == request.Code, ct))
        {
            return Conflict(new ProblemDetails { Title = $"ロケーションコード '{request.Code}' は既に存在します。" });
        }
        var workCenter = await FindWorkCenterAsync(request.WorkCenterId, ct);
        if (request.WorkCenterId is { } missing && workCenter is null)
        {
            return BadRequest(new ProblemDetails { Title = $"作業区（ID {missing}）が見つかりません。" });
        }
        if (WorkCenterHierarchyPolicy.CheckLocationPlacement(workCenter) is { } reason)
        {
            return BadRequest(new ProblemDetails { Title = reason });
        }
        var l = new Location
        {
            Code = request.Code,
            AreaType = request.AreaType,
            ShelfNo = request.ShelfNo,
            WorkCenterId = request.WorkCenterId,
        };
        db.Locations.Add(l);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Create", nameof(Location), l.Id.ToString(),
            detail: $"code={l.Code}", ct: ct);
        return CreatedAtAction(nameof(Get), new { id = l.Id }, ToResponse(l, workCenter));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<LocationResponse>> Update(int id, LocationRequest request, CancellationToken ct)
    {
        var l = await db.Locations.FindAsync([id], ct);
        if (l is null)
        {
            return NotFound();
        }
        if (await db.Locations.AnyAsync(x => x.Code == request.Code && x.Id != id, ct))
        {
            return Conflict(new ProblemDetails { Title = $"ロケーションコード '{request.Code}' は既に存在します。" });
        }
        var workCenter = await FindWorkCenterAsync(request.WorkCenterId, ct);
        if (request.WorkCenterId is { } missing && workCenter is null)
        {
            return BadRequest(new ProblemDetails { Title = $"作業区（ID {missing}）が見つかりません。" });
        }
        if (WorkCenterHierarchyPolicy.CheckLocationPlacement(workCenter) is { } reason)
        {
            return BadRequest(new ProblemDetails { Title = reason });
        }
        l.Code = request.Code;
        l.AreaType = request.AreaType;
        l.ShelfNo = request.ShelfNo;
        l.WorkCenterId = request.WorkCenterId;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Update", nameof(Location), id.ToString(),
            detail: $"code={l.Code}", ct: ct);
        return ToResponse(l, workCenter);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        var l = await db.Locations.FindAsync([id], ct);
        if (l is null)
        {
            return NotFound();
        }
        l.IsActive = false;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Deactivate", nameof(Location), id.ToString(),
            detail: $"code={l.Code}", ct: ct);
        return NoContent();
    }

    private async Task<WorkCenter?> FindWorkCenterAsync(int? id, CancellationToken ct) =>
        id is { } value
            ? await db.WorkCenters.AsNoTracking().FirstOrDefaultAsync(w => w.Id == value, ct)
            : null;

    /// <summary>作業区は未設定でもよいため、コード・名称はnull許容のまま返す</summary>
    private static LocationResponse ToResponse(Location l, WorkCenter? workCenter = null)
    {
        var wc = workCenter ?? l.WorkCenter;
        return new(l.Id, l.Code, l.AreaType, l.ShelfNo, l.IsActive, l.WorkCenterId, wc?.Code, wc?.Name);
    }
}
