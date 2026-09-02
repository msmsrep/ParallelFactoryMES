using System.Security.Claims;
using MesApp.Core.Contracts.Common;
using MesApp.Core.Contracts.Maintenance;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 設備稼働報告・稼働監視（B-40-20 稼働時間・停止時間・停止原因の記録、E-20-10 稼働情報の収集・
/// モニタリング）。初期は手入力（センサー自動収集は将来拡張）。現場作業者も記録できる。
/// <para>
/// 設備の稼働・停止の記録はロールで絞らない（Spec.md 7.4 の意図的な例外）。
/// 気づいた人がその場で上げられることを優先する。指示・承認は別途ロールで絞る。
/// </para>
/// </summary>
[ApiController]
[Route("api/equipment-logs")]
[Authorize]
public class EquipmentLogsController(MesAppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<EquipmentLogResponse>>> List(
        [FromQuery] PageQuery paging,
        [FromQuery] int? equipmentId = null,
        [FromQuery] EquipmentLogStatus? status = null,
        CancellationToken ct = default)
    {
        var query = db.EquipmentLogs.AsNoTracking().AsQueryable();
        if (equipmentId is not null)
        {
            query = query.Where(l => l.EquipmentId == equipmentId);
        }
        if (status is not null)
        {
            query = query.Where(l => l.Status == status);
        }
        return await query.OrderByDescending(l => l.Id)
            .Select(l => new EquipmentLogResponse(
                l.Id, l.EquipmentId, l.Equipment!.Name, l.Status,
                l.StartedAt, l.EndedAt, l.StopCause, l.Note))
            .ToPagedResultAsync(paging, ct);
    }

    [HttpPost]
    public async Task<ActionResult<EquipmentLogResponse>> Create(EquipmentLogRequest request, CancellationToken ct)
    {
        var equipment = await db.Equipments.FirstOrDefaultAsync(e => e.Id == request.EquipmentId, ct);
        if (equipment is null || !equipment.IsActive)
        {
            return BadRequest(new ProblemDetails { Title = "存在しない（または無効な）設備IDです。" });
        }
        if (request.EndedAt is not null && request.EndedAt <= request.StartedAt)
        {
            return BadRequest(new ProblemDetails { Title = "終了時刻は開始時刻より後である必要があります。" });
        }
        if (request.Status is EquipmentLogStatus.Stopped or EquipmentLogStatus.Failure
            && string.IsNullOrWhiteSpace(request.StopCause))
        {
            return BadRequest(new ProblemDetails { Title = "停止・故障の記録には停止原因（stopCause）が必要です（B-40-20-02）。" });
        }

        var log = new EquipmentLog
        {
            EquipmentId = request.EquipmentId,
            Status = request.Status,
            StartedAt = request.StartedAt,
            EndedAt = request.EndedAt,
            StopCause = request.StopCause,
            Note = request.Note,
            RecordedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
        };
        db.EquipmentLogs.Add(log);
        await db.SaveChangesAsync(ct);
        return new EquipmentLogResponse(log.Id, log.EquipmentId, equipment.Name, log.Status,
            log.StartedAt, log.EndedAt, log.StopCause, log.Note);
    }

    /// <summary>
    /// 設備別の稼働サマリ（E-20-10-03、E-20-30-03）。終了時刻が記録済みのログを集計し、
    /// 時間稼働率＝稼働時間÷記録済み総時間を返す。
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<List<EquipmentUtilizationRow>>> Summary(CancellationToken ct)
    {
        // SQLiteはDateTimeOffsetの演算を翻訳できないため、集計はクライアント側で行う
        var logs = await db.EquipmentLogs.AsNoTracking()
            .Where(l => l.EndedAt != null)
            .Select(l => new
            {
                l.EquipmentId,
                l.Equipment!.AssetNo,
                EquipmentName = l.Equipment!.Name,
                l.Status,
                l.StartedAt,
                l.EndedAt,
            })
            .ToListAsync(ct);

        var rows = logs
            .GroupBy(l => new { l.EquipmentId, l.AssetNo, l.EquipmentName })
            .OrderBy(g => g.Key.AssetNo)
            .Select(g =>
            {
                decimal Hours(EquipmentLogStatus status) => Math.Round(g
                    .Where(l => l.Status == status)
                    .Sum(l => (decimal)(l.EndedAt!.Value - l.StartedAt).TotalHours), 2);
                var running = Hours(EquipmentLogStatus.Running);
                var stopped = Hours(EquipmentLogStatus.Stopped);
                var setup = Hours(EquipmentLogStatus.Setup);
                var failure = Hours(EquipmentLogStatus.Failure);
                var total = running + stopped + setup + failure;
                return new EquipmentUtilizationRow(
                    g.Key.EquipmentId, g.Key.AssetNo, g.Key.EquipmentName,
                    running, stopped, setup, failure,
                    g.Count(l => l.Status == EquipmentLogStatus.Failure),
                    total == 0 ? 0 : Math.Round(running / total * 100, 2));
            })
            .ToList();
        return rows;
    }
}
