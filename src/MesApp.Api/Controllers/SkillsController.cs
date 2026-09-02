using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>スキル・資格マスタ（Spec.md 5.1 SkillMaster。F-20-10-01）</summary>
[ApiController]
[Route("api/skills")]
[Authorize]
public class SkillsController(MesAppDbContext db, IAuditLogger auditLogger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<SkillResponse>>> List(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var query = db.Skills.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(s => s.IsActive);
        }
        return await query.OrderBy(s => s.Code)
            .Select(s => new SkillResponse(s.Id, s.Code, s.Name, s.Type, s.RequiresExpiry, s.IsActive))
            .ToListAsync(ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SkillResponse>> Get(int id, CancellationToken ct)
    {
        var s = await db.Skills.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return s is null ? NotFound()
            : new SkillResponse(s.Id, s.Code, s.Name, s.Type, s.RequiresExpiry, s.IsActive);
    }

    [HttpPost]
    [Authorize(Roles = MesRoleGroups.UserAdmin)]
    public async Task<ActionResult<SkillResponse>> Create(SkillRequest request, CancellationToken ct)
    {
        if (await db.Skills.AnyAsync(s => s.Code == request.Code, ct))
        {
            return Conflict(new ProblemDetails { Title = $"スキル・資格コード '{request.Code}' は既に存在します。" });
        }
        var s = new SkillMaster
        {
            Code = request.Code,
            Name = request.Name,
            Type = request.Type,
            RequiresExpiry = request.RequiresExpiry,
        };
        db.Skills.Add(s);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Create", nameof(SkillMaster), s.Id.ToString(),
            detail: $"code={s.Code}", ct: ct);
        return CreatedAtAction(nameof(Get), new { id = s.Id },
            new SkillResponse(s.Id, s.Code, s.Name, s.Type, s.RequiresExpiry, s.IsActive));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = MesRoleGroups.UserAdmin)]
    public async Task<ActionResult<SkillResponse>> Update(int id, SkillRequest request, CancellationToken ct)
    {
        var s = await db.Skills.FindAsync([id], ct);
        if (s is null)
        {
            return NotFound();
        }
        if (await db.Skills.AnyAsync(x => x.Code == request.Code && x.Id != id, ct))
        {
            return Conflict(new ProblemDetails { Title = $"スキル・資格コード '{request.Code}' は既に存在します。" });
        }
        s.Code = request.Code;
        s.Name = request.Name;
        s.Type = request.Type;
        s.RequiresExpiry = request.RequiresExpiry;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Update", nameof(SkillMaster), id.ToString(),
            detail: $"code={s.Code}", ct: ct);
        return new SkillResponse(s.Id, s.Code, s.Name, s.Type, s.RequiresExpiry, s.IsActive);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = MesRoleGroups.UserAdmin)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        var s = await db.Skills.FindAsync([id], ct);
        if (s is null)
        {
            return NotFound();
        }
        s.IsActive = false;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Deactivate", nameof(SkillMaster), id.ToString(),
            detail: $"code={s.Code}", ct: ct);
        return NoContent();
    }
}
