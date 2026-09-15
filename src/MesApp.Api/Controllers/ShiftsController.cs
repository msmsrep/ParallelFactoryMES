using MesApp.Api.Policies;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 勤務シフト（直）マスタ（Spec.md 5.1 Shift。F-10-10-01）。
/// 3.9節の製造日（業務日付）が夜勤を前提にしているのに対し、
/// 「その実績がどの直のものか」を表す定義を与える。
/// 時間帯が重なる直は登録できない（重なると実績の直が一意に決まらない）。
/// </summary>
[ApiController]
[Route("api/shifts")]
[Authorize]
public class ShiftsController(MesAppDbContext db, IAuditLogger auditLogger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ShiftResponse>>> List(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var query = db.Shifts.AsNoTracking().AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(s => s.IsActive);
        }
        var shifts = await query.ToListAsync(ct);
        // 直は時間帯で並べたほうが読みやすい（夜勤が最後に来る）
        return shifts
            .OrderBy(s => s.StartTime).ThenBy(s => s.Code, StringComparer.Ordinal)
            .Select(ToResponse)
            .ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ShiftResponse>> Get(int id, CancellationToken ct)
    {
        var shift = await db.Shifts.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        return shift is null ? NotFound() : ToResponse(shift);
    }

    [HttpPost]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<ShiftResponse>> Create(ShiftRequest request, CancellationToken ct)
    {
        if (await db.Shifts.AnyAsync(s => s.Code == request.Code, ct))
        {
            return Conflict(new ProblemDetails { Title = $"シフトコード '{request.Code}' は既に存在します。" });
        }
        if (await CheckScheduleAsync(request, null, ct) is { } error)
        {
            return BadRequest(new ProblemDetails { Title = error });
        }

        var shift = new Shift
        {
            Code = request.Code,
            Name = request.Name,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
        };
        db.Shifts.Add(shift);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Create", nameof(Shift), shift.Id.ToString(),
            detail: $"code={shift.Code}", ct: ct);
        return CreatedAtAction(nameof(Get), new { id = shift.Id }, ToResponse(shift));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<ShiftResponse>> Update(int id, ShiftRequest request, CancellationToken ct)
    {
        var shift = await db.Shifts.FindAsync([id], ct);
        if (shift is null)
        {
            return NotFound();
        }
        if (await db.Shifts.AnyAsync(s => s.Code == request.Code && s.Id != id, ct))
        {
            return Conflict(new ProblemDetails { Title = $"シフトコード '{request.Code}' は既に存在します。" });
        }
        if (await CheckScheduleAsync(request, id, ct) is { } error)
        {
            return BadRequest(new ProblemDetails { Title = error });
        }

        shift.Code = request.Code;
        shift.Name = request.Name;
        shift.StartTime = request.StartTime;
        shift.EndTime = request.EndTime;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Update", nameof(Shift), id.ToString(),
            detail: $"code={shift.Code}", ct: ct);
        return ToResponse(shift);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        var shift = await db.Shifts.FindAsync([id], ct);
        if (shift is null)
        {
            return NotFound();
        }
        // 所属する直として使われている間は無効化しない（従業員の所属が宙に浮く）
        var assigned = await db.Users.CountAsync(u => u.ShiftId == id && u.IsActive, ct);
        if (assigned > 0)
        {
            return Conflict(new ProblemDetails
            {
                Title = $"直 '{shift.Code}' は在籍中の従業員 {assigned} 名の所属になっているため無効化できません。",
            });
        }

        shift.IsActive = false;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Deactivate", nameof(Shift), id.ToString(),
            detail: $"code={shift.Code}", ct: ct);
        return NoContent();
    }

    /// <summary>時間帯の整合（自分以外の有効な直との重なり）を確認する</summary>
    private async Task<string?> CheckScheduleAsync(ShiftRequest request, int? excludeId, CancellationToken ct)
    {
        var others = await db.Shifts.AsNoTracking()
            .Where(s => s.IsActive && (excludeId == null || s.Id != excludeId))
            .ToListAsync(ct);
        return ShiftSchedulePolicy.Check(request.Code, request.StartTime, request.EndTime, others);
    }

    private static ShiftResponse ToResponse(Shift s) =>
        new(s.Id, s.Code, s.Name, s.StartTime, s.EndTime,
            ShiftSchedulePolicy.CrossesMidnight(s.StartTime, s.EndTime),
            ShiftSchedulePolicy.Format(s.StartTime, s.EndTime),
            s.IsActive);
}
