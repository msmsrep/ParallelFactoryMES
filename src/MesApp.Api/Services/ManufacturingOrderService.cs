using MesApp.Core.Localization;
using MesApp.Api.Localization;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Production;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>製造指図の業務処理の結果（Errorがあれば未反映。IsConflictは状態・番号の衝突）</summary>
public sealed record OrderOutcome(ManufacturingOrder? Order, string? Error, bool IsConflict = false)
{
    public static OrderOutcome Ok(ManufacturingOrder order) => new(order, null);
    public static OrderOutcome Invalid(string error) => new(null, error);
    public static OrderOutcome Conflict(string error) => new(null, error, IsConflict: true);
}

/// <summary>
/// 製造指図の登録・承認・変更・取消・工程展開・完了判定（A-20-10-01、A-20-20-01/02、B-10-10-01/05、B-40-10-10）。
/// 指図の状態はここでだけ変更する。
/// 単票API（<c>ManufacturingOrdersController</c>）と実績CSV取込（<c>ActualCsvService</c>）の両方から呼ぶ。
/// 保存と監査ログまで行う。トランザクションは呼び出し側が張る（<see cref="ReceivingService"/> と同じ）。
/// </summary>
public sealed class ManufacturingOrderService(
    MesAppDbContext db,
    NumberingService numbering,
    IBusinessDateService businessDate,
    WorkOrderStatusService workOrderStatus,
    IAuditLogger auditLogger)
{
    /// <summary>自動採番の指図番号の接頭辞（手入力の番号と衝突させないため、手入力では使わせない）</summary>
    public const string AutoOrderNoPrefix = "MO";

    /// <summary>
    /// 指図登録（A-20-10-01 手動登録、B-10-10-04 突発、B-70-10-01 リワーク）。
    /// orderNo を渡すとその番号で登録する（CSV取込で後続の実績から指図を指すため。空なら自動採番）
    /// </summary>
    public async Task<OrderOutcome> CreateAsync(
        CreateManufacturingOrderRequest request, string? orderNo, string? userId, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId, ct);
        if (product is null || !product.IsActive)
        {
            return OrderOutcome.Invalid(ApiText.T("存在しない（または無効な）品目IDです。"));
        }

        ManufacturingOrder? source = null;
        if (request.OrderType == ManufacturingOrderType.Rework)
        {
            if (request.SourceOrderId is null)
            {
                return OrderOutcome.Invalid(ApiText.T("リワーク指図には元指図ID（sourceOrderId）が必要です。"));
            }
            source = await db.ManufacturingOrders.FindAsync([request.SourceOrderId.Value], ct);
            if (source is null)
            {
                return OrderOutcome.Invalid(ApiText.T("元指図が存在しません。"));
            }
        }
        else if (request.SourceOrderId is not null)
        {
            return OrderOutcome.Invalid(ApiText.T("元指図IDはリワーク指図でのみ指定できます。"));
        }

        if (string.IsNullOrWhiteSpace(orderNo))
        {
            orderNo = await numbering.NextOrderNoAsync(ct);
        }
        else if (orderNo.StartsWith(AutoOrderNoPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // 自動採番の連番は採番テーブルで管理しており、同じ形式の手入力番号があると後で衝突する
            return OrderOutcome.Invalid(
                ApiText.T("指図番号 '{0}' は自動採番の形式（{1}〜）と重なるため指定できません。", orderNo, AutoOrderNoPrefix));
        }
        else if (await db.ManufacturingOrders.AnyAsync(o => o.OrderNo == orderNo, ct))
        {
            return OrderOutcome.Conflict(ApiText.T("指図番号 '{0}' は既に存在します。", orderNo));
        }

        var order = new ManufacturingOrder
        {
            OrderNo = orderNo,
            ProductId = product.Id,
            Quantity = request.Quantity,
            DueDate = request.DueDate,
            OrderType = request.OrderType,
            SourceOrderId = source?.Id,
            Note = request.Note,
            CreatedByUserId = userId,
        };
        db.ManufacturingOrders.Add(order);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Production", "Create", nameof(ManufacturingOrder), order.Id.ToString(),
            detail: $"orderNo={order.OrderNo}, type={order.OrderType}", ct: ct);
        order.Product = product;
        return OrderOutcome.Ok(order);
    }

    /// <summary>指図承認（A-20-20-01。単段階承認：Spec.md 3.9）</summary>
    public async Task<OrderOutcome> ApproveAsync(ManufacturingOrder order, string? userId, CancellationToken ct)
    {
        if (order.Status != ManufacturingOrderStatus.Draft)
        {
            return OrderOutcome.Conflict(ApiText.T("状態 '{0}' の指図は承認できません。", EnumLabels.Of(order.Status)));
        }

        order.Status = ManufacturingOrderStatus.Approved;
        order.ApprovedByUserId = userId;
        order.ApprovedAt = DateTimeOffset.UtcNow;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Production", "Approve", nameof(ManufacturingOrder), order.Id.ToString(),
            detail: $"orderNo={order.OrderNo}", ct: ct);
        return OrderOutcome.Ok(order);
    }

    /// <summary>
    /// 指図変更（A-20-20-02）。未承認・承認済みの指図のみ変更でき、承認済みを変更したときは
    /// 承認を取り消して未承認へ戻す（変更後の内容で承認し直させるため）
    /// </summary>
    public async Task<OrderOutcome> UpdateAsync(
        ManufacturingOrder order, UpdateManufacturingOrderRequest request, CancellationToken ct)
    {
        if (order.Status is not (ManufacturingOrderStatus.Draft or ManufacturingOrderStatus.Approved))
        {
            return OrderOutcome.Conflict(
                ApiText.T("状態 '{0}' の指図は変更できません（展開済み以降は取消のみ可能です）。", EnumLabels.Of(order.Status)));
        }

        var reapproval = order.Status == ManufacturingOrderStatus.Approved;
        order.Quantity = request.Quantity;
        order.DueDate = request.DueDate;
        order.Note = request.Note;
        if (reapproval)
        {
            order.Status = ManufacturingOrderStatus.Draft;
            order.ApprovedByUserId = null;
            order.ApprovedAt = null;
        }
        order.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Production", "Update", nameof(ManufacturingOrder), order.Id.ToString(),
            detail: $"orderNo={order.OrderNo}, reapprovalRequired={reapproval}", ct: ct);
        return OrderOutcome.Ok(order);
    }

    /// <summary>
    /// 指図取消（A-20-20-02）。完了・取消済み以外の指図を取り消し、未完了の作業指示も連動して取り消す。
    /// order は WorkOrders を読み込んだ追跡中のエンティティを渡す。
    /// </summary>
    public async Task<OrderOutcome> CancelAsync(ManufacturingOrder order, string? userId, CancellationToken ct)
    {
        if (order.Status is ManufacturingOrderStatus.Completed or ManufacturingOrderStatus.Canceled)
        {
            return OrderOutcome.Conflict(ApiText.T("状態 '{0}' の指図は取消できません。", EnumLabels.Of(order.Status)));
        }

        order.Status = ManufacturingOrderStatus.Canceled;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        foreach (var workOrder in order.WorkOrders.Where(w =>
                     w.Status is not (WorkOrderStatus.Completed or WorkOrderStatus.Approved)))
        {
            workOrderStatus.ChangeStatus(workOrder, WorkOrderStatus.Canceled,
                WorkOrderStatusChangeSource.OrderCancel, userId,
                $"指図 {order.OrderNo} の取消に連動");
        }
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Production", "Cancel", nameof(ManufacturingOrder), order.Id.ToString(),
            detail: $"orderNo={order.OrderNo}", ct: ct);
        return OrderOutcome.Ok(order);
    }

    /// <summary>
    /// 作業指示の承認（B-40-10-10）に続く指図の完了判定。承認した作業指示以外がすべて承認済みか取消なら
    /// 指図を完了にし、完了にしたかどうかを返す。
    /// 作業指示の承認と同じ保存で反映するため、SaveChanges は呼び出し側が行う。
    /// </summary>
    public async Task<bool> CompleteIfAllWorkOrdersDoneAsync(
        ManufacturingOrder order, int approvedWorkOrderId, DateTimeOffset now, CancellationToken ct)
    {
        var allDone = !await db.WorkOrders.AnyAsync(w =>
            w.ManufacturingOrderId == order.Id
            && w.Id != approvedWorkOrderId
            && w.Status != WorkOrderStatus.Approved && w.Status != WorkOrderStatus.Canceled, ct);
        if (allDone)
        {
            order.Status = ManufacturingOrderStatus.Completed;
            order.UpdatedAt = now;
        }
        return allDone;
    }

    /// <summary>
    /// 工程展開（B-10-10-01：工順に基づき工程単位の作業指示に展開）＋産出ロット採番（B-10-10-05：
    /// 自動＝品目コード-日付-連番、または手入力）。承認済みの指図のみ展開できる。
    /// order は Product を読み込んだ追跡中のエンティティを渡す。
    /// </summary>
    public async Task<OrderOutcome> ExpandAsync(ManufacturingOrder order, string? lotNumber, CancellationToken ct)
    {
        if (order.Status != ManufacturingOrderStatus.Approved)
        {
            return OrderOutcome.Conflict(ApiText.T("状態 '{0}' の指図は展開できません（承認済みの指図のみ）。", EnumLabels.Of(order.Status)));
        }

        var routing = await db.Routings
            .Include(r => r.WorkProcedure)
            .Where(r => r.ProductId == order.ProductId)
            .OrderBy(r => r.Sequence)
            .ToListAsync(ct);
        if (routing.Count == 0)
        {
            return OrderOutcome.Invalid(ApiText.T("品目 '{0}' に工順（BOP）が登録されていません。", order.Product!.Code));
        }

        // 産出ロット採番（手入力があれば一意性を確認して使用）
        if (string.IsNullOrWhiteSpace(lotNumber))
        {
            lotNumber = await numbering.NextLotNumberAsync(order.Product!.Code, ct);
        }
        else if (await db.Lots.AnyAsync(l => l.LotNumber == lotNumber, ct))
        {
            return OrderOutcome.Conflict(ApiText.T("ロット番号 '{0}' は既に存在します。", lotNumber));
        }

        var lot = new Lot
        {
            LotNumber = lotNumber,
            ProductId = order.ProductId,
            InitialQuantity = 0, // 実績計上（Phase 3）で確定
            OriginType = LotOriginType.Production,
            ManufacturedOn = businessDate.Today,
            StockStatus = LotStockStatus.Normal,
        };
        db.Lots.Add(lot);

        // 工順（BOP）は展開時点の値を作業指示へ写して固定する（Spec.md 5.7）。
        // 以降に工順が改訂されても、この指図の標準時間・必要スキル・管理項目は変わらない
        // 工程管理項目も同じ理由で展開時点の値を写す（B-30-30-04）。検査基準のスナップショットと同じ方針で、
        // 対象品目/工程に合致する有効な項目を自動選択する（工順に明示的な紐付けを持たせない）
        var controlItems = await db.ControlItems.AsNoTracking()
            .Where(i => i.IsActive && i.TargetProductId == order.ProductId)
            .ToListAsync(ct);
        var processControlItems = await db.ControlItems.AsNoTracking()
            .Where(i => i.IsActive && i.TargetProcessId != null)
            .ToListAsync(ct);

        foreach (var step in routing)
        {
            var workOrder = new WorkOrder
            {
                WorkOrderNo = $"{order.OrderNo}-{step.Sequence:00}",
                ManufacturingOrder = order,
                ProductId = order.ProductId,
                ProcessId = step.ProcessId,
                RoutingSequence = step.Sequence,
                PlannedQuantity = order.Quantity,
                StandardWorkMinutes = step.StandardWorkMinutes,
                StandardSetupMinutes = step.StandardSetupMinutes,
                RequiredSkillId = step.RequiredSkillId,
                WorkCenterId = step.WorkCenterId,
                ControlItems = step.ControlItems,
                RoutingChecklistId = step.ChecklistId,
                WorkProcedureId = step.WorkProcedureId,
                // 手順の本文は写さない。改訂した手順は仕掛中の指示にも届くべきなので
                // 表示はマスタの現在値を使い、ここには「計画時の版数」だけを残す
                WorkProcedureVersion = step.WorkProcedure?.Version,
            };
            // 品目単位の項目と、この工程を対象にした項目を合わせる（同じ項目は1回だけ）
            workOrder.ControlItemSnapshots =
            [
                .. controlItems
                    .Concat(processControlItems.Where(i => i.TargetProcessId == step.ProcessId))
                    .DistinctBy(i => i.Id)
                    .OrderBy(i => i.Code)
                    .Select(i => new WorkOrderControlItem
                    {
                        ControlItemId = i.Id,
                        ItemCode = i.Code,
                        ItemName = i.Name,
                        Unit = i.Unit,
                        ItemVersion = i.Version,
                        TargetValue = i.TargetValue,
                        LowerLimit = i.LowerLimit,
                        UpperLimit = i.UpperLimit,
                    }),
            ];
            db.WorkOrders.Add(workOrder);
        }

        // MBOMも展開時点で予定材料として固定する。以降の投入照合（B-30-20-01）と
        // バックフラッシュ（B-40-10-09）はこの予定材料を基準にする
        var bom = await db.BomItems
            .Where(b => b.ParentProductId == order.ProductId)
            .ToListAsync(ct);
        foreach (var item in bom)
        {
            db.ManufacturingOrderMaterials.Add(new ManufacturingOrderMaterial
            {
                ManufacturingOrder = order,
                ChildProductId = item.ChildProductId,
                QuantityPer = item.QuantityPer,
                PlannedQuantity = item.QuantityPer * order.Quantity,
                AlternativeGroup = item.AlternativeGroup,
                IsAlternative = item.IsAlternative,
            });
        }

        order.OutputLot = lot;
        order.Status = ManufacturingOrderStatus.Released;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Production", "Expand", nameof(ManufacturingOrder), order.Id.ToString(),
            detail: new
            {
                orderNo = order.OrderNo,
                lot = lotNumber,
                workOrders = routing.Count,
                materials = bom.Count,
            }, ct: ct);
        return OrderOutcome.Ok(order);
    }
}
