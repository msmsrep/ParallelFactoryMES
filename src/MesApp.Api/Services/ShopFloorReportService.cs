using MesApp.Api.Localization;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Execution;
using MesApp.Core.Contracts.Maintenance;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// 現場からの記録のうち作業指示の実行に属さないもの：作業時間（B-30-30-02、F-30-20-01）、
/// 製造トラブル報告（B-40-10-06、B-60-10）、設備の稼働・停止の記録（B-40-20、E-20-10-01）。
/// 単票API（<c>WorkTimeRecordsController</c>・<c>TroubleReportsController</c>・<c>EquipmentLogsController</c>）と
/// 実績CSV取込の両方から呼ぶ。
/// 保存と監査ログまで行う。トランザクションは呼び出し側が張る。
/// </summary>
public sealed class ShopFloorReportService(MesAppDbContext db, IAuditLogger auditLogger)
{
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
        EquipmentLogRequest request, string? userId, CancellationToken ct)
    {
        var equipment = await db.Equipments.AsNoTracking().FirstOrDefaultAsync(e => e.Id == request.EquipmentId, ct);
        if (equipment is null || !equipment.IsActive)
        {
            return Outcome<EquipmentLog>.Invalid(ApiText.T("存在しない（または無効な）設備IDです。"));
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
        return Outcome<EquipmentLog>.Ok(log);
    }
}
