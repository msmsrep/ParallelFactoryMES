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
/// 作業手順書（SOP。Spec.md 5.1 WorkProcedure。I-30-40-01 登録、I-30-40-02 承認、B-10-30-03 閲覧）。
/// 工順（BOP）から紐付けて使う（I-30-20-12）。更新のたびに版数を上げる
/// （保全手順書 E-10-20 と同じ扱い）。
/// </summary>
[ApiController]
[Route("api/work-procedures")]
[Authorize]
public class WorkProceduresController(MesAppDbContext db, IAuditLogger auditLogger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<WorkProcedureResponse>>> List(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var query = db.WorkProcedures.AsNoTracking().AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(p => p.IsActive);
        }
        return await query.OrderBy(p => p.ProcedureNo).Select(p => ToResponse(p)).ToListAsync(ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<WorkProcedureResponse>> Get(int id, CancellationToken ct)
    {
        var procedure = await db.WorkProcedures.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        return procedure is null ? NotFound() : ToResponse(procedure);
    }

    [HttpPost]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<WorkProcedureResponse>> Create(
        WorkProcedureRequest request, CancellationToken ct)
    {
        if (await db.WorkProcedures.AnyAsync(p => p.ProcedureNo == request.ProcedureNo, ct))
        {
            return Conflict(new ProblemDetails { Title = $"手順書番号 '{request.ProcedureNo}' は既に存在します。" });
        }
        if (Validate(request) is { } error)
        {
            return BadRequest(new ProblemDetails { Title = error });
        }

        var procedure = new WorkProcedure
        {
            ProcedureNo = request.ProcedureNo,
            Title = request.Title,
            Steps = request.Steps,
            Reference = request.Reference,
        };
        db.WorkProcedures.Add(procedure);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Create", nameof(WorkProcedure), procedure.Id.ToString(),
            detail: $"procedureNo={procedure.ProcedureNo}", ct: ct);
        return CreatedAtAction(nameof(Get), new { id = procedure.Id }, ToResponse(procedure));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<WorkProcedureResponse>> Update(
        int id, WorkProcedureRequest request, CancellationToken ct)
    {
        var procedure = await db.WorkProcedures.FindAsync([id], ct);
        if (procedure is null)
        {
            return NotFound();
        }
        if (await db.WorkProcedures.AnyAsync(p => p.ProcedureNo == request.ProcedureNo && p.Id != id, ct))
        {
            return Conflict(new ProblemDetails { Title = $"手順書番号 '{request.ProcedureNo}' は既に存在します。" });
        }
        if (Validate(request) is { } error)
        {
            return BadRequest(new ProblemDetails { Title = error });
        }

        procedure.ProcedureNo = request.ProcedureNo;
        procedure.Title = request.Title;
        procedure.Steps = request.Steps;
        procedure.Reference = request.Reference;
        procedure.Version++; // 改訂（I-30-40-02）
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Update", nameof(WorkProcedure), id.ToString(),
            detail: $"procedureNo={procedure.ProcedureNo}, version={procedure.Version}", ct: ct);
        return ToResponse(procedure);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        var procedure = await db.WorkProcedures.FindAsync([id], ct);
        if (procedure is null)
        {
            return NotFound();
        }
        // 工順から参照されている手順書を無効化すると、作業者が手順を辿れない作業指示ができる。
        // 判定は MasterDeactivationPolicy に置き、CSV取込と同じ条件・同じ文面で弾く
        var referencing = await db.Routings.AsNoTracking()
            .Where(r => r.WorkProcedureId == id)
            .Select(r => r.Product!.Code)
            .Distinct()
            .ToListAsync(ct);
        if (MasterDeactivationPolicy.CheckWorkProcedure(procedure.ProcedureNo, referencing) is { } error)
        {
            return Conflict(new ProblemDetails { Title = error });
        }

        procedure.IsActive = false;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Deactivate", nameof(WorkProcedure), id.ToString(),
            detail: $"procedureNo={procedure.ProcedureNo}", ct: ct);
        return NoContent();
    }

    /// <summary>手順が辿れる形になっているかを確認する。問題があれば日本語の理由を返す</summary>
    private static string? Validate(WorkProcedureRequest request)
    {
        // 手順書の作成自体はMESの対象外（I-30-30-01）なので、本文と所在のどちらかがあればよい。
        // どちらも無いと「番号と表題だけの手順書」になり、作業者が何も参照できない
        if (string.IsNullOrWhiteSpace(request.Steps) && string.IsNullOrWhiteSpace(request.Reference))
        {
            return "手順ステップか、手順書の所在のどちらかを入力してください。";
        }
        return null;
    }

    private static WorkProcedureResponse ToResponse(WorkProcedure p) =>
        new(p.Id, p.ProcedureNo, p.Title, p.Steps, p.Reference, p.Version, p.IsActive);
}
