using MesApp.Api.Localization;
using MesApp.Api.Services.Webhooks;
using MesApp.Core.Abstractions;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Integration;
using MesApp.Core.Localization;
using MesApp.Core.Contracts.Execution;
using MesApp.Core.Contracts.Maintenance;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// 現場からの記録のうち作業指示の実行に属さないもの：作業時間（B-30-30-02、F-30-20-01）、
/// 製造トラブル報告（B-40-10-06、B-60-10）、設備の稼働・停止の記録（B-40-20、E-20-10-01）、
/// 治工具の利用実績（E-60-20-01）。
/// 単票API（<c>WorkTimeRecordsController</c>・<c>TroubleReportsController</c>・<c>EquipmentLogsController</c>・
/// <c>ToolUsagesController</c>）と
/// 実績CSV取込の両方から呼ぶ。
/// 保存と監査ログまで行う。トランザクションは呼び出し側が張る。
/// </summary>
public sealed class ShopFloorReportService(MesAppDbContext db, WebhookPublisher webhooks, IAuditLogger auditLogger)
{
    /// <summary>
    /// Webhook <c>equipment.stopped</c>（Spec.md 3.8 Webhook）を、停止・故障の区間なら積む。
    /// 手入力・実績CSV（このサービス）と設備イベント（<see cref="EquipmentEventService"/>）で同じものを使う。
    /// 区間は保存前でも、設備・作業指示は ID で持っているので番号を引ける
    /// </summary>
    public static async Task EnqueueStoppedAsync(
        WebhookPublisher webhooks, MesAppDbContext db, EquipmentLog log, string? alarmCode, CancellationToken ct)
    {
        if (log.Status is not (EquipmentLogStatus.Stopped or EquipmentLogStatus.Failure))
        {
            return;
        }
        await webhooks.EnqueueAsync(WebhookEventTypes.EquipmentStopped, async c =>
        {
            var assetNo = await db.Equipments.AsNoTracking()
                .Where(x => x.Id == log.EquipmentId).Select(x => x.AssetNo).FirstAsync(c);
            var workOrderNo = log.WorkOrderId is { } workOrderId
                ? await db.WorkOrders.AsNoTracking().Where(w => w.Id == workOrderId).Select(w => w.WorkOrderNo).FirstOrDefaultAsync(c)
                : null;
            // 送る文は保存される値なので訳さない（区分は原文の表示名）
            var cause = string.IsNullOrWhiteSpace(log.StopCause) ? "原因未入力" : log.StopCause;
            return ($"設備 {assetNo} が「{EnumLabels.OriginalOf(log.Status)}」になりました（{cause}）。",
                new EquipmentStoppedData(assetNo, log.Status, log.StartedAt, log.EndedAt,
                    string.IsNullOrWhiteSpace(log.StopCause) ? null : log.StopCause, alarmCode, log.Source, workOrderNo));
        }, ct);
    }

