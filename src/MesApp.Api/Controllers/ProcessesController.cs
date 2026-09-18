using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>工程マスタ（Spec.md 5.1 Process）</summary>
[ApiController]
[Route("api/processes")]
[Authorize]
public class ProcessesController(MesAppDbContext db, IAuditLogger auditLogger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ProcessResponse>>> List(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var query = db.Processes.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(p => p.IsActive);
        }
        return await query.OrderBy(p => p.Code)
            .Select(p => new ProcessResponse(p.Id, p.Code, p.Name, p.Category, p.IsActive))
            .ToListAsync(ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProcessResponse>> Get(int id, CancellationToken ct)
    {
        var p = await db.Processes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return p is null ? NotFound() : new ProcessResponse(p.Id, p.Code, p.Name, p.Category, p.IsActive);
    }

    [HttpPost]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<ProcessResponse>> Create(ProcessRequest request, CancellationToken ct)
    {
        if (await db.Processes.AnyAsync(p => p.Code == request.Code, ct))
        {
            return this.ConflictProblem($"工程コード '{request.Code}' は既に存在します。");
        }
        var p = new ProcessMaster { Code = request.Code, Name = request.Name, Category = request.Category };
        db.Processes.Add(p);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Create", nameof(ProcessMaster), p.Id.ToString(),
            detail: $"code={p.Code}", ct: ct);
        return CreatedAtAction(nameof(Get), new { id = p.Id },
            new ProcessResponse(p.Id, p.Code, p.Name, p.Category, p.IsActive));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<ProcessResponse>> Update(int id, ProcessRequest request, CancellationToken ct)
    {
        var p = await db.Processes.FindAsync([id], ct);
        if (p is null)
        {
            return NotFound();
        }
        if (await db.Processes.AnyAsync(x => x.Code == request.Code && x.Id != id, ct))
        {
            return this.ConflictProblem($"工程コード '{request.Code}' は既に存在します。");
        }
        p.Code = request.Code;
        p.Name = request.Name;
        p.Category = request.Category;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Update", nameof(ProcessMaster), id.ToString(),
            detail: $"code={p.Code}", ct: ct);
        return new ProcessResponse(p.Id, p.Code, p.Name, p.Category, p.IsActive);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public Task<IActionResult> Deactivate(int id, CancellationToken ct) =>
        this.DeactivateMasterAsync<ProcessMaster>(db, auditLogger, id, p => $"code={p.Code}", ct);
}
