using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>治工具マスタ（Spec.md 5.1 Tool。E-60）</summary>
[ApiController]
[Route("api/tools")]
[Authorize]
public class ToolsController(MesAppDbContext db, IAuditLogger auditLogger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ToolResponse>>> List(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var query = db.Tools.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(t => t.IsActive);
        }
        return await query.OrderBy(t => t.Code).Select(t => ToResponse(t)).ToListAsync(ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ToolResponse>> Get(int id, CancellationToken ct)
    {
        var t = await db.Tools.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return t is null ? NotFound() : ToResponse(t);
    }

    [HttpPost]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<ToolResponse>> Create(ToolRequest request, CancellationToken ct)
    {
        if (await db.Tools.AnyAsync(t => t.Code == request.Code, ct))
        {
            return this.ConflictProblem($"治工具コード '{request.Code}' は既に存在します。");
        }
        var t = new Tool
        {
            Code = request.Code,
            Name = request.Name,
            ToolType = request.ToolType,
            LifeThresholdCount = request.LifeThresholdCount,
            LifeThresholdHours = request.LifeThresholdHours,
            Status = request.Status,
        };
        db.Tools.Add(t);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Create", nameof(Tool), t.Id.ToString(),
            detail: $"code={t.Code}", ct: ct);
        return CreatedAtAction(nameof(Get), new { id = t.Id }, ToResponse(t));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<ToolResponse>> Update(int id, ToolRequest request, CancellationToken ct)
    {
        var t = await db.Tools.FindAsync([id], ct);
        if (t is null)
        {
            return NotFound();
        }
        if (await db.Tools.AnyAsync(x => x.Code == request.Code && x.Id != id, ct))
        {
            return this.ConflictProblem($"治工具コード '{request.Code}' は既に存在します。");
        }
        t.Code = request.Code;
        t.Name = request.Name;
        t.ToolType = request.ToolType;
        t.LifeThresholdCount = request.LifeThresholdCount;
        t.LifeThresholdHours = request.LifeThresholdHours;
        t.Status = request.Status;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Update", nameof(Tool), id.ToString(),
            detail: $"code={t.Code}", ct: ct);
        return ToResponse(t);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        var t = await db.Tools.FindAsync([id], ct);
        if (t is null)
        {
            return NotFound();
        }
        t.IsActive = false;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Deactivate", nameof(Tool), id.ToString(),
            detail: $"code={t.Code}", ct: ct);
        return NoContent();
    }

    private static ToolResponse ToResponse(Tool t) =>
        new(t.Id, t.Code, t.Name, t.ToolType, t.LifeThresholdCount, t.LifeThresholdHours, t.Status, t.IsActive);
}
