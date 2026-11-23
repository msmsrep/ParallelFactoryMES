using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Policies;

/// <summary>
/// 停止原因の対応表の判定（Spec.md 5.1 EquipmentAlarmCode。B-40-20-02）。
/// 重複の判定は単票APIとCSV取込、原因の引き当ては設備イベントの受信から呼ぶ
/// </summary>
public static class EquipmentAlarmCodePolicy
{
    /// <summary>
    /// 同じ（設備, アラーム番号）の行が既にあるか。設備が空（共通）の行どうしは一意索引では止まらない
    /// （SQLite・PostgreSQL は NULL を互いに別の値として扱う）ので、ここで判定する
    /// </summary>
    public static Task<bool> ExistsAsync(
        MesAppDbContext db, int? equipmentId, string alarmCode, int? excludeId, CancellationToken ct) =>
        db.EquipmentAlarmCodes.AnyAsync(a =>
            a.EquipmentId == equipmentId && a.AlarmCode == alarmCode && a.Id != excludeId, ct);

    /// <summary>
    /// アラーム番号から停止原因を引く。<b>設備ごとの定義が共通の定義に勝つ</b>。無効な定義は使わない。
    /// 引けなければ null（区間は原因未入力のまま開き、画面から後で入れる）
    /// </summary>
    public static async Task<string?> ResolveStopCauseAsync(
        MesAppDbContext db, int equipmentId, string? alarmCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(alarmCode))
        {
            return null;
        }
        var code = alarmCode.Trim();
        var candidates = await db.EquipmentAlarmCodes.AsNoTracking()
            .Where(a => a.IsActive && a.AlarmCode == code && (a.EquipmentId == equipmentId || a.EquipmentId == null))
            .Select(a => new { a.EquipmentId, a.StopCause })
            .ToListAsync(ct);
        return (candidates.FirstOrDefault(a => a.EquipmentId == equipmentId) ?? candidates.FirstOrDefault())?.StopCause;
    }
}
