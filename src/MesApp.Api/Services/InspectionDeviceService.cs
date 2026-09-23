using MesApp.Api.Localization;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Entities;
using MesApp.Infrastructure;

namespace MesApp.Api.Services;

/// <summary>
/// 検査機・測定器の校正の記録（C-20-50-03）。
/// 単票API（<c>InspectionDevicesController</c>）と実績CSV取込の両方から呼ぶ。
/// 保存と監査ログまで行う。トランザクションは呼び出し側が張る。
/// </summary>
public sealed class InspectionDeviceService(MesAppDbContext db, IAuditLogger auditLogger)
{
    /// <summary>
    /// 校正の実施を記録する。マスタの現在値（最終校正日・次回期限）を更新しつつ履歴を1件残す。
    /// 次回期限の指定が無ければ校正周期から置く（周期も無ければ期限なしになる）
    /// </summary>
    public async Task<Outcome<InspectionDeviceCalibration>> AddCalibrationAsync(
        int deviceId, InspectionDeviceCalibrationRequest request, string? userId, CancellationToken ct)
    {
        var device = await db.InspectionDevices.FindAsync([deviceId], ct);
        if (device is null)
        {
            return Outcome<InspectionDeviceCalibration>.NotFound(ApiText.T("検査機が見つかりません。"));
        }

        var nextDue = request.NextDueOn
                      ?? (device.CalibrationCycleDays is { } cycle
                          ? request.CalibratedOn.AddDays(cycle)
                          : null);
        var calibration = new InspectionDeviceCalibration
        {
            InspectionDeviceId = deviceId,
            CalibratedOn = request.CalibratedOn,
            NextDueOn = nextDue,
            Result = request.Result,
            PerformedByUserId = userId,
        };
        db.InspectionDeviceCalibrations.Add(calibration);

        var before = new { device.CalibratedOn, device.CalibrationDueOn };
        device.CalibratedOn = request.CalibratedOn;
        device.CalibrationDueOn = nextDue;
        await db.SaveChangesAsync(ct);

        await auditLogger.LogAsync("Master", "Calibrate", nameof(InspectionDevice), deviceId.ToString(),
            detail: new
            {
                before,
                after = new { device.CalibratedOn, device.CalibrationDueOn },
                reason = request.Result,
            }, ct: ct);
        return Outcome<InspectionDeviceCalibration>.Ok(calibration);
    }
}
