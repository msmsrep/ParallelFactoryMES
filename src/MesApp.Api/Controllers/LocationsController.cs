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
        return await query.OrderBy(l => l.Code)
            .Select(l => new LocationResponse(l.Id, l.Code, l.AreaType, l.ShelfNo, l.IsActive))
            .ToListAsync(ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<LocationResponse>> Get(int id, CancellationToken ct)
    {
        var l = await db.Locations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return l is null ? NotFound() : new LocationResponse(l.Id, l.Code, l.AreaType, l.ShelfNo, l.IsActive);
    }

    [HttpPost]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<LocationResponse>> Create(LocationRequest request, CancellationToken ct)
    {
        if (await db.Locations.AnyAsync(l => l.Code == request.Code, ct))
        {
            return Conflict(new ProblemDetails { Title = $"ロケーションコード '{request.Code}' は既に存在します。" });
        }
        var l = new Location { Code = request.Code, AreaType = request.AreaType, ShelfNo = request.ShelfNo };
        db.Locations.Add(l);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Create", nameof(Location), l.Id.ToString(),
            detail: $"code={l.Code}", ct: ct);
        return CreatedAtAction(nameof(Get), new { id = l.Id },
            new LocationResponse(l.Id, l.Code, l.AreaType, l.ShelfNo, l.IsActive));
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
        l.Code = request.Code;
        l.AreaType = request.AreaType;
        l.ShelfNo = request.ShelfNo;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Update", nameof(Location), id.ToString(),
            detail: $"code={l.Code}", ct: ct);
        return new LocationResponse(l.Id, l.Code, l.AreaType, l.ShelfNo, l.IsActive);
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
}
