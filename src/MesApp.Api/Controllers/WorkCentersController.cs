using MesApp.Api.Policies;
using MesApp.Core.Abstractions;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>作業区／資源階層マスタ（Spec.md 5.1 WorkCenter。I-10-20-02）</summary>
[ApiController]
[Route("api/work-centers")]
[Authorize]
public class WorkCentersController(MesAppDbContext db, IAuditLogger auditLogger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<WorkCenterResponse>>> List(
        [FromQuery] WorkCenterLevel? level = null,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        var query = db.WorkCenters.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(w => w.IsActive);
        }
        if (level is { } l)
        {
            query = query.Where(w => w.Level == l);
        }
        var items = await query.OrderBy(w => w.Code)
            .Select(w => new WorkCenterResponse(
                w.Id, w.Code, w.Name, w.Level,
                w.ParentId, w.Parent!.Code, w.Parent.Name, w.IsActive))
            .ToListAsync(ct);
        // 上の段から順に並べると画面で階層のまま読める。Levelは文字列で保存しているため
        // DB側で並べるとアルファベット順（Area→Line→Plant）になるので、取得後に段の順へ並べ直す
        return items.OrderBy(w => w.Level).ThenBy(w => w.Code, StringComparer.Ordinal).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<WorkCenterResponse>> Get(int id, CancellationToken ct)
    {
        var w = await db.WorkCenters.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new WorkCenterResponse(
                x.Id, x.Code, x.Name, x.Level,
                x.ParentId, x.Parent!.Code, x.Parent.Name, x.IsActive))
            .FirstOrDefaultAsync(ct);
        return w is null ? NotFound() : w;
    }

    [HttpPost]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<WorkCenterResponse>> Create(WorkCenterRequest request, CancellationToken ct)
    {
        if (await db.WorkCenters.AnyAsync(w => w.Code == request.Code, ct))
        {
            return Conflict(new ProblemDetails { Title = $"作業区コード '{request.Code}' は既に存在します。" });
        }

        var all = await db.WorkCenters.AsNoTracking().ToListAsync(ct);
        var parent = request.ParentId is { } pid ? all.FirstOrDefault(x => x.Id == pid) : null;
        if (request.ParentId is { } missing && parent is null)
        {
            return BadRequest(new ProblemDetails { Title = $"上位の資源（ID {missing}）が見つかりません。" });
        }
        if (WorkCenterHierarchyPolicy.Check(request.Code, request.Level, parent, null, all) is { } reason)
        {
            return BadRequest(new ProblemDetails { Title = reason });
        }

        var w = new WorkCenter
        {
            Code = request.Code,
            Name = request.Name,
            Level = request.Level,
            ParentId = request.ParentId,
        };
        db.WorkCenters.Add(w);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Create", nameof(WorkCenter), w.Id.ToString(),
            detail: $"code={w.Code}", ct: ct);
        return CreatedAtAction(nameof(Get), new { id = w.Id },
            new WorkCenterResponse(w.Id, w.Code, w.Name, w.Level,
                w.ParentId, parent?.Code, parent?.Name, w.IsActive));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<WorkCenterResponse>> Update(int id, WorkCenterRequest request, CancellationToken ct)
    {
        var w = await db.WorkCenters.FindAsync([id], ct);
        if (w is null)
        {
            return NotFound();
        }
        if (await db.WorkCenters.AnyAsync(x => x.Code == request.Code && x.Id != id, ct))
        {
            return Conflict(new ProblemDetails { Title = $"作業区コード '{request.Code}' は既に存在します。" });
        }

        var all = await db.WorkCenters.AsNoTracking().ToListAsync(ct);
        var parent = request.ParentId is { } pid ? all.FirstOrDefault(x => x.Id == pid) : null;
        if (request.ParentId is { } missing && parent is null)
        {
            return BadRequest(new ProblemDetails { Title = $"上位の資源（ID {missing}）が見つかりません。" });
        }
        if (WorkCenterHierarchyPolicy.Check(request.Code, request.Level, parent, id, all) is { } reason)
        {
            return BadRequest(new ProblemDetails { Title = reason });
        }
        // 段を変えると配下の親子関係が崩れるため、子がいる間は段を変えさせない
        if (w.Level != request.Level && all.Any(x => x.ParentId == id))
        {
            return BadRequest(new ProblemDetails
            {
                Title = $"'{w.Code}' には下位の資源があるため、段を変更できません" +
                        "（下位の資源を付け替えてから変更してください）。",
            });
        }

        w.Code = request.Code;
        w.Name = request.Name;
        w.Level = request.Level;
        w.ParentId = request.ParentId;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Update", nameof(WorkCenter), id.ToString(),
            detail: $"code={w.Code}", ct: ct);
        return new WorkCenterResponse(w.Id, w.Code, w.Name, w.Level,
            w.ParentId, parent?.Code, parent?.Name, w.IsActive);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        var w = await db.WorkCenters.FindAsync([id], ct);
        if (w is null)
        {
            return NotFound();
        }
        // 有効な下位が残ったまま上位を無効化すると、階層を辿れない資源ができる
        if (await db.WorkCenters.AnyAsync(x => x.ParentId == id && x.IsActive, ct))
        {
            return Conflict(new ProblemDetails
            {
                Title = $"'{w.Code}' には有効な下位の資源があるため、無効化できません" +
                        "（下位の資源を先に無効化するか、付け替えてください）。",
            });
        }
        w.IsActive = false;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Deactivate", nameof(WorkCenter), id.ToString(),
            detail: $"code={w.Code}", ct: ct);
        return NoContent();
    }
}
