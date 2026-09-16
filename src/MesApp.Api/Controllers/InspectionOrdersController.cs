using System.Security.Claims;
using MesApp.Api.Policies;
using MesApp.Api.Services;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Common;
using MesApp.Core.Contracts.Quality;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 検査指示・実績・判定・承認（Spec.md 3.3：C-20 受入/工程内/完成品/サンプル/再検査）。
/// 検査対象ロットは指示作成で「検査待ち」になり、総合判定で正常/不良へ反映される（Spec.md 5.7）。
/// 不合格判定時は不適合レポートを自動起票する。
/// </summary>
[ApiController]
[Route("api/inspection-orders")]
[Authorize]
public class InspectionOrdersController(
    MesAppDbContext db,
    InspectionService inspections,
    LotStatusService lotStatus,
    IAuditLogger auditLogger) : ControllerBase
{
    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>検査進捗・ステータス一覧（C-20-10-01 ほか）</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<InspectionOrderResponse>>> List(
        [FromQuery] PageQuery paging,
        [FromQuery] InspectionOrderStatus? status = null,
        [FromQuery] InspectionOrderType? type = null,
        [FromQuery] int? targetLotId = null,
        CancellationToken ct = default)
    {
        var query = BaseQuery();
        if (status is not null)
        {
            query = query.Where(o => o.Status == status);
        }
        if (type is not null)
        {
            query = query.Where(o => o.Type == type);
        }
        if (targetLotId is not null)
        {
            query = query.Where(o => o.TargetLotId == targetLotId);
        }
        var orders = await query.OrderByDescending(o => o.Id).ToPagedResultAsync(paging, ct);
        return orders.Map(ToResponse);
    }

    /// <summary>検査指示の詳細（検査成績書 C-20-10-05 のデータソース。帳票出力はPhase 7）</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<InspectionOrderResponse>> Get(int id, CancellationToken ct)
    {
        var order = await BaseQuery().FirstOrDefaultAsync(o => o.Id == id, ct);
        return order is null ? NotFound() : await ToResponseWithCorrectionsAsync(order, ct);
    }

    /// <summary>
    /// 検査指示（依頼）の発行（C-20-10-02 ほか）。検査項目未指定時は種別・対象品目/工程に
    /// 合致する有効な検査基準を自動選択する。対象ロットは検査待ちになる（サンプル検査を除く）。
    /// </summary>
    [HttpPost]
    [Authorize(Roles = MesRoleGroups.QualityManage)]
    public async Task<ActionResult<InspectionOrderResponse>> Create(
        InspectionOrderCreateRequest request, CancellationToken ct)
    {
        // 履歴に検査指示IDを残すため保存が2回に分かれる。途中で失敗すると
        // 「検査指示はあるのに対象ロットが検査待ちにならない」状態が残るのでトランザクションでまとめる
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var outcome = await inspections.CreateAsync(request, CurrentUserId, ct);
        if (outcome.Value is not { } order)
        {
            return ToProblem(outcome);
        }
        await transaction.CommitAsync(ct);
        var saved = await BaseQuery().FirstAsync(o => o.Id == order.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, ToResponse(saved));
    }

    /// <summary>
    /// 検査実績の登録（C-20-10-03 ほか）。測定値があり規格値が定義されていれば自動判定する。
    /// </summary>
    [HttpPost("{id:int}/results")]
    [Authorize(Roles = MesRoleGroups.QualityManage)]
    public async Task<ActionResult<InspectionOrderResponse>> AddResults(
        int id, List<InspectionResultRequest> requests, CancellationToken ct)
    {
        var outcome = await inspections.AddResultsAsync(id, requests, CurrentUserId!, ct);
        if (outcome.Failed)
        {
            return ToProblem(outcome);
        }
        var saved = await BaseQuery().FirstAsync(o => o.Id == id, ct);
        return ToResponse(saved);
    }

    /// <summary>
    /// 検査実績の訂正（C-20-50-07。理由必須。判定済みの指示は実施中へ戻し再判定を要求する）
    /// </summary>
    [HttpPut("{id:int}/results/{resultId:int}")]
    [Authorize(Roles = MesRoleGroups.QualityManage)]
    public async Task<ActionResult<InspectionOrderResponse>> CorrectResult(
        int id, int resultId, InspectionResultCorrectionRequest request, CancellationToken ct)
    {
        var order = await db.InspectionOrders.Include(o => o.TargetLot)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return NotFound();
        }
        if (order.Status == InspectionOrderStatus.Approved)
        {
            return Conflict(new ProblemDetails { Title = "承認済みの検査は訂正できません。" });
        }
        var result = await db.InspectionResults
            .FirstOrDefaultAsync(r => r.Id == resultId && r.InspectionOrderId == id, ct);
        if (result is null)
        {
            return NotFound();
        }

        var before = new { value = result.MeasuredValue, text = result.TextValue, judgment = result.Judgment };

        // 訂正前の記録を業務履歴として残す（C-20-50-07）。検査成績書に
        // 「元の記録＋訂正理由・訂正者」を出せるようにするための正式な記録
        db.InspectionResultCorrections.Add(new InspectionResultCorrection
        {
            InspectionResultId = result.Id,
            InspectionOrderId = id,
            BeforeMeasuredValue = result.MeasuredValue,
            BeforeTextValue = result.TextValue,
            BeforeJudgment = result.Judgment,
            AfterMeasuredValue = request.MeasuredValue,
            AfterTextValue = request.TextValue,
            AfterJudgment = request.Judgment,
            Reason = request.Reason,
            CorrectedByUserId = CurrentUserId,
        });

        result.MeasuredValue = request.MeasuredValue;
        result.TextValue = request.TextValue;
        result.Judgment = request.Judgment;
        result.CorrectionNote = string.IsNullOrEmpty(result.CorrectionNote)
            ? request.Reason
            : $"{result.CorrectionNote}\n{request.Reason}";

        // 判定済みだった場合は再判定を要求し、ロットを検査待ちへ戻す
        if (order.Status == InspectionOrderStatus.Judged)
        {
            order.Status = InspectionOrderStatus.InProgress;
            order.OverallJudgment = null;
            order.JudgedAt = null;
            order.JudgedByUserId = null;
            if (order.TargetLot is not null && order.Type != InspectionOrderType.Sample)
            {
                lotStatus.ChangeStatus(order.TargetLot, LotStockStatus.AwaitingInspection,
                    LotStatusChangeSource.Inspection, $"検査実績の訂正による再判定待ち（{request.Reason}）",
                    CurrentUserId, inspectionOrderId: order.Id);
            }
        }
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Quality", "InspectionCorrect", nameof(InspectionResult), resultId.ToString(),
            detail: new
            {
                orderNo = order.OrderNo,
                before,
                after = new
                {
                    value = request.MeasuredValue,
                    text = request.TextValue,
                    judgment = request.Judgment,
                },
                reason = request.Reason,
            }, ct: ct);
        var saved = await BaseQuery().FirstAsync(o => o.Id == id, ct);
        return await ToResponseWithCorrectionsAsync(saved, ct);
    }

    /// <summary>
    /// 総合判定（C-20-10-04）。全検査項目に実績があり全て合格なら合格。判定結果は
    /// 対象ロットの在庫ステータス（検査待ち→正常/不良）へ反映し、不合格時は不適合を自動起票する。
    /// </summary>
    [HttpPost("{id:int}/judge")]
    [Authorize(Roles = MesRoleGroups.QualityManage)]
    public async Task<ActionResult<InspectionOrderResponse>> Judge(
        int id, InspectionJudgeRequest request, CancellationToken ct)
    {
        var outcome = await inspections.JudgeAsync(id, request.Grade, CurrentUserId, ct);
        if (outcome.Failed)
        {
            return ToProblem(outcome);
        }
        var saved = await BaseQuery().FirstAsync(o => o.Id == id, ct);
        return ToResponse(saved);
    }

    /// <summary>検査承認（C-20-10-06）</summary>
    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = MesRoleGroups.QualityManage)]
    public async Task<ActionResult<InspectionOrderResponse>> Approve(int id, CancellationToken ct)
    {
        var order = await db.InspectionOrders.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return NotFound();
        }
        if (order.Status != InspectionOrderStatus.Judged)
        {
            return Conflict(new ProblemDetails { Title = $"状態 '{order.Status}' の検査指示は承認できません（判定済みのみ）。" });
        }
        order.Status = InspectionOrderStatus.Approved;
        order.ApprovedByUserId = CurrentUserId;
        order.ApprovedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Quality", "InspectionApprove", nameof(InspectionOrder), id.ToString(),
            detail: $"orderNo={order.OrderNo}", ct: ct);
        var saved = await BaseQuery().FirstAsync(o => o.Id == id, ct);
        return ToResponse(saved);
    }

    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = MesRoleGroups.QualityManage)]
    public async Task<ActionResult<InspectionOrderResponse>> Cancel(int id, CancellationToken ct)
    {
        var order = await db.InspectionOrders.Include(o => o.TargetLot)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return NotFound();
        }
        if (order.Status is InspectionOrderStatus.Approved or InspectionOrderStatus.Canceled)
        {
            return Conflict(new ProblemDetails { Title = $"状態 '{order.Status}' の検査指示は取消できません。" });
        }
        order.Status = InspectionOrderStatus.Canceled;
        // 検査待ちで拘束していたロットを解放する
        if (order.TargetLot is { StockStatus: LotStockStatus.AwaitingInspection })
        {
            lotStatus.ChangeStatus(order.TargetLot, LotStockStatus.Normal, LotStatusChangeSource.Inspection,
                $"検査指示 {order.OrderNo} の取消による拘束解除", CurrentUserId, inspectionOrderId: order.Id);
        }
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Quality", "InspectionCancel", nameof(InspectionOrder), id.ToString(), ct: ct);
        var saved = await BaseQuery().FirstAsync(o => o.Id == id, ct);
        return ToResponse(saved);
    }

    private ActionResult ToProblem<T>(Outcome<T> outcome) => outcome.Kind switch
    {
        OutcomeError.NotFound => NotFound(),
        OutcomeError.Conflict => Conflict(new ProblemDetails { Title = outcome.Error }),
        _ => BadRequest(new ProblemDetails { Title = outcome.Error }),
    };

    private IQueryable<InspectionOrder> BaseQuery() =>
        db.InspectionOrders.AsNoTracking()
            .Include(o => o.TargetLot)
            .Include(o => o.TargetWorkOrder)
            .Include(o => o.Items).ThenInclude(i => i.InspectionItem)
            .Include(o => o.Results).ThenInclude(r => r.InspectionItem)
            .Include(o => o.Results).ThenInclude(r => r.InspectedBy)
            .Include(o => o.Results).ThenInclude(r => r.InspectionDevice);

    /// <summary>
    /// 訂正履歴付きの応答を組み立てる。訂正履歴は指示に属する業務履歴として返し、
    /// 画面・検査成績書から「元の記録＋訂正理由・訂正者」を参照できるようにする
    /// </summary>
    private async Task<InspectionOrderResponse> ToResponseWithCorrectionsAsync(
        InspectionOrder o, CancellationToken ct)
    {
        var corrections = await db.InspectionResultCorrections.AsNoTracking()
            .Where(c => c.InspectionOrderId == o.Id)
            .OrderBy(c => c.Id)
            .Select(c => new
            {
                c.InspectionResultId,
                c.BeforeMeasuredValue,
                c.BeforeTextValue,
                c.BeforeJudgment,
                c.AfterMeasuredValue,
                c.AfterTextValue,
                c.AfterJudgment,
                c.Reason,
                CorrectedByName = c.CorrectedBy!.DisplayName,
                c.CorrectedAt,
                c.InspectionResult!.InspectionItemId,
                c.InspectionResult!.SampleNo,
            })
            .ToListAsync(ct);

        var response = ToResponse(o);
        return response with
        {
            Corrections = corrections.Select(c =>
            {
                var item = SnapshotOf(o, c.InspectionItemId);
                return new InspectionResultCorrectionResponse(
                    c.InspectionResultId, item?.ItemCode ?? string.Empty, item?.ItemName ?? string.Empty,
                    c.SampleNo,
                    c.BeforeMeasuredValue, c.BeforeTextValue, c.BeforeJudgment,
                    c.AfterMeasuredValue, c.AfterTextValue, c.AfterJudgment,
                    c.Reason, c.CorrectedByName, c.CorrectedAt);
            }).ToList(),
        };
    }

    private static InspectionOrderResponse ToResponse(InspectionOrder o) =>
        new(o.Id, o.OrderNo, o.Type, o.Status,
            o.TargetLotId, o.TargetLot?.LotNumber, o.TargetWorkOrderId, o.TargetWorkOrder?.WorkOrderNo,
            o.OverallJudgment, o.JudgedAt, o.ApprovedByUserId, o.ApprovedAt, o.Note, o.CreatedAt,
            o.Items.Select(i => new InspectionOrderItemResponse(
                i.InspectionItemId, i.ItemCode, i.ItemName, i.ItemVersion,
                i.LowerLimit, i.UpperLimit, i.StandardValue,
                i.Method, i.SamplingCount)).ToList(),
            o.Results.OrderBy(r => r.Id).Select(r => new InspectionResultResponse(
                r.Id, r.InspectionItemId,
                // 項目名もマスタ現在値ではなくスナップショットから出す（改称しても記録は当時のまま）
                SnapshotOf(o, r.InspectionItemId)?.ItemCode ?? r.InspectionItem!.Code,
                SnapshotOf(o, r.InspectionItemId)?.ItemName ?? r.InspectionItem!.Name,
                r.SampleNo, r.MeasuredValue, r.TextValue, r.Judgment,
                r.InspectedByUserId, r.InspectedBy?.DisplayName, r.InspectedAt, r.CorrectionNote,
                r.InspectionDeviceId, r.InspectionDevice?.Code, r.InspectionDevice?.Name)).ToList(),
            // 訂正履歴は ToResponseWithCorrectionsAsync で詰める（一覧では取得しない）
            []);

    private static InspectionOrderItem? SnapshotOf(InspectionOrder order, int inspectionItemId) =>
        order.Items.FirstOrDefault(i => i.InspectionItemId == inspectionItemId);
}
