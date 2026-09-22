using MesApp.Api.Localization;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Quality;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// 出荷判定（H-10-10。判定（可/保留/特採）→承認の単段階承認）。
/// 単票API（<c>ShipmentJudgmentsController</c>）と実績CSV取込（<c>ActualCsvService</c>）の両方から呼ぶ。
/// 出荷実行のゲートの条件は <see cref="Policies.ShipmentGatePolicy"/> に置く。
/// <para>保存と監査ログまで行う。トランザクションは呼び出し側が張る。</para>
/// </summary>
public sealed class ShipmentJudgmentService(
    MesAppDbContext db,
    NumberingService numbering,
    IAuditLogger auditLogger)
{
    /// <summary>出荷判定（H-10-10-02。対象はロットまたは出荷指示）</summary>
    public async Task<Outcome<ShipmentJudgment>> CreateAsync(
        ShipmentJudgmentCreateRequest request, string userId, CancellationToken ct)
    {
        if (request.LotId is null && request.ShippingOrderId is null)
        {
            return Outcome<ShipmentJudgment>.Invalid(ApiText.T("対象ロットIDまたは出荷指示IDを指定してください。"));
        }
        if (request.LotId is int lotId && !await db.Lots.AnyAsync(l => l.Id == lotId, ct))
        {
            return Outcome<ShipmentJudgment>.Invalid(ApiText.T("存在しないロットIDです。"));
        }
        if (request.ShippingOrderId is int shippingOrderId
            && !await db.ShippingOrders.AnyAsync(s => s.Id == shippingOrderId, ct))
        {
            return Outcome<ShipmentJudgment>.Invalid(ApiText.T("存在しない出荷指示IDです。"));
        }

        var judgment = new ShipmentJudgment
        {
            JudgmentNo = await numbering.NextJudgmentNoAsync(ct),
            LotId = request.LotId,
            ShippingOrderId = request.ShippingOrderId,
            Result = request.Result,
            JudgedByUserId = userId,
            Note = request.Note,
        };
        db.ShipmentJudgments.Add(judgment);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Quality", "ShipmentJudge", nameof(ShipmentJudgment), judgment.Id.ToString(),
            detail: $"judgmentNo={judgment.JudgmentNo}, result={judgment.Result}", ct: ct);
        return Outcome<ShipmentJudgment>.Ok(judgment);
    }

    /// <summary>判定承認（H-10-10-03）</summary>
    public async Task<Outcome<ShipmentJudgment>> ApproveAsync(int id, string? userId, CancellationToken ct)
    {
        var judgment = await db.ShipmentJudgments.FirstOrDefaultAsync(j => j.Id == id, ct);
        if (judgment is null)
        {
            return Outcome<ShipmentJudgment>.NotFound(ApiText.T("存在しない出荷判定IDです。"));
        }
        if (judgment.ApprovedAt is not null)
        {
            return Outcome<ShipmentJudgment>.Conflict(ApiText.T("既に承認済みです。"));
        }
        judgment.ApprovedByUserId = userId;
        judgment.ApprovedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Quality", "ShipmentJudgeApprove", nameof(ShipmentJudgment), id.ToString(),
            detail: $"judgmentNo={judgment.JudgmentNo}", ct: ct);
        return Outcome<ShipmentJudgment>.Ok(judgment);
    }
}
