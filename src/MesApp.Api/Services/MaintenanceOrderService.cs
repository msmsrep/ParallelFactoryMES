using MesApp.Api.Policies;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Maintenance;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// 保全指示の作成（E-30-20-01 / E-30-30-01）・実績登録（E-40-30-01）・取消と、保全計画の取消（E-30-10）。
/// 保全指示と保全計画の状態変更をここに集める（Controller で代入しない）。
/// 計画の状態は指示の作成・実績・取消に連動するため、計画側の取消も同じ場所で判定する。
/// 保存・監査ログまで行う。ロールによる作成可否は呼び出し側（Controller）で判定してから呼ぶ。
/// <para>
/// 実績登録で消費部材の引落しが途中で失敗したときは、未保存の在庫変更が追跡中に残る。
/// 単票APIは要求の終わりで破棄されるが、同じ DbContext で続けて処理する呼び出し側は変更を捨てること。
/// </para>
/// </summary>
public sealed class MaintenanceOrderService(
    MesAppDbContext db,
    NumberingService numbering,
    InventoryService inventory,
    IBusinessDateService businessDate,
    IAuditLogger auditLogger)
{
    /// <summary>保全指示の作成。保全計画から作る場合は計画を「指示済み」にする</summary>
    public async Task<Outcome<MaintenanceOrder>> CreateAsync(
        MaintenanceOrderCreateRequest request, string? userId, CancellationToken ct)
    {
        if ((request.EquipmentId is null) == (request.ToolId is null))
        {
            return Outcome<MaintenanceOrder>.Invalid("対象設備IDまたは対象治工具IDのどちらか一方を指定してください。");
        }
        if (request.EquipmentId is int equipmentId
            && !await db.Equipments.AnyAsync(e => e.Id == equipmentId, ct))
        {
            return Outcome<MaintenanceOrder>.Invalid("存在しない設備IDです。");
        }
        if (request.ToolId is int toolId && !await db.Tools.AnyAsync(t => t.Id == toolId, ct))
        {
            return Outcome<MaintenanceOrder>.Invalid("存在しない治工具IDです。");
        }
        if (request.ProcedureId is int procedureId
            && !await db.MaintenanceProcedures.AnyAsync(p => p.Id == procedureId && p.IsActive, ct))
        {
            return Outcome<MaintenanceOrder>.Invalid("存在しない（または無効な）手順書IDです。");
        }

        MaintenancePlan? plan = null;
        if (request.MaintenancePlanId is int planId)
        {
            plan = await db.MaintenancePlans.FirstOrDefaultAsync(p => p.Id == planId, ct);
            if (plan is null)
            {
                return Outcome<MaintenanceOrder>.Invalid("存在しない保全計画IDです。");
            }
            if (plan.Status is not MaintenancePlanStatus.Planned)
            {
                return Outcome<MaintenanceOrder>.Conflict($"状態 '{plan.Status}' の保全計画からは指示を作成できません。");
            }
            plan.Status = MaintenancePlanStatus.Ordered;
        }

        var order = new MaintenanceOrder
        {
            OrderNo = await numbering.NextMaintenanceNoAsync(ct),
            EquipmentId = request.EquipmentId,
            ToolId = request.ToolId,
            MaintenancePlanId = plan?.Id,
            ProcedureId = request.ProcedureId,
            ScheduledDate = request.ScheduledDate,
            RequestType = request.RequestType,
            Note = request.Note,
            CreatedByUserId = userId,
        };
        db.MaintenanceOrders.Add(order);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Maintenance", "OrderCreate", nameof(MaintenanceOrder), order.Id.ToString(),
            detail: $"orderNo={order.OrderNo}, type={order.RequestType}", ct: ct);
        return Outcome<MaintenanceOrder>.Ok(order);
    }

    /// <summary>
    /// 保全実績の登録（E-40-30-01）。登録と同時に指示は完了。元計画は完了、
    /// 治工具メンテでresetToolLife指定時は寿命カウンタをリセットする（E-60-30）。
    /// </summary>
    public async Task<Outcome<MaintenanceOrder>> AddRecordAsync(
        int orderId, MaintenanceRecordRequest request, string userId, CancellationToken ct)
    {
        var order = await db.MaintenanceOrders
            .Include(o => o.MaintenancePlan)
            .Include(o => o.Tool)
            .Include(o => o.Equipment)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null)
        {
            return Outcome<MaintenanceOrder>.NotFound("保全指示が見つかりません。");
        }
        if (order.Status != MaintenanceOrderStatus.Instructed)
        {
            return Outcome<MaintenanceOrder>.Conflict($"状態 '{order.Status}' の保全指示には実績を登録できません。");
        }
        if (request.ResetToolLife && order.Tool is null)
        {
            return Outcome<MaintenanceOrder>.Invalid("寿命リセットは治工具メンテナンスの指示でのみ指定できます。");
        }

        var record = new MaintenanceRecord
        {
            MaintenanceOrderId = orderId,
            PerformedByUserId = userId,
            StartedAt = request.StartedAt,
            EndedAt = request.EndedAt,
            PartsUsed = request.PartsUsed,
            Result = request.Result,
            Note = request.Note,
        };
        // 消費部材の在庫引落し（E-40-30-01）。引落しは InventoryService に一本化してあるので
        // ここでは在庫を直接触らず、業務判定だけを行って同サービスへ渡す
        foreach (var line in request.Parts ?? [])
        {
            var lot = await db.Lots.Include(l => l.Product)
                .FirstOrDefaultAsync(l => l.Id == line.LotId, ct);
            if (lot is null)
            {
                return Outcome<MaintenanceOrder>.Invalid("存在しないロットIDです。");
            }
            // 使える現品かの判定は部材投入・出荷と同じ LotUsabilityPolicy を通す
            if (LotUsabilityPolicy.CheckIssuable(lot, businessDate.Today) is string reason)
            {
                return Outcome<MaintenanceOrder>.Invalid(reason);
            }
            if (await CheckPartCategoryAsync(order, lot, ct) is string categoryError)
            {
                return Outcome<MaintenanceOrder>.Invalid(categoryError);
            }
            try
            {
                await inventory.RemoveAsync(lot, line.LocationId, line.Quantity,
                    InventoryTransactionType.MaintenanceIssue, userId,
                    note: $"保全消費（{order.OrderNo}）", ct: ct);
            }
            catch (InventoryException ex)
            {
                return Outcome<MaintenanceOrder>.Invalid(ex.Message);
            }
            record.Parts.Add(new MaintenanceRecordPart
            {
                ProductId = lot.ProductId,
                LotId = lot.Id,
                LocationId = line.LocationId,
                Quantity = line.Quantity,
                Note = line.Note,
            });
        }
        db.MaintenanceRecords.Add(record);
        order.Status = MaintenanceOrderStatus.Completed;
        if (order.MaintenancePlan is not null)
        {
            order.MaintenancePlan.Status = MaintenancePlanStatus.Completed;
        }
        if (request.ResetToolLife && order.Tool is not null)
        {
            order.Tool.LifeResetAt = DateTimeOffset.UtcNow;
        }
        // 保全が終わった設備を差立に戻す。保全中へは保全担当が設備マスタで切り替える運用のため、
        // 戻すのは保全中の設備だけ（停止中・廃棄の判断を保全実績で上書きしない）
        var restoreEquipment = order.Equipment is { Status: EquipmentStatus.UnderMaintenance };
        if (restoreEquipment)
        {
            order.Equipment!.Status = EquipmentStatus.Available;
        }
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Maintenance", "RecordAdd", nameof(MaintenanceOrder), orderId.ToString(),
            detail: $"orderNo={order.OrderNo}, resetToolLife={request.ResetToolLife}, " +
                    $"parts={record.Parts.Count}", ct: ct);
        if (restoreEquipment)
        {
            await auditLogger.LogAsync("Maintenance", "EquipmentStatusChange", nameof(Equipment),
                order.Equipment!.Id.ToString(),
                detail: new
                {
                    before = EquipmentStatus.UnderMaintenance,
                    after = EquipmentStatus.Available,
                    reason = $"保全指示 {order.OrderNo} の実績登録による復帰",
                }, ct: ct);
        }
        return Outcome<MaintenanceOrder>.Ok(order);
    }

    /// <summary>保全指示の取消。元計画を計画中に戻す（再指示できるように）</summary>
    public async Task<Outcome<MaintenanceOrder>> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await db.MaintenanceOrders.Include(o => o.MaintenancePlan)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null)
        {
            return Outcome<MaintenanceOrder>.NotFound("保全指示が見つかりません。");
        }
        if (order.Status != MaintenanceOrderStatus.Instructed)
        {
            return Outcome<MaintenanceOrder>.Conflict($"状態 '{order.Status}' の保全指示は取消できません。");
        }
        order.Status = MaintenanceOrderStatus.Canceled;
        if (order.MaintenancePlan is { Status: MaintenancePlanStatus.Ordered })
        {
            order.MaintenancePlan.Status = MaintenancePlanStatus.Planned;
        }
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Maintenance", "OrderCancel", nameof(MaintenanceOrder), orderId.ToString(), ct: ct);
        return Outcome<MaintenanceOrder>.Ok(order);
    }

    /// <summary>保全計画の取消（E-30-10）</summary>
    public async Task<Outcome<MaintenancePlan>> CancelPlanAsync(int planId, CancellationToken ct)
    {
        var plan = await db.MaintenancePlans.FindAsync([planId], ct);
        if (plan is null)
        {
            return Outcome<MaintenancePlan>.NotFound("保全計画が見つかりません。");
        }
        // 指示発行済みの計画を取り消すと保全指示だけが「指示済み」で残るため、先に指示を取り消させる
        // （指示の取消で計画は計画中へ戻る）。現場が着手しようとしている指示を計画側の操作で消さない
        if (plan.Status == MaintenancePlanStatus.Ordered)
        {
            return Outcome<MaintenancePlan>.Conflict("保全指示を発行済みの計画は取消できません。先に保全指示を取り消してください。");
        }
        if (plan.Status is MaintenancePlanStatus.Completed or MaintenancePlanStatus.Canceled)
        {
            return Outcome<MaintenancePlan>.Conflict($"状態 '{plan.Status}' の保全計画は取消できません。");
        }
        plan.Status = MaintenancePlanStatus.Canceled;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Maintenance", "PlanCancel", nameof(MaintenancePlan), planId.ToString(), ct: ct);
        return Outcome<MaintenancePlan>.Ok(plan);
    }

    /// <summary>
    /// 消費部材の管理区分の確認（Spec.md 5.7 保全部品は品目マスタで持つ）。
    /// 資産管理部品（金型など）は個体と寿命で管理する対象で、数量在庫の引落しにはなじまない。
    /// 引き落とせば在庫と実物が合わなくなるため、登録済みの区分が資産管理部品なら拒否する。
    /// 保全部品として未登録の品目は、突発保全でありうるため通す（記録できない方が在庫がずれる）。
    /// </summary>
    private async Task<string?> CheckPartCategoryAsync(MaintenanceOrder order, Lot lot, CancellationToken ct)
    {
        if (order.EquipmentId is not int equipmentId)
        {
            return null;
        }
        var part = await db.EquipmentParts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.EquipmentId == equipmentId && p.ProductId == lot.ProductId, ct);
        if (part is { Category: MaintenancePartCategory.Asset })
        {
            return $"品目 '{lot.Product?.Code}' は資産管理部品のため在庫引落しの対象外です" +
                   "（個体と寿命は治工具の寿命管理で扱います）。";
        }
        return null;
    }
}
