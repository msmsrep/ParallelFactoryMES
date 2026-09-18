using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// チェックリストマスタ（Spec.md 5.1 Checklist。B-30-10。HSE項目 G-20-20-02/G-30-20-03 もカテゴリHseで登録可能）
/// </summary>
[ApiController]
[Route("api/checklists")]
[Authorize]
public class ChecklistsController(MesAppDbContext db, IAuditLogger auditLogger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ChecklistResponse>>> List(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var query = db.Checklists.AsNoTracking().Include(c => c.Items);
        var filtered = includeInactive ? query : query.Where(c => c.IsActive);
        var lists = await filtered.OrderBy(c => c.Code).ToListAsync(ct);
        return lists.Select(ToResponse).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ChecklistResponse>> Get(int id, CancellationToken ct)
    {
        var c = await db.Checklists.AsNoTracking().Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return c is null ? NotFound() : ToResponse(c);
    }

    [HttpPost]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<ChecklistResponse>> Create(ChecklistRequest request, CancellationToken ct)
    {
        if (await db.Checklists.AnyAsync(c => c.Code == request.Code, ct))
        {
            return this.ConflictProblem($"チェックリストコード '{request.Code}' は既に存在します。");
        }
        if (request.Items.GroupBy(i => i.Sequence).Any(g => g.Count() > 1))
        {
            return this.BadRequestProblem("項目の表示順が重複しています。");
        }

        var c = new Checklist
        {
            Code = request.Code,
            Name = request.Name,
            Category = request.Category,
            Items = request.Items
                .Select(i => new ChecklistItem { Sequence = i.Sequence, Text = i.Text, IsRequired = i.IsRequired })
                .ToList(),
        };
        db.Checklists.Add(c);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Create", nameof(Checklist), c.Id.ToString(),
            detail: $"code={c.Code}", ct: ct);
        return CreatedAtAction(nameof(Get), new { id = c.Id }, ToResponse(c));
    }

    /// <summary>チェックリストの更新（項目は一括置換）</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<ChecklistResponse>> Update(int id, ChecklistRequest request, CancellationToken ct)
    {
        var c = await db.Checklists.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null)
        {
            return NotFound();
        }
        if (await db.Checklists.AnyAsync(x => x.Code == request.Code && x.Id != id, ct))
        {
            return this.ConflictProblem($"チェックリストコード '{request.Code}' は既に存在します。");
        }
        if (request.Items.GroupBy(i => i.Sequence).Any(g => g.Count() > 1))
        {
            return this.BadRequestProblem("項目の表示順が重複しています。");
        }

        c.Code = request.Code;
        c.Name = request.Name;
        c.Category = request.Category;
        c.Items.Clear();
        c.Items.AddRange(request.Items
            .Select(i => new ChecklistItem { Sequence = i.Sequence, Text = i.Text, IsRequired = i.IsRequired }));
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Update", nameof(Checklist), id.ToString(),
            detail: $"code={c.Code}", ct: ct);
        return ToResponse(c);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public Task<IActionResult> Deactivate(int id, CancellationToken ct) =>
        this.DeactivateMasterAsync<Checklist>(db, auditLogger, id, c => $"code={c.Code}", ct);

    private static ChecklistResponse ToResponse(Checklist c) =>
        new(c.Id, c.Code, c.Name, c.Category, c.IsActive,
            c.Items.OrderBy(i => i.Sequence)
                .Select(i => new ChecklistItemResponse(i.Id, i.Sequence, i.Text, i.IsRequired))
                .ToList());
}
