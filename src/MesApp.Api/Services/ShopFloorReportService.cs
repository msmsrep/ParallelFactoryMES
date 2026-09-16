using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Execution;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// 現場からの記録のうち作業指示の実行に属さないもの：作業時間（B-30-30-02、F-30-20-01）と
/// 製造トラブル報告（B-40-10-06、B-60-10）。
/// 単票API（<c>WorkTimeRecordsController</c>・<c>TroubleReportsController</c>）と実績CSV取込の両方から呼ぶ。
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
            return Outcome<WorkTimeRecord>.Invalid("直接作業には作業指示ID（workOrderId）が必要です。");
        }
        if (request.WorkOrderId is int workOrderId
            && !await db.WorkOrders.AnyAsync(w => w.Id == workOrderId, ct))
        {
            return Outcome<WorkTimeRecord>.Invalid("存在しない作業指示IDです。");
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
            return Outcome<TroubleReport>.Invalid("存在しない作業指示IDです。");
        }
        if (request.EquipmentId is int equipmentId
            && !await db.Equipments.AnyAsync(e => e.Id == equipmentId, ct))
        {
            return Outcome<TroubleReport>.Invalid("存在しない設備IDです。");
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
}
