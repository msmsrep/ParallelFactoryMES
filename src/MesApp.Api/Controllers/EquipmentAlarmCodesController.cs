using MesApp.Api.Localization;
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
/// 停止原因の対応表（Spec.md 5.1 EquipmentAlarmCode。B-40-20-02）。
/// 設備イベントのアラーム番号を停止原因に置き換える。設備を空にすると全設備共通で、設備ごとの定義が勝つ
/// </summary>
[ApiController]
[Route("api/equipment-alarm-codes")]
[Authorize]
public class EquipmentAlarmCodesController(MesAppDbContext db, IAuditLogger auditLogger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<EquipmentAlarmCodeResponse>>> List(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var query = db.EquipmentAlarmCodes.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(a => a.IsActive);
        }
        // 共通の定義を先に、続いて設備の資産番号の順
        return await query.Include(a => a.Equipment)
            .OrderBy(a => a.EquipmentId != null).ThenBy(a => a.Equipment!.AssetNo).ThenBy(a => a.AlarmCode)
            .Select(a => ToResponse(a))
            .ToListAsync(ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EquipmentAlarmCodeResponse>> Get(int id, CancellationToken ct)
    {
        var a = await db.EquipmentAlarmCodes.AsNoTracking().Include(x => x.Equipment)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return a is null ? NotFound() : ToResponse(a);
    }

    [HttpPost]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<EquipmentAlarmCodeResponse>> Create(EquipmentAlarmCodeRequest request, CancellationToken ct)
    {
        var alarmCode = request.AlarmCode.Trim();
        var equipment = await FindEquipmentAsync(request.EquipmentId, ct);
        if (request.EquipmentId is { } missing && equipment is null)
        {
            return this.BadRequestProblem(ApiText.T("設備（ID {0}）が見つかりません。", missing));
        }
        if (await EquipmentAlarmCodePolicy.ExistsAsync(db, request.EquipmentId, alarmCode, null, ct))
        {
            return this.ConflictProblem(ApiText.T("アラーム番号 '{0}' は既に登録されています。", alarmCode));
        }
        var a = new EquipmentAlarmCode
        {
            EquipmentId = request.EquipmentId,
            AlarmCode = alarmCode,
            StopCause = request.StopCause.Trim(),
        };
        db.EquipmentAlarmCodes.Add(a);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Create", nameof(EquipmentAlarmCode), a.Id.ToString(),
            detail: $"equipment={equipment?.AssetNo ?? "*"}, alarmCode={a.AlarmCode}", ct: ct);
        return CreatedAtAction(nameof(Get), new { id = a.Id }, ToResponse(a, equipment));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<EquipmentAlarmCodeResponse>> Update(
        int id, EquipmentAlarmCodeRequest request, CancellationToken ct)
    {
        var a = await db.EquipmentAlarmCodes.FindAsync([id], ct);
        if (a is null)
        {
            return NotFound();
        }
        var alarmCode = request.AlarmCode.Trim();
        var equipment = await FindEquipmentAsync(request.EquipmentId, ct);
        if (request.EquipmentId is { } missing && equipment is null)
        {
            return this.BadRequestProblem(ApiText.T("設備（ID {0}）が見つかりません。", missing));
        }
        if (await EquipmentAlarmCodePolicy.ExistsAsync(db, request.EquipmentId, alarmCode, id, ct))
        {
            return this.ConflictProblem(ApiText.T("アラーム番号 '{0}' は既に登録されています。", alarmCode));
        }
        a.EquipmentId = request.EquipmentId;
        a.AlarmCode = alarmCode;
        a.StopCause = request.StopCause.Trim();
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Update", nameof(EquipmentAlarmCode), id.ToString(),
            detail: $"equipment={equipment?.AssetNo ?? "*"}, alarmCode={a.AlarmCode}", ct: ct);
        return ToResponse(a, equipment);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public Task<IActionResult> Deactivate(int id, CancellationToken ct) =>
        this.DeactivateMasterAsync<EquipmentAlarmCode>(db, auditLogger, id, a => $"alarmCode={a.AlarmCode}", ct);

    private async Task<Equipment?> FindEquipmentAsync(int? id, CancellationToken ct) =>
        id is { } value
            ? await db.Equipments.AsNoTracking().FirstOrDefaultAsync(e => e.Id == value, ct)
            : null;

    private static EquipmentAlarmCodeResponse ToResponse(EquipmentAlarmCode a, Equipment? equipment = null)
    {
        var eq = equipment ?? a.Equipment;
        return new(a.Id, a.EquipmentId, eq?.AssetNo, eq?.Name, a.AlarmCode, a.StopCause, a.IsActive);
    }
}
