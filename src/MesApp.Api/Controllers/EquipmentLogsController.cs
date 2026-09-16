using MesApp.Core.Abstractions;
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
public class EquipmentLogsController(
    MesAppDbContext db, IAuditLogger auditLogger, IBusinessDateService businessDate) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<EquipmentLogResponse>>> List(
        [FromQuery] PageQuery paging,
        [FromQuery] int? equipmentId = null,
        [FromQuery] int? workOrderId = null,
        [FromQuery] EquipmentLogStatus? status = null,
        CancellationToken ct = default)
    {
        var query = db.EquipmentLogs.AsNoTracking().AsQueryable();
        if (equipmentId is not null)
        {
            query = query.Where(l => l.EquipmentId == equipmentId);
        }
        if (workOrderId is not null)
        {
            query = query.Where(l => l.WorkOrderId == workOrderId);
        }
        if (status is not null)
        {
            query = query.Where(l => l.Status == status);
        }
        return await query.OrderByDescending(l => l.Id)
            .Select(l => new EquipmentLogResponse(
                l.Id, l.EquipmentId, l.Equipment!.Name, l.Status,
                l.StartedAt, l.EndedAt, l.StopCause, l.Note,
                l.WorkOrderId, l.WorkOrder != null ? l.WorkOrder.WorkOrderNo : null))
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

        // 作業指示に紐づけると、その指示で作ったロットの品質と設備の状態を突き合わせられる
        // （PQC×EQCの交差点。Spec.md 5.7）。段取り・保全のように紐づかない記録もあるため任意
        WorkOrder? workOrder = null;
        if (request.WorkOrderId is { } workOrderId)
        {
            workOrder = await db.WorkOrders.AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == workOrderId, ct);
            if (workOrder is null)
            {
                return BadRequest(new ProblemDetails { Title = $"作業指示（ID {workOrderId}）が見つかりません。" });
            }
        }

        var log = new EquipmentLog
        {
            EquipmentId = request.EquipmentId,
            WorkOrderId = request.WorkOrderId,
            Status = request.Status,
            StartedAt = request.StartedAt,
            EndedAt = request.EndedAt,
            StopCause = request.StopCause,
            Note = request.Note,
            RecordedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
        };
        db.EquipmentLogs.Add(log);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Equipment", "Log", nameof(EquipmentLog), log.Id.ToString(),
            detail: new
            {
                equipmentId = log.EquipmentId,
                workOrderId = log.WorkOrderId,
                status = log.Status,
                stopCause = log.StopCause,
            }, ct: ct);
        return new EquipmentLogResponse(log.Id, log.EquipmentId, equipment.Name, log.Status,
            log.StartedAt, log.EndedAt, log.StopCause, log.Note,
            log.WorkOrderId, workOrder?.WorkOrderNo);
    }

    /// <summary>
    /// 設備別の稼働サマリ（E-20-10-03、E-20-30-03）。終了時刻が記録済みのログを集計し、
    /// 時間稼働率＝稼働時間÷記録済み総時間を返す。
    /// <para>
    /// 期間は製造日（業務日付）基準で、稼働の<b>開始時刻</b>が属する製造日で振り分ける
    /// （Spec.md 3.9。夜勤の日跨ぎ稼働を1つの製造日に寄せるため、終了時刻では分けない）。
    /// 省略時は全期間を集計する。
    /// </para>
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<List<EquipmentUtilizationRow>>> Summary(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken ct = default)
    {
        var fromStart = from is null ? (DateTimeOffset?)null : businessDate.GetRange(from.Value).Start;
        var toEnd = to is null ? (DateTimeOffset?)null : businessDate.GetRange(to.Value).End;

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
            .Where(l => (fromStart is null || l.StartedAt >= fromStart)
                        && (toEnd is null || l.StartedAt < toEnd))
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
                var idle = Hours(EquipmentLogStatus.Idle);
                var total = running + stopped + setup + failure + idle;
                return new EquipmentUtilizationRow(
                    g.Key.EquipmentId, g.Key.AssetNo, g.Key.EquipmentName,
                    running, stopped, setup, failure, idle,
                    g.Count(l => l.Status == EquipmentLogStatus.Failure),
                    total == 0 ? 0 : Math.Round(running / total * 100, 2));
            })
            .ToList();
        return rows;
    }
}
