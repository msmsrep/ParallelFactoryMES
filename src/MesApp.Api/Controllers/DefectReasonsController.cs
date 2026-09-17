using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>不良理由マスタ（Spec.md 5.1 DefectReason。C-40-10-01）</summary>
[ApiController]
[Route("api/defect-reasons")]
[Authorize]
public class DefectReasonsController(MesAppDbContext db, IAuditLogger auditLogger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<DefectReasonResponse>>> List(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var query = db.DefectReasons.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(r => r.IsActive);
        }
        return await query.OrderBy(r => r.Code)
            .Select(r => new DefectReasonResponse(r.Id, r.Code, r.Name, r.Category, r.IsActive))
            .ToListAsync(ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<DefectReasonResponse>> Get(int id, CancellationToken ct)
    {
        var r = await db.DefectReasons.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return r is null ? NotFound() : new DefectReasonResponse(r.Id, r.Code, r.Name, r.Category, r.IsActive);
    }

    [HttpPost]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<DefectReasonResponse>> Create(DefectReasonRequest request, CancellationToken ct)
    {
        if (await db.DefectReasons.AnyAsync(r => r.Code == request.Code, ct))
        {
            return this.ConflictProblem($"不良理由コード '{request.Code}' は既に存在します。");
        }
        var r = new DefectReason { Code = request.Code, Name = request.Name, Category = request.Category };
        db.DefectReasons.Add(r);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Create", nameof(DefectReason), r.Id.ToString(),
            detail: $"code={r.Code}", ct: ct);
        return CreatedAtAction(nameof(Get), new { id = r.Id },
            new DefectReasonResponse(r.Id, r.Code, r.Name, r.Category, r.IsActive));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<DefectReasonResponse>> Update(
        int id, DefectReasonRequest request, CancellationToken ct)
    {
        var r = await db.DefectReasons.FindAsync([id], ct);
        if (r is null)
        {
            return NotFound();
        }
        if (await db.DefectReasons.AnyAsync(x => x.Code == request.Code && x.Id != id, ct))
        {
            return this.ConflictProblem($"不良理由コード '{request.Code}' は既に存在します。");
        }
        r.Code = request.Code;
        r.Name = request.Name;
        r.Category = request.Category;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Update", nameof(DefectReason), id.ToString(),
            detail: $"code={r.Code}", ct: ct);
        return new DefectReasonResponse(r.Id, r.Code, r.Name, r.Category, r.IsActive);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        var r = await db.DefectReasons.FindAsync([id], ct);
        if (r is null)
        {
            return NotFound();
        }
        r.IsActive = false;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Deactivate", nameof(DefectReason), id.ToString(),
            detail: $"code={r.Code}", ct: ct);
        return NoContent();
    }
}
