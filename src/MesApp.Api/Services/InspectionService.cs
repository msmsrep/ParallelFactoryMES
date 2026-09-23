using MesApp.Core.Localization;
using MesApp.Api.Localization;
using MesApp.Api.Policies;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Quality;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// 検査指示の発行・検査実績の登録と訂正・総合判定・承認・取消（Spec.md 3.3：C-20）。
/// 検査指示の状態はここでだけ変更する。
/// 単票API（<c>InspectionOrdersController</c>）と実績CSV取込（<c>ActualCsvService</c>）の両方から呼ぶ。
/// 保存と監査ログまで行う。トランザクションは呼び出し側が張る（<see cref="ReceivingService"/> と同じ）。
/// </summary>
public sealed class InspectionService(
    MesAppDbContext db,
    NumberingService numbering,
    LotStatusService lotStatus,
    IBusinessDateService businessDate,
    IAuditLogger auditLogger)
{
    /// <summary>
    /// 検査指示（依頼）の発行（C-20-10-02 ほか）。検査項目未指定時は種別・対象品目/工程に
    /// 合致する有効な検査基準を自動選択する（<see cref="InspectionItemPolicy.Applicable"/>）。
    /// 検査項目を指定した場合も同じ条件に合わない基準は拒否する。対象ロットは検査待ちになる（サンプル検査を除く）。
    /// </summary>
    public async Task<Outcome<InspectionOrder>> CreateAsync(
        InspectionOrderCreateRequest request, string? userId, CancellationToken ct)
    {
        Lot? lot = null;
        WorkOrder? workOrder = null;

        if (request.Type == InspectionOrderType.InProcess)
        {
            if (request.TargetWorkOrderId is null)
            {
                return Outcome<InspectionOrder>.Invalid(ApiText.T("工程内検査には対象作業指示ID（targetWorkOrderId）が必要です。"));
            }
            workOrder = await db.WorkOrders.FirstOrDefaultAsync(w => w.Id == request.TargetWorkOrderId, ct);
            if (workOrder is null)
            {
                return Outcome<InspectionOrder>.Invalid(ApiText.T("存在しない作業指示IDです。"));
            }
        }
        else if (request.TargetLotId is null)
        {
            return Outcome<InspectionOrder>.Invalid(ApiText.T("対象ロットID（targetLotId）が必要です。"));
        }

        if (request.TargetLotId is not null)
        {
            lot = await db.Lots.FirstOrDefaultAsync(l => l.Id == request.TargetLotId, ct);
            if (lot is null)
            {
                return Outcome<InspectionOrder>.Invalid(ApiText.T("存在しないロットIDです。"));
            }
            // ロットを検査待ちで拘束する検査は、拘束してよい状態のときだけ発行する（サンプル検査は拘束しない）
            if (request.Type != InspectionOrderType.Sample)
            {
                var pending = await db.InspectionOrders.AsNoTracking()
                    .Where(o => o.TargetLotId == lot.Id && o.Type != InspectionOrderType.Sample
                                && (o.Status == InspectionOrderStatus.Instructed || o.Status == InspectionOrderStatus.InProgress))
                    .Select(o => o.OrderNo)
                    .FirstOrDefaultAsync(ct);
                if (InspectionLotPolicy.CheckIssuable(lot, request.Type, pending) is { } blocked)
                {
                    return Outcome<InspectionOrder>.Conflict(blocked);
                }
            }
        }

        // 検査項目セットの決定：種別（再検査は完成品基準）＋対象品目/工程に「かつ」で合う有効な基準（C-10-10）
        var applicable = InspectionItemPolicy.Applicable(
            InspectionItemPolicy.ItemTypeOf(request.Type), lot?.ProductId ?? workOrder!.ProductId, workOrder?.ProcessId);
        List<InspectionItem> items;
        if (request.ItemIds is { Count: > 0 })
        {
            var itemIds = request.ItemIds.Distinct().ToList();
            items = await db.InspectionItems.AsNoTracking()
                .Where(i => itemIds.Contains(i.Id))
                .ToListAsync(ct);
            if (items.Count != itemIds.Count)
            {
                return Outcome<InspectionOrder>.Invalid(ApiText.T("存在しない検査項目IDが含まれています。"));
            }
            // 指定した基準も自動選択と同じ条件で確かめる（無効な基準や他品目の基準で判定させない）
            var isApplicable = applicable.Compile();
            if (items.FirstOrDefault(i => !isApplicable(i)) is { } unusable)
            {
                return Outcome<InspectionOrder>.Invalid(ApiText.T(
                    "検査項目 '{0}' はこの検査に使えません（無効、検査種別が違う、または対象の品目・工程が合いません）。", unusable.Code));
            }
        }
        else
        {
            items = await db.InspectionItems.AsNoTracking().Where(applicable).ToListAsync(ct);
            if (items.Count == 0)
            {
                return Outcome<InspectionOrder>.Invalid(
                    ApiText.T("対象に合致する検査基準がありません。検査項目マスタを登録するか itemIds を指定してください。"));
            }
        }

        var order = new InspectionOrder
        {
            OrderNo = await numbering.NextInspectionNoAsync(ct),
            Type = request.Type,
            TargetLotId = lot?.Id,
            TargetWorkOrderId = workOrder?.Id,
            RequestedByUserId = userId,
            Note = request.Note,
            // 発行時点の基準を写す。以降マスタが改訂されても、この検査の判定根拠は変わらない
            Items = items.Select(i => new InspectionOrderItem
            {
                InspectionItemId = i.Id,
                ItemCode = i.Code,
                ItemName = i.Name,
                ItemVersion = i.Version,
                LowerLimit = i.LowerLimit,
                UpperLimit = i.UpperLimit,
                StandardValue = i.StandardValue,
                Method = i.Method,
                SamplingCount = i.SamplingCount,
            }).ToList(),
        };
        db.InspectionOrders.Add(order);
        await db.SaveChangesAsync(ct);

        // 対象ロットを検査待ちへ（サンプル検査はロットを拘束しない）。
        // 履歴に検査指示IDを残すため、採番済みになってから変更する
        if (lot is not null && request.Type != InspectionOrderType.Sample)
        {
            lotStatus.ChangeStatus(lot, LotStockStatus.AwaitingInspection, LotStatusChangeSource.Inspection,
                $"検査指示 {order.OrderNo} の発行", userId, inspectionOrderId: order.Id);
            await db.SaveChangesAsync(ct);
        }
        await auditLogger.LogAsync("Quality", "InspectionCreate", nameof(InspectionOrder), order.Id.ToString(),
            detail: $"orderNo={order.OrderNo}, type={order.Type}", ct: ct);
        return Outcome<InspectionOrder>.Ok(order);
    }

    /// <summary>検査実績の登録（C-20-10-03 ほか）。測定値があり規格値が定義されていれば自動判定する</summary>
    public async Task<Outcome<InspectionOrder>> AddResultsAsync(
        int orderId, List<InspectionResultRequest> requests, string userId, CancellationToken ct)
    {
        var order = await db.InspectionOrders.Include(o => o.Items).ThenInclude(i => i.InspectionItem)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null)
        {
            return Outcome<InspectionOrder>.NotFound(ApiText.T("検査指示が存在しません。"));
        }
        if (order.Status is not (InspectionOrderStatus.Instructed or InspectionOrderStatus.InProgress))
        {
            return Outcome<InspectionOrder>.Conflict(ApiText.T("状態 '{0}' の検査指示には実績を登録できません。", EnumLabels.Of(order.Status)));
        }
        if (requests.Count == 0)
        {
            return Outcome<InspectionOrder>.Invalid(ApiText.T("登録する実績がありません。"));
        }

        var itemById = order.Items.ToDictionary(i => i.InspectionItemId);

        // 校正期限を過ぎた検査機で測った結果は品質保証の根拠にならないため、記録させない
        // （判定は InspectionDeviceCalibrationPolicy。期限接近の一覧と同じ条件を使う）
        var deviceIds = requests.Where(r => r.InspectionDeviceId is not null)
            .Select(r => r.InspectionDeviceId!.Value).Distinct().ToList();
        var devices = await db.InspectionDevices.AsNoTracking()
            .Where(d => deviceIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, ct);
        foreach (var deviceId in deviceIds)
        {
            if (!devices.TryGetValue(deviceId, out var device))
            {
                return Outcome<InspectionOrder>.Invalid(ApiText.T("検査機ID {0} は登録されていません。", deviceId));
            }
            if (InspectionDeviceCalibrationPolicy.CheckUsable(device, businessDate.Today) is { } reason)
            {
                return Outcome<InspectionOrder>.Conflict(reason);
            }
        }

        foreach (var request in requests)
        {
            if (!itemById.TryGetValue(request.InspectionItemId, out var item))
            {
                return Outcome<InspectionOrder>.Invalid(ApiText.T("検査項目ID {0} はこの検査指示の対象ではありません。", request.InspectionItemId));
            }
            // 判定は指示発行時点の規格値（スナップショット）で行う
            var judgment = Judge(item, request.MeasuredValue, request.Judgment);
            if (judgment is null)
            {
                return Outcome<InspectionOrder>.Invalid(
                    ApiText.T("検査項目 '{0}' は規格値による自動判定ができません。judgmentを指定してください。", item.ItemCode));
            }
            db.InspectionResults.Add(new InspectionResult
            {
                InspectionOrderId = orderId,
                InspectionItemId = item.InspectionItemId,
                SampleNo = request.SampleNo ?? 1,
                MeasuredValue = request.MeasuredValue,
                TextValue = request.TextValue,
                Judgment = judgment.Value,
                InspectedByUserId = userId,
                InspectionDeviceId = request.InspectionDeviceId,
            });
        }
        order.Status = InspectionOrderStatus.InProgress;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Quality", "InspectionResult", nameof(InspectionOrder), orderId.ToString(),
            detail: $"results={requests.Count}", ct: ct);
        return Outcome<InspectionOrder>.Ok(order);
    }

    /// <summary>
    /// 総合判定（C-20-10-04）。全検査項目に実績があり全て合格なら合格。判定結果は
    /// 対象ロットの在庫ステータス（検査待ち→正常/不良）へ反映し、不合格時は不適合を自動起票する。
    /// </summary>
    public async Task<Outcome<InspectionOrder>> JudgeAsync(
        int orderId, string? grade, string? userId, CancellationToken ct)
    {
        var order = await db.InspectionOrders
            .Include(o => o.Items)
            .Include(o => o.Results)
            .Include(o => o.TargetLot)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null)
        {
            return Outcome<InspectionOrder>.NotFound(ApiText.T("検査指示が存在しません。"));
        }
        if (order.Status is not (InspectionOrderStatus.Instructed or InspectionOrderStatus.InProgress))
        {
            return Outcome<InspectionOrder>.Conflict(ApiText.T("状態 '{0}' の検査指示は判定できません。", EnumLabels.Of(order.Status)));
        }

        var itemsWithoutResult = order.Items
            .Where(i => order.Results.All(r => r.InspectionItemId != i.InspectionItemId))
            .ToList();
        if (itemsWithoutResult.Count > 0)
        {
            return Outcome<InspectionOrder>.Invalid(
                ApiText.T("実績未登録の検査項目が {0} 件あります。全項目の実績登録後に判定してください。", itemsWithoutResult.Count));
        }

        var pass = order.Results.All(r => r.Judgment == InspectionJudgment.Pass);
        order.OverallJudgment = pass ? InspectionJudgment.Pass : InspectionJudgment.Fail;
        order.JudgedByUserId = userId;
        order.JudgedAt = DateTimeOffset.UtcNow;
        order.Status = InspectionOrderStatus.Judged;

        // 判定結果のロット反映（Spec.md 5.7：検査待ち→正常/不良）
        if (order.TargetLot is not null && order.Type != InspectionOrderType.Sample)
        {
            lotStatus.ChangeStatus(order.TargetLot, pass ? LotStockStatus.Normal : LotStockStatus.Defective,
                LotStatusChangeSource.Inspection,
                $"検査指示 {order.OrderNo} の総合判定：{order.OverallJudgment}",
                userId, inspectionOrderId: order.Id);
            if (!string.IsNullOrWhiteSpace(grade))
            {
                order.TargetLot.Grade = grade; // グレード管理（C-60-10-01）
            }
        }

        // 不合格時は不適合レポートを自動起票（不適合の連鎖：C-30）
        if (!pass)
        {
            db.NonconformanceReports.Add(new NonconformanceReport
            {
                ReportNo = await numbering.NextNonconformanceNoAsync(ct),
                Source = NonconformanceSource.Inspection,
                LotId = order.TargetLotId,
                WorkOrderId = order.TargetWorkOrderId,
                InspectionOrderId = order.Id,
                Content = $"検査 {order.OrderNo} で不合格判定",
                ReportedByUserId = userId,
            });
        }

        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Quality", "InspectionJudge", nameof(InspectionOrder), orderId.ToString(),
            detail: $"orderNo={order.OrderNo}, judgment={order.OverallJudgment}", ct: ct);
        return Outcome<InspectionOrder>.Ok(order);
    }

    /// <summary>
    /// 検査実績の訂正（C-20-50-07。理由必須。判定済みの指示は実施中へ戻し再判定を要求する）
    /// </summary>
    public async Task<Outcome<InspectionOrder>> CorrectResultAsync(
        int orderId, int resultId, InspectionResultCorrectionRequest request, string? userId, CancellationToken ct)
    {
        var order = await db.InspectionOrders.Include(o => o.TargetLot)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null)
        {
            return Outcome<InspectionOrder>.NotFound(ApiText.T("検査指示が存在しません。"));
        }
        if (order.Status == InspectionOrderStatus.Approved)
        {
            return Outcome<InspectionOrder>.Conflict(ApiText.T("承認済みの検査は訂正できません。"));
        }
        var result = await db.InspectionResults
            .FirstOrDefaultAsync(r => r.Id == resultId && r.InspectionOrderId == orderId, ct);
        if (result is null)
        {
            return Outcome<InspectionOrder>.NotFound(ApiText.T("検査実績が存在しません。"));
        }

        var before = new { value = result.MeasuredValue, text = result.TextValue, judgment = result.Judgment };

        // 訂正前の記録を業務履歴として残す（C-20-50-07）。検査成績書に
        // 「元の記録＋訂正理由・訂正者」を出せるようにするための正式な記録
        db.InspectionResultCorrections.Add(new InspectionResultCorrection
        {
            InspectionResultId = result.Id,
            InspectionOrderId = orderId,
            BeforeMeasuredValue = result.MeasuredValue,
            BeforeTextValue = result.TextValue,
            BeforeJudgment = result.Judgment,
            AfterMeasuredValue = request.MeasuredValue,
            AfterTextValue = request.TextValue,
            AfterJudgment = request.Judgment,
            Reason = request.Reason,
            CorrectedByUserId = userId,
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
                    userId, inspectionOrderId: order.Id);
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
        return Outcome<InspectionOrder>.Ok(order);
    }

    /// <summary>検査承認（C-20-10-06）。判定済みの指示のみ承認できる</summary>
    public async Task<Outcome<InspectionOrder>> ApproveAsync(int orderId, string? userId, CancellationToken ct)
    {
        var order = await db.InspectionOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null)
        {
            return Outcome<InspectionOrder>.NotFound(ApiText.T("検査指示が存在しません。"));
        }
        if (order.Status != InspectionOrderStatus.Judged)
        {
            return Outcome<InspectionOrder>.Conflict(ApiText.T("状態 '{0}' の検査指示は承認できません（判定済みのみ）。", EnumLabels.Of(order.Status)));
        }
        order.Status = InspectionOrderStatus.Approved;
        order.ApprovedByUserId = userId;
        order.ApprovedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Quality", "InspectionApprove", nameof(InspectionOrder), orderId.ToString(),
            detail: $"orderNo={order.OrderNo}", ct: ct);
        return Outcome<InspectionOrder>.Ok(order);
    }

    /// <summary>
    /// 検査指示の取消。承認済み・取消済み以外を取り消し、検査待ちで拘束していたロットを**発行前のステータスへ戻す**。
    /// 一律に正常へ戻すと、不良ロットの再検査を取り消しただけで不良が解除されてしまう
    /// </summary>
    public async Task<Outcome<InspectionOrder>> CancelAsync(int orderId, string? userId, CancellationToken ct)
    {
        var order = await db.InspectionOrders.Include(o => o.TargetLot)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null)
        {
            return Outcome<InspectionOrder>.NotFound(ApiText.T("検査指示が存在しません。"));
        }
        if (order.Status is InspectionOrderStatus.Approved or InspectionOrderStatus.Canceled)
        {
            return Outcome<InspectionOrder>.Conflict(ApiText.T("状態 '{0}' の検査指示は取消できません。", EnumLabels.Of(order.Status)));
        }
        order.Status = InspectionOrderStatus.Canceled;
        // 検査待ちで拘束していたロットを、この指示が拘束する前のステータスへ戻す（状態履歴の最初の拘束から引く）
        if (order.TargetLot is { StockStatus: LotStockStatus.AwaitingInspection } lot)
        {
            var before = await db.LotStatusHistories.AsNoTracking()
                .Where(h => h.LotId == lot.Id && h.InspectionOrderId == order.Id
                            && h.ToStatus == LotStockStatus.AwaitingInspection)
                .OrderBy(h => h.Id)
                .Select(h => (LotStockStatus?)h.FromStatus)
                .FirstOrDefaultAsync(ct);
            // 履歴が無いのは発行前から検査待ちだった場合。そのままにする
            if (before is not null)
            {
                lotStatus.ChangeStatus(lot, before.Value, LotStatusChangeSource.Inspection,
                    $"検査指示 {order.OrderNo} の取消による拘束解除", userId, inspectionOrderId: order.Id);
            }
        }
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Quality", "InspectionCancel", nameof(InspectionOrder), orderId.ToString(), ct: ct);
        return Outcome<InspectionOrder>.Ok(order);
    }

    /// <summary>
    /// 規格値との照合による自動判定（下限≦測定値≦上限）。判定不能ならnull。
    /// 基準はマスタの現在値ではなく、指示発行時点のスナップショットを使う
    /// </summary>
    private static InspectionJudgment? Judge(
        InspectionOrderItem item, decimal? measuredValue, InspectionJudgment? explicitJudgment)
    {
        if (explicitJudgment is not null)
        {
            return explicitJudgment;
        }
        if (measuredValue is null || (item.LowerLimit is null && item.UpperLimit is null))
        {
            return null;
        }
        var pass = (item.LowerLimit is null || measuredValue >= item.LowerLimit)
                   && (item.UpperLimit is null || measuredValue <= item.UpperLimit);
        return pass ? InspectionJudgment.Pass : InspectionJudgment.Fail;
    }
}
