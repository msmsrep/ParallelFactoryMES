using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Quality;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// 不適合・逸脱の処理（Spec.md 3.3：B-40-30 逸脱・品質不具合の記録、C-30 不適合対応・特別採用）。
/// 記録・対応指示・対応実績・承認の状態遷移と、不適合の連鎖（Spec.md 5.7：
/// ロットの在庫ステータス変更・リワーク指図の自動起票）をここにまとめる。
/// <para>保存と監査ログまで行う。状態は Controller で代入せず本サービス経由で変更する。</para>
/// </summary>
public sealed class NonconformanceService(
    MesAppDbContext db,
    NumberingService numbering,
    LotStatusService lotStatus,
    IAuditLogger auditLogger)
{
    /// <summary>逸脱・品質不具合の記録（B-40-30-01〜02）</summary>
    public async Task<Outcome<NonconformanceReport>> CreateAsync(
        NonconformanceCreateRequest request, string? userId, CancellationToken ct)
    {
        if (request.LotId is int lotId && !await db.Lots.AnyAsync(l => l.Id == lotId, ct))
        {
            return Outcome<NonconformanceReport>.Invalid("存在しないロットIDです。");
        }
        if (request.WorkOrderId is int workOrderId
            && !await db.WorkOrders.AnyAsync(w => w.Id == workOrderId, ct))
        {
            return Outcome<NonconformanceReport>.Invalid("存在しない作業指示IDです。");
        }
        if (request.InspectionOrderId is int inspectionOrderId
            && !await db.InspectionOrders.AnyAsync(i => i.Id == inspectionOrderId, ct))
        {
            return Outcome<NonconformanceReport>.Invalid("存在しない検査指示IDです。");
        }

        var report = new NonconformanceReport
        {
            ReportNo = await numbering.NextNonconformanceNoAsync(ct),
            Source = request.Source,
            LotId = request.LotId,
            WorkOrderId = request.WorkOrderId,
            InspectionOrderId = request.InspectionOrderId,
            Content = request.Content,
            CauseCategory = request.CauseCategory,
            CauseDetail = request.CauseDetail,
            ReportedByUserId = userId,
        };
        db.NonconformanceReports.Add(report);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Quality", "NonconformanceCreate", nameof(NonconformanceReport),
            report.Id.ToString(), detail: $"reportNo={report.ReportNo}, source={report.Source}", ct: ct);
        return Outcome<NonconformanceReport>.Ok(report);
    }

    /// <summary>
    /// 対応指示（C-30-20-01）。保留→ロット保留、廃棄→ロット廃棄予定、
    /// リワーク→リワーク指図の自動起票（対象ロットの由来指図が特定できる場合）、特採→承認待ち。
    /// </summary>
    public async Task<Outcome<NonconformanceReport>> InstructAsync(
        int id, NonconformanceActionRequest request, string? userId, CancellationToken ct)
    {
        var report = await db.NonconformanceReports
            .Include(n => n.Lot).ThenInclude(l => l!.SourceWorkOrder)
            .FirstOrDefaultAsync(n => n.Id == id, ct);
        if (report is null)
        {
            return Outcome<NonconformanceReport>.NotFound("存在しない不適合IDです。");
        }
        if (report.Status is NonconformanceStatus.Closed)
        {
            return Outcome<NonconformanceReport>.Conflict("クローズ済みの不適合には対応指示できません。");
        }

        report.Action = request.Action;
        report.ActionInstruction = request.Instruction;
        report.ActionInstructedByUserId = userId;
        report.ActionInstructedAt = DateTimeOffset.UtcNow;
        report.Status = NonconformanceStatus.ActionInstructed;

        // 不適合の連鎖（Spec.md 5.7）
        switch (request.Action)
        {
            case NonconformanceAction.Hold when report.Lot is not null:
                lotStatus.ChangeStatus(report.Lot, LotStockStatus.OnHold,
                    LotStatusChangeSource.Nonconformance,
                    $"不適合 {report.ReportNo} の保留指示（{request.Instruction}）",
                    userId, nonconformanceReportId: report.Id);
                break;
            case NonconformanceAction.Discard when report.Lot is not null:
                lotStatus.ChangeStatus(report.Lot, LotStockStatus.ToBeDiscarded,
                    LotStatusChangeSource.Nonconformance,
                    $"不適合 {report.ReportNo} の廃棄指示（{request.Instruction}）",
                    userId, nonconformanceReportId: report.Id);
                break;
            case NonconformanceAction.Rework when report.Lot is not null:
                await AddReworkOrderAsync(report, userId, ct);
                break;
        }

        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Quality", "NonconformanceInstruct", nameof(NonconformanceReport),
            id.ToString(), detail: $"reportNo={report.ReportNo}, action={request.Action}", ct: ct);
        return Outcome<NonconformanceReport>.Ok(report);
    }

    /// <summary>対応実行記録（C-30-20-02）</summary>
    public async Task<Outcome<NonconformanceReport>> RecordActionAsync(
        int id, NonconformanceActionRecordRequest request, string? userId, CancellationToken ct)
    {
        var report = await db.NonconformanceReports.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (report is null)
        {
            return Outcome<NonconformanceReport>.NotFound("存在しない不適合IDです。");
        }
        if (report.Status != NonconformanceStatus.ActionInstructed)
        {
            return Outcome<NonconformanceReport>.Conflict("対応指示済みの不適合のみ対応実績を記録できます。");
        }

        report.ActionRecord = request.Record;
        report.ActionCompletedByUserId = userId;
        report.ActionCompletedAt = DateTimeOffset.UtcNow;
        report.Status = NonconformanceStatus.ActionCompleted;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Quality", "NonconformanceAction", nameof(NonconformanceReport),
            id.ToString(), detail: $"reportNo={report.ReportNo}", ct: ct);
        return Outcome<NonconformanceReport>.Ok(report);
    }

    /// <summary>
    /// 逸脱承認・特別採用の承認（C-30-20-03）。特採の場合は対象ロットを正常へ戻し次工程進行を許可する。
    /// </summary>
    public async Task<Outcome<NonconformanceReport>> ApproveAsync(
        int id, string? userId, CancellationToken ct)
    {
        var report = await db.NonconformanceReports.Include(n => n.Lot)
            .FirstOrDefaultAsync(n => n.Id == id, ct);
        if (report is null)
        {
            return Outcome<NonconformanceReport>.NotFound("存在しない不適合IDです。");
        }
        if (report.Status is NonconformanceStatus.Open)
        {
            return Outcome<NonconformanceReport>.Conflict("対応指示前の不適合は承認できません。");
        }
        if (report.Status is NonconformanceStatus.Closed)
        {
            return Outcome<NonconformanceReport>.Conflict("既にクローズ済みです。");
        }

        report.ApprovedByUserId = userId;
        report.ApprovedAt = DateTimeOffset.UtcNow;
        report.Status = NonconformanceStatus.Closed;

        // 特採：承認記録付きで次工程進行（Spec.md 5.7）
        if (report.Action == NonconformanceAction.SpecialAcceptance && report.Lot is not null)
        {
            lotStatus.ChangeStatus(report.Lot, LotStockStatus.Normal,
                LotStatusChangeSource.Nonconformance,
                $"不適合 {report.ReportNo} の特採承認による解放",
                userId, nonconformanceReportId: report.Id);
        }
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Quality", "NonconformanceApprove", nameof(NonconformanceReport),
            id.ToString(), detail: $"reportNo={report.ReportNo}, action={report.Action}", ct: ct);
        return Outcome<NonconformanceReport>.Ok(report);
    }

    /// <summary>リワーク指図の自動起票（B-70-10-01。由来指図が特定できる場合のみ）</summary>
    private async Task AddReworkOrderAsync(NonconformanceReport report, string? userId, CancellationToken ct)
    {
        var sourceOrderId = report.Lot!.SourceWorkOrder?.ManufacturingOrderId;
        if (sourceOrderId is null)
        {
            return;
        }
        var rework = new ManufacturingOrder
        {
            OrderNo = await numbering.NextOrderNoAsync(ct),
            ProductId = report.Lot.ProductId,
            Quantity = report.Lot.InitialQuantity,
            OrderType = ManufacturingOrderType.Rework,
            SourceOrderId = sourceOrderId,
            Note = $"不適合 {report.ReportNo} のリワーク",
            CreatedByUserId = userId,
        };
        db.ManufacturingOrders.Add(rework);
        report.ReworkOrder = rework;
    }
}
