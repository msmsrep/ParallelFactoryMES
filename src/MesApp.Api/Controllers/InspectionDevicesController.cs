using MesApp.Api.Localization;
using MesApp.Api.Services;
using System.Security.Claims;
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

/// <summary>
/// 検査機・測定器マスタと校正管理（Spec.md 5.1 InspectionDevice。C-20-50-03）。
/// 校正期限を過ぎた機器で測った結果は品質保証の根拠にならないため、
/// 期限を管理し、検査実績の登録時に判定する（判定は InspectionDeviceCalibrationPolicy）。
/// </summary>
[ApiController]
[Route("api/inspection-devices")]
[Authorize]
public class InspectionDevicesController(
    MesAppDbContext db, IAuditLogger auditLogger, IBusinessDateService businessDate,
    InspectionDeviceService devices) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<InspectionDeviceResponse>>> List(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var query = db.InspectionDevices.AsNoTracking().AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(d => d.IsActive);
        }
        var devices = await query.OrderBy(d => d.Code).ToListAsync(ct);
        return devices.Select(ToResponse).ToList();
    }

    /// <summary>
    /// 校正期限が近い・過ぎている検査機（C-20-50-03 有効期限確認）。
    /// 期限を設定していない機器は対象にしない（判定の基準が無いため）
    /// </summary>
    [HttpGet("expiring")]
    public async Task<ActionResult<List<InspectionDeviceResponse>>> Expiring(
        [FromQuery] int withinDays = 30, CancellationToken ct = default)
    {
        var today = businessDate.Today;
        var threshold = today.AddDays(withinDays);
        var devices = await db.InspectionDevices.AsNoTracking()
            .Where(d => d.IsActive && d.CalibrationDueOn != null && d.CalibrationDueOn <= threshold)
            .OrderBy(d => d.CalibrationDueOn)
            .ToListAsync(ct);
        return devices.Select(ToResponse).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<InspectionDeviceResponse>> Get(int id, CancellationToken ct)
    {
        var device = await db.InspectionDevices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        return device is null ? NotFound() : ToResponse(device);
    }

    [HttpGet("{id:int}/calibrations")]
    public async Task<ActionResult<List<InspectionDeviceCalibrationResponse>>> Calibrations(
        int id, CancellationToken ct)
    {
        if (!await db.InspectionDevices.AnyAsync(d => d.Id == id, ct))
        {
            return NotFound();
        }
        return await db.InspectionDeviceCalibrations.AsNoTracking()
            .Where(c => c.InspectionDeviceId == id)
            .OrderByDescending(c => c.CalibratedOn).ThenByDescending(c => c.Id)
            .Select(c => new InspectionDeviceCalibrationResponse(
                c.Id, c.InspectionDeviceId, c.CalibratedOn, c.NextDueOn, c.Result,
                c.PerformedBy!.DisplayName, c.CreatedAt))
            .ToListAsync(ct);
    }

    [HttpPost]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<InspectionDeviceResponse>> Create(
        InspectionDeviceRequest request, CancellationToken ct)
    {
        if (await db.InspectionDevices.AnyAsync(d => d.Code == request.Code, ct))
        {
            return this.ConflictProblem(ApiText.T("検査機コード '{0}' は既に存在します。", request.Code));
        }

        var device = new InspectionDevice();
        Apply(device, request);
        db.InspectionDevices.Add(device);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Create", nameof(InspectionDevice), device.Id.ToString(),
            detail: $"code={device.Code}", ct: ct);
        return CreatedAtAction(nameof(Get), new { id = device.Id }, ToResponse(device));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<InspectionDeviceResponse>> Update(
        int id, InspectionDeviceRequest request, CancellationToken ct)
    {
        var device = await db.InspectionDevices.FindAsync([id], ct);
        if (device is null)
        {
            return NotFound();
        }
        if (await db.InspectionDevices.AnyAsync(d => d.Code == request.Code && d.Id != id, ct))
        {
            return this.ConflictProblem(ApiText.T("検査機コード '{0}' は既に存在します。", request.Code));
        }

        Apply(device, request);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Update", nameof(InspectionDevice), id.ToString(),
            detail: $"code={device.Code}", ct: ct);
        return ToResponse(device);
    }

    /// <summary>
    /// 校正の実施を記録する（C-20-50-03）。マスタの現在値を更新しつつ履歴を1件残す。
    /// 次回期限の指定が無ければ校正周期から置く（周期も無ければ期限なしになる）
    /// </summary>
    [HttpPost("{id:int}/calibrations")]
    [Authorize(Roles = MesRoleGroups.QualityManage)]
    public async Task<ActionResult<InspectionDeviceCalibrationResponse>> AddCalibration(
        int id, InspectionDeviceCalibrationRequest request, CancellationToken ct)
    {
        // 判定・保存は実績CSV取込と共通（InspectionDeviceService）
        var outcome = await devices.AddCalibrationAsync(id, request, User.FindFirstValue(ClaimTypes.NameIdentifier), ct);
        if (outcome.Failed)
        {
            return NotFound();
        }
        var calibration = outcome.Value!;

        return new InspectionDeviceCalibrationResponse(
            calibration.Id, id, calibration.CalibratedOn, calibration.NextDueOn,
            calibration.Result, null, calibration.CreatedAt);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public Task<IActionResult> Deactivate(int id, CancellationToken ct) =>
        this.DeactivateMasterAsync<InspectionDevice>(db, auditLogger, id, device => $"code={device.Code}", ct);

    private static void Apply(InspectionDevice device, InspectionDeviceRequest request)
    {
        device.Code = request.Code;
        device.Name = request.Name;
        device.SerialNo = request.SerialNo;
        device.Location = request.Location;
        device.CalibratedOn = request.CalibratedOn;
        device.CalibrationDueOn = request.CalibrationDueOn;
        device.CalibrationCycleDays = request.CalibrationCycleDays;
        device.Note = request.Note;
    }

    private InspectionDeviceResponse ToResponse(InspectionDevice d) =>
        new(d.Id, d.Code, d.Name, d.SerialNo, d.Location,
            d.CalibratedOn, d.CalibrationDueOn, d.CalibrationCycleDays, d.Note, d.IsActive,
            InspectionDeviceCalibrationPolicy.IsExpired(d.CalibrationDueOn, businessDate.Today),
            InspectionDeviceCalibrationPolicy.DaysUntilDue(d.CalibrationDueOn, businessDate.Today));
}
