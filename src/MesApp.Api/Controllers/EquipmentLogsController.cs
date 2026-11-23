using MesApp.Core.Abstractions;
using System.Security.Claims;
using MesApp.Core.Contracts.Common;
using MesApp.Core.Contracts.Maintenance;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using MesApp.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MesApp.Api.Controllers;

/// <summary>
/// 設備稼働報告・稼働監視（B-40-20 稼働時間・停止時間・停止原因の記録、E-20-10 稼働情報の収集・
/// モニタリング）。手入力・実績CSVに加え、設備イベント（連携API。EquipmentEventsController）で収集した区間も並ぶ。現場作業者も記録できる。
/// <para>
/// 設備の稼働・停止の記録はロールで絞らない（Spec.md 7.4 の意図的な例外）。
/// 気づいた人がその場で上げられることを優先する。指示・承認は別途ロールで絞る。
/// </para>
/// </summary>
[ApiController]
[Route("api/equipment-logs")]
[Authorize]
public class EquipmentLogsController(
    MesAppDbContext db, ShopFloorReportService reports, IBusinessDateService businessDate,
    IOptions<EquipmentEventOptions> eventOptions) : ControllerBase
{
    /// <summary>
    /// 設備ごとの今の状態（E-20-10-03）。有効な設備ごとに、開いている稼働区間（状態・開始・経過・作業指示）と、
    /// イベントで収集する設備では最後に受けた時刻と途絶の判定を返す。workCenterId で設置場所の作業区に絞れる。
    /// <para>
    /// 自動更新はしない（画面の「更新」で読み直す）。途絶の判定は受信の時刻で行う（設備の時計ではなく MES の時計で数えるため）
    /// </para>
    /// </summary>
    [HttpGet("current")]
    public async Task<ActionResult<List<EquipmentCurrentStatusRow>>> Current(
        [FromQuery] int? workCenterId = null, CancellationToken ct = default)
    {
        var equipments = await db.Equipments.AsNoTracking()
            .Where(e => e.IsActive && (workCenterId == null || e.WorkCenterId == workCenterId))
            .OrderBy(e => e.AssetNo)
            .Select(e => new
            {
                e.Id, e.AssetNo, e.Name, e.WorkCenterId,
                WorkCenterCode = e.WorkCenter != null ? e.WorkCenter.Code : null,
                WorkCenterName = e.WorkCenter != null ? e.WorkCenter.Name : null,
                e.CollectsEvents,
            })
            .ToListAsync(ct);
        var ids = equipments.Select(e => e.Id).ToList();

        // 設備ごとの最新の開いた区間・最新の受信。SQLite は DateTimeOffset の最大を SQL にできないので、ID の最大で引く
        var openLogIds = await db.EquipmentLogs.AsNoTracking()
            .Where(l => l.EndedAt == null && ids.Contains(l.EquipmentId))
            .GroupBy(l => l.EquipmentId).Select(g => g.Max(l => l.Id))
            .ToListAsync(ct);
        var openLogs = await db.EquipmentLogs.AsNoTracking()
            .Where(l => openLogIds.Contains(l.Id))
            .Select(l => new
            {
                l.EquipmentId, l.Status, l.StartedAt, l.StopCause, l.WorkOrderId,
                WorkOrderNo = l.WorkOrder != null ? l.WorkOrder.WorkOrderNo : null,
            })
            .ToDictionaryAsync(l => l.EquipmentId, ct);
        var lastEventIds = await db.EquipmentEvents.AsNoTracking()
            .Where(e => ids.Contains(e.EquipmentId))
            .GroupBy(e => e.EquipmentId).Select(g => g.Max(e => e.Id))
            .ToListAsync(ct);
        var lastReceived = await db.EquipmentEvents.AsNoTracking()
            .Where(e => lastEventIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.EquipmentId, e => e.ReceivedAt, ct);

        var now = DateTimeOffset.UtcNow;
        var silence = TimeSpan.FromMinutes(Math.Max(1, eventOptions.Value.SilenceMinutes));
        return equipments.Select(e =>
        {
            openLogs.TryGetValue(e.Id, out var log);
            DateTimeOffset? received = lastReceived.TryGetValue(e.Id, out var at) ? at : null;
            return new EquipmentCurrentStatusRow(
                e.Id, e.AssetNo, e.Name, e.WorkCenterId, e.WorkCenterCode, e.WorkCenterName, e.CollectsEvents,
                log?.Status, log?.StartedAt,
                log is null ? null : Math.Round((decimal)(now - log.StartedAt).TotalMinutes, 1),
                log?.StopCause, log?.WorkOrderId, log?.WorkOrderNo,
                e.CollectsEvents ? received : null,
                e.CollectsEvents && (received is null || now - received.Value > silence));
        }).ToList();
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<EquipmentLogResponse>>> List(
        [FromQuery] PageQuery paging,
        [FromQuery] int? equipmentId = null,
        [FromQuery] int? workOrderId = null,
        [FromQuery] EquipmentLogStatus? status = null,
        [FromQuery] bool missingCause = false,
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
        // 原因未入力：停止・故障なのに原因が無い区間（設備イベントで原因を引けなかったもの。B-40-20-02）
        if (missingCause)
        {
            query = query.Where(l => (l.Status == EquipmentLogStatus.Stopped || l.Status == EquipmentLogStatus.Failure)
                && (l.StopCause == null || l.StopCause == ""));
        }
        return await query.OrderByDescending(l => l.Id)
            .Select(l => new EquipmentLogResponse(
                l.Id, l.EquipmentId, l.Equipment!.Name, l.Status,
                l.StartedAt, l.EndedAt, l.StopCause, l.Note,
                l.WorkOrderId, l.WorkOrder != null ? l.WorkOrder.WorkOrderNo : null, l.Source))
            .ToPagedResultAsync(paging, ct);
    }

    [HttpPost]
    public async Task<ActionResult<EquipmentLogResponse>> Create(EquipmentLogRequest request, CancellationToken ct)
    {
        // 登録の判定と監査ログは実績CSV取込と共通（ShopFloorReportService）
        var outcome = await reports.AddEquipmentLogAsync(
            request, User.FindFirstValue(ClaimTypes.NameIdentifier), ct);
        if (outcome.Value is not { } log)
        {
            return this.BadRequestProblem(outcome.Error);
        }
        return await db.EquipmentLogs.AsNoTracking()
            .Where(l => l.Id == log.Id)
            .Select(l => new EquipmentLogResponse(
                l.Id, l.EquipmentId, l.Equipment!.Name, l.Status,
                l.StartedAt, l.EndedAt, l.StopCause, l.Note,
                l.WorkOrderId, l.WorkOrder != null ? l.WorkOrder.WorkOrderNo : null, l.Source))
            .SingleAsync(ct);
    }

    /// <summary>停止原因の追記（B-40-20-02）。ロールで絞らない（稼働記録の登録と同じ）</summary>
    [HttpPut("{id:int}/stop-cause")]
    public async Task<ActionResult<EquipmentLogResponse>> UpdateStopCause(
        int id, EquipmentLogStopCauseRequest request, CancellationToken ct)
    {
        var outcome = await reports.UpdateStopCauseAsync(id, request.StopCause, ct);
        if (outcome.Value is not { } log)
        {
            return outcome.Kind == OutcomeError.NotFound
                ? this.NotFoundProblem(outcome.Error)
                : this.BadRequestProblem(outcome.Error);
        }
        return await db.EquipmentLogs.AsNoTracking()
            .Where(l => l.Id == log.Id)
            .Select(l => new EquipmentLogResponse(
                l.Id, l.EquipmentId, l.Equipment!.Name, l.Status,
                l.StartedAt, l.EndedAt, l.StopCause, l.Note,
                l.WorkOrderId, l.WorkOrder != null ? l.WorkOrder.WorkOrderNo : null, l.Source))
            .SingleAsync(ct);
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