    /// <summary>作業時間の記録（直接作業は作業指示が必須）</summary>
    public async Task<Outcome<WorkTimeRecord>> AddWorkTimeAsync(
        WorkTimeRequest request, string userId, CancellationToken ct)
    {
        if (request.Type == WorkTimeType.Direct && request.WorkOrderId is null)
        {
            return Outcome<WorkTimeRecord>.Invalid(ApiText.T("直接作業には作業指示ID（workOrderId）が必要です。"));
        }
        if (request.WorkOrderId is int workOrderId
            && !await db.WorkOrders.AnyAsync(w => w.Id == workOrderId, ct))
        {
            return Outcome<WorkTimeRecord>.Invalid(ApiText.T("存在しない作業指示IDです。"));
        }

        var record = new WorkTimeRecord
        {
            UserId = userId,
            Type = request.Type,
            IndirectCategory = request.IndirectCategory,
            WorkOrderId = request.WorkOrderId,
            StartedAt = request.StartedAt,
            EndedAt = request.EndedAt,
            Note = request.Note,
        };
        db.WorkTimeRecords.Add(record);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Execution", "WorkTime", nameof(WorkTimeRecord), record.Id.ToString(),
            detail: new { type = record.Type, workOrderId = record.WorkOrderId }, ct: ct);
        return Outcome<WorkTimeRecord>.Ok(record);
    }

    /// <summary>製造トラブル報告（B-60-10-01。報告は誰でも上げられる）</summary>
    public async Task<Outcome<TroubleReport>> AddTroubleReportAsync(
        TroubleReportRequest request, string userId, CancellationToken ct)
    {
        if (request.WorkOrderId is int workOrderId
            && !await db.WorkOrders.AnyAsync(w => w.Id == workOrderId, ct))
        {
            return Outcome<TroubleReport>.Invalid(ApiText.T("存在しない作業指示IDです。"));
        }
        if (request.EquipmentId is int equipmentId
            && !await db.Equipments.AnyAsync(e => e.Id == equipmentId, ct))
        {
            return Outcome<TroubleReport>.Invalid(ApiText.T("存在しない設備IDです。"));
        }

        var report = new TroubleReport
        {
            OccurredAt = request.OccurredAt,
            Category = request.Category,
            WorkOrderId = request.WorkOrderId,
            EquipmentId = request.EquipmentId,
            Content = request.Content,
            ReportedByUserId = userId,
        };
        db.TroubleReports.Add(report);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Execution", "TroubleReport", nameof(TroubleReport), report.Id.ToString(),
            detail: $"category={request.Category}", ct: ct);
        return Outcome<TroubleReport>.Ok(report);
    }

    /// <summary>
    /// 設備の稼働・停止の記録（B-40-20、E-20-10-01）。記録はロールで絞らない（気づいた人がその場で上げる）。
    /// 作業指示への紐付けは任意（段取り・保全のように紐づかない記録もある）
    /// </summary>
    public async Task<Outcome<EquipmentLog>> AddEquipmentLogAsync(
        EquipmentLogRequest request, string? userId, CancellationToken ct,
        EquipmentLogSource source = EquipmentLogSource.Manual)
    {
        var equipment = await db.Equipments.AsNoTracking().FirstOrDefaultAsync(e => e.Id == request.EquipmentId, ct);
        if (equipment is null || !equipment.IsActive)
        {
            return Outcome<EquipmentLog>.Invalid(ApiText.T("存在しない（または無効な）設備IDです。"));
        }
        // イベントで収集する設備に手で区間を足すと、同じ時間を二重に数える（Spec.md 5.5 EquipmentEvent）
        if (equipment.CollectsEvents)
        {
            return Outcome<EquipmentLog>.Invalid(ApiText.T(
                "設備 '{0}' は設備イベントで稼働状態を収集しているため、稼働記録を手で登録できません。", equipment.AssetNo));
        }
        if (request.EndedAt is not null && request.EndedAt <= request.StartedAt)
        {
            return Outcome<EquipmentLog>.Invalid(ApiText.T("終了時刻は開始時刻より後である必要があります。"));
        }
        if (request.Status is EquipmentLogStatus.Stopped or EquipmentLogStatus.Failure
            && string.IsNullOrWhiteSpace(request.StopCause))
        {
            return Outcome<EquipmentLog>.Invalid(ApiText.T("停止・故障の記録には停止原因（stopCause）が必要です（B-40-20-02）。"));
        }
        // 作業指示に紐づけると、その指示で作ったロットの品質と設備の状態を突き合わせられる（PQC×EQC。Spec.md 5.7）
        if (request.WorkOrderId is { } workOrderId
            && !await db.WorkOrders.AnyAsync(w => w.Id == workOrderId, ct))
        {
            return Outcome<EquipmentLog>.Invalid(ApiText.T("作業指示（ID {0}）が見つかりません。", workOrderId));
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
            RecordedByUserId = userId,
            Source = source,
        };
        db.EquipmentLogs.Add(log);
        await EnqueueStoppedAsync(webhooks, db, log, alarmCode: null, ct);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Equipment", "Log", nameof(EquipmentLog), log.Id.ToString(),
            detail: new
            {
                equipmentId = log.EquipmentId,
                workOrderId = log.WorkOrderId,
                status = log.Status,
                stopCause = log.StopCause,
            }, ct: ct);
        return Outcome<EquipmentLog>.Ok(log);
    }

    /// <summary>
    /// 停止原因の追記（B-40-20-02）。停止・故障の区間の原因だけを書き換え、変更前後を監査ログに残す。
    /// 設備イベントで収集する設備にも使える（区間の手入力は拒否するが、原因は人が入れるしかないため）
    /// </summary>
    public async Task<Outcome<EquipmentLog>> UpdateStopCauseAsync(int logId, string stopCause, CancellationToken ct)
    {
        var log = await db.EquipmentLogs.FindAsync([logId], ct);
        if (log is null)
        {
            return Outcome<EquipmentLog>.NotFound(ApiText.T("稼働記録（ID {0}）が見つかりません。", logId));
        }
        if (log.Status is not (EquipmentLogStatus.Stopped or EquipmentLogStatus.Failure))
        {
            return Outcome<EquipmentLog>.Invalid(ApiText.T("停止原因を入れられるのは停止・故障の記録だけです。"));
        }
        var cause = stopCause.Trim();
        if (cause.Length == 0)
        {
            return Outcome<EquipmentLog>.Invalid(ApiText.T("停止原因を入力してください。"));
        }
        var before = log.StopCause;
        log.StopCause = cause;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Equipment", "UpdateStopCause", nameof(EquipmentLog), log.Id.ToString(),
            detail: new { before, after = cause, reason = (string?)null }, ct: ct);
        return Outcome<EquipmentLog>.Ok(log);
    }

    /// <summary>
    /// 治工具の利用実績（E-60-20-01。寿命の累計に使う）。記録はロールで絞らない（現場作業者も記録できる）。
    /// recordedAt は省略すると登録時刻（CSV取込で過去の利用実績を入れるときだけ指定する）
    /// </summary>
    public async Task<Outcome<ToolUsage>> AddToolUsageAsync(
        ToolUsageRequest request, DateTimeOffset? recordedAt, string? userId, CancellationToken ct)
    {
        var tool = await db.Tools.AsNoTracking().FirstOrDefaultAsync(t => t.Id == request.ToolId, ct);
        if (tool is null || !tool.IsActive)
        {
            return Outcome<ToolUsage>.Invalid(ApiText.T("存在しない（または無効な）治工具IDです。"));
        }
        if (request.WorkOrderId is int workOrderId
            && !await db.WorkOrders.AnyAsync(w => w.Id == workOrderId, ct))
        {
            return Outcome<ToolUsage>.Invalid(ApiText.T("存在しない作業指示IDです。"));
        }
        if (request.UsageCount <= 0 && (request.UsageHours is null or <= 0))
        {
            return Outcome<ToolUsage>.Invalid(ApiText.T("使用回数または使用時間のどちらかを記録してください。"));
        }

        var usage = new ToolUsage
        {
            ToolId = request.ToolId,
            WorkOrderId = request.WorkOrderId,
            UsageCount = request.UsageCount,
            UsageHours = request.UsageHours,
            RecordedByUserId = userId,
        };
        if (recordedAt is { } at)
        {
            usage.RecordedAt = at;
        }
        db.ToolUsages.Add(usage);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Equipment", "ToolUsage", nameof(ToolUsage), usage.Id.ToString(),
            detail: new { toolId = usage.ToolId, count = usage.UsageCount, hours = usage.UsageHours }, ct: ct);
        return Outcome<ToolUsage>.Ok(usage);
    }
}
