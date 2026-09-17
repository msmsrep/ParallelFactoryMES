using System.Security.Claims;
using MesApp.Api.Policies;
using MesApp.Api.Services;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Common;
using MesApp.Core.Contracts.Maintenance;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 保全指示・実績（E-30-20 指示の作成・発行、E-30-30 計画外の保全依頼、E-40 保全実施、
/// E-60-30 治工具メンテナンス）。計画保全の作成は保全ロール、突発依頼は現場からも起票できる。
/// 実績登録（E-40-30-01）で指示は完了し、元計画・治工具寿命カウンタへ連動する。
/// <para>
/// 保全の依頼・実績の記録はロールで絞らない（Spec.md 7.4 の意図的な例外）。
/// 気づいた人がその場で上げられることを優先する。指示・承認は別途ロールで絞る。
/// </para>
/// </summary>
[ApiController]
[Route("api/maintenance-orders")]
[Authorize]
public class MaintenanceOrdersController(
    MesAppDbContext db,
    NumberingService numbering,
    InventoryService inventory,
    IBusinessDateService businessDate,
    IAuditLogger auditLogger) : ControllerBase
{
    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet]
    public async Task<ActionResult<PagedResult<MaintenanceOrderResponse>>> List(
        [FromQuery] PageQuery paging,
        [FromQuery] MaintenanceOrderStatus? status = null,
        [FromQuery] int? equipmentId = null,
        [FromQuery] int? toolId = null,
        CancellationToken ct = default)
    {
        var query = BaseQuery();
        if (status is not null)
        {
            query = query.Where(o => o.Status == status);
        }
        if (equipmentId is not null)
        {
            query = query.Where(o => o.EquipmentId == equipmentId);
        }
        if (toolId is not null)
        {
            query = query.Where(o => o.ToolId == toolId);
        }
        var orders = await query.OrderByDescending(o => o.Id).ToPagedResultAsync(paging, ct);
        return orders.Map(ToResponse);
    }

    /// <summary>保全履歴の詳細（E-20-30-01〜02：いつ・誰が・どう保全したか）</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<MaintenanceOrderResponse>> Get(int id, CancellationToken ct)
    {
        var order = await BaseQuery().FirstOrDefaultAsync(o => o.Id == id, ct);
        return order is null ? NotFound() : ToResponse(order);
    }

    /// <summary>
    /// 保全指示の作成（計画保全 E-30-20-01 は保全ロール、突発依頼 E-30-30-01 は全ユーザー可）
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<MaintenanceOrderResponse>> Create(
        MaintenanceOrderCreateRequest request, CancellationToken ct)
    {
        // 計画保全の作成は保全ロールのみ。突発依頼（Spot）は現場からも起票できる
        if (request.RequestType == MaintenanceRequestType.Planned
            && !User.IsInRole(Core.Constants.MesRoles.SystemAdmin)
            && !User.IsInRole(Core.Constants.MesRoles.Maintenance))
        {
            return Forbid();
        }
        if ((request.EquipmentId is null) == (request.ToolId is null))
        {
            return this.BadRequestProblem("対象設備IDまたは対象治工具IDのどちらか一方を指定してください。");
        }
        if (request.EquipmentId is int equipmentId
            && !await db.Equipments.AnyAsync(e => e.Id == equipmentId, ct))
        {
            return this.BadRequestProblem("存在しない設備IDです。");
        }
        if (request.ToolId is int toolId && !await db.Tools.AnyAsync(t => t.Id == toolId, ct))
        {
            return this.BadRequestProblem("存在しない治工具IDです。");
        }
        if (request.ProcedureId is int procedureId
            && !await db.MaintenanceProcedures.AnyAsync(p => p.Id == procedureId && p.IsActive, ct))
        {
            return this.BadRequestProblem("存在しない（または無効な）手順書IDです。");
        }

        MaintenancePlan? plan = null;
        if (request.MaintenancePlanId is int planId)
        {
            plan = await db.MaintenancePlans.FirstOrDefaultAsync(p => p.Id == planId, ct);
            if (plan is null)
            {
                return this.BadRequestProblem("存在しない保全計画IDです。");
            }
            if (plan.Status is not MaintenancePlanStatus.Planned)
            {
                return this.ConflictProblem($"状態 '{plan.Status}' の保全計画からは指示を作成できません。");
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
            CreatedByUserId = CurrentUserId,
        };
        db.MaintenanceOrders.Add(order);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Maintenance", "OrderCreate", nameof(MaintenanceOrder), order.Id.ToString(),
            detail: $"orderNo={order.OrderNo}, type={order.RequestType}", ct: ct);
        var saved = await BaseQuery().FirstAsync(o => o.Id == order.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, ToResponse(saved));
    }

    /// <summary>
    /// 保全実績の登録（E-40-30-01）。登録と同時に指示は完了。元計画は完了、
    /// 治工具メンテでresetToolLife指定時は寿命カウンタをリセットする（E-60-30）。
    /// </summary>
    [HttpPost("{id:int}/record")]
    public async Task<ActionResult<MaintenanceOrderResponse>> AddRecord(
        int id, MaintenanceRecordRequest request, CancellationToken ct)
    {
        var order = await db.MaintenanceOrders
            .Include(o => o.MaintenancePlan)
            .Include(o => o.Tool)
            .Include(o => o.Equipment)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return NotFound();
        }
        if (order.Status != MaintenanceOrderStatus.Instructed)
        {
            return this.ConflictProblem($"状態 '{order.Status}' の保全指示には実績を登録できません。");
        }
        if (request.ResetToolLife && order.Tool is null)
        {
            return this.BadRequestProblem("寿命リセットは治工具メンテナンスの指示でのみ指定できます。");
        }

        var record = new MaintenanceRecord
        {
            MaintenanceOrderId = id,
            PerformedByUserId = CurrentUserId!,
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
                return this.BadRequestProblem("存在しないロットIDです。");
            }
            // 使える現品かの判定は部材投入・出荷と同じ LotUsabilityPolicy を通す
            if (LotUsabilityPolicy.CheckIssuable(lot, businessDate.Today) is string reason)
            {
                return this.BadRequestProblem(reason);
            }
            if (await CheckPartCategoryAsync(order, lot, ct) is string categoryError)
            {
                return this.BadRequestProblem(categoryError);
            }
            try
            {
                await inventory.RemoveAsync(lot, line.LocationId, line.Quantity,
                    InventoryTransactionType.MaintenanceIssue, CurrentUserId,
                    note: $"保全消費（{order.OrderNo}）", ct: ct);
            }
            catch (InventoryException ex)
            {
                return this.BadRequestProblem(ex.Message);
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
        await auditLogger.LogAsync("Maintenance", "RecordAdd", nameof(MaintenanceOrder), id.ToString(),
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
        var saved = await BaseQuery().FirstAsync(o => o.Id == id, ct);
        return ToResponse(saved);
    }

    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = MesRoleGroups.MaintenanceManage)]
    public async Task<ActionResult<MaintenanceOrderResponse>> Cancel(int id, CancellationToken ct)
    {
        var order = await db.MaintenanceOrders.Include(o => o.MaintenancePlan)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return NotFound();
        }
        if (order.Status != MaintenanceOrderStatus.Instructed)
        {
            return this.ConflictProblem($"状態 '{order.Status}' の保全指示は取消できません。");
        }
        order.Status = MaintenanceOrderStatus.Canceled;
        // 元計画を計画中に戻す（再指示できるように）
        if (order.MaintenancePlan is { Status: MaintenancePlanStatus.Ordered })
        {
            order.MaintenancePlan.Status = MaintenancePlanStatus.Planned;
        }
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Maintenance", "OrderCancel", nameof(MaintenanceOrder), id.ToString(), ct: ct);
        var saved = await BaseQuery().FirstAsync(o => o.Id == id, ct);
        return ToResponse(saved);
    }

    /// <summary>
    /// 消耗材モニタリング（E-20-10-04）。期間内の保全実績で引き落とした部材を品目ごとに集計し、
    /// 現在の在庫合計を並べて返す。補充の要否をこの1画面で判断できるようにする。
    /// </summary>
    [HttpGet("parts-consumption")]
    public async Task<ActionResult<List<MaintenancePartConsumptionRow>>> PartsConsumption(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken ct = default)
    {
        // 期間の基準は「いつ保全したか」なので実績の開始時刻を使う。
        // SQLiteではDateTimeOffsetの比較をSQLへ翻訳できないため、明細を取り出してから絞り込む
        // （保全の消費明細は件数が限られるため、全件の取得で足りる）
        var lines = await (from part in db.MaintenanceRecordParts.AsNoTracking()
                           join rec in db.MaintenanceRecords.AsNoTracking()
                               on part.MaintenanceRecordId equals rec.Id
                           select new
                           {
                               part.ProductId,
                               part.Quantity,
                               part.MaintenanceRecordId,
                               rec.StartedAt,
                           }).ToListAsync(ct);

        if (from is { } fromDate)
        {
            var fromMoment = new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            lines = [.. lines.Where(p => p.StartedAt >= fromMoment)];
        }
        if (to is { } toDate)
        {
            var toMoment = new DateTimeOffset(toDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            lines = [.. lines.Where(p => p.StartedAt < toMoment)];
        }

        var consumed = lines
            .GroupBy(p => p.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                Quantity = g.Sum(x => x.Quantity),
                RecordCount = g.Select(x => x.MaintenanceRecordId).Distinct().Count(),
            })
            .ToList();

        var productIds = consumed.Select(c => c.ProductId).ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Code, p.Name, p.Unit })
            .ToListAsync(ct);
        var stocks = await db.InventoryStocks.AsNoTracking()
            .Where(s => productIds.Contains(s.ProductId))
            .GroupBy(s => s.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToListAsync(ct);

        return consumed
            .Select(c =>
            {
                var product = products.First(p => p.Id == c.ProductId);
                var onHand = stocks.FirstOrDefault(s => s.ProductId == c.ProductId)?.Quantity ?? 0m;
                return new MaintenancePartConsumptionRow(
                    c.ProductId, product.Code, product.Name, product.Unit,
                    c.Quantity, c.RecordCount, onHand);
            })
            .OrderByDescending(r => r.Quantity).ThenBy(r => r.ProductCode)
            .ToList();
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

    private IQueryable<MaintenanceOrder> BaseQuery() =>
        db.MaintenanceOrders.AsNoTracking()
            .Include(o => o.Equipment)
            .Include(o => o.Tool)
            .Include(o => o.Procedure)
            .Include(o => o.Records).ThenInclude(r => r.PerformedBy)
            .Include(o => o.Records).ThenInclude(r => r.Parts).ThenInclude(p => p.Product)
            .Include(o => o.Records).ThenInclude(r => r.Parts).ThenInclude(p => p.Lot)
            .Include(o => o.Records).ThenInclude(r => r.Parts).ThenInclude(p => p.Location);

    private static MaintenanceOrderResponse ToResponse(MaintenanceOrder o) =>
        new(o.Id, o.OrderNo,
            o.EquipmentId, o.Equipment?.Name, o.ToolId, o.Tool?.Name,
            o.MaintenancePlanId, o.ProcedureId, o.Procedure?.ProcedureNo,
            o.ScheduledDate, o.RequestType, o.Status, o.Note, o.CreatedAt,
            o.Records.OrderBy(r => r.Id).Select(r => new MaintenanceRecordResponse(
                r.Id, r.PerformedByUserId, r.PerformedBy?.DisplayName,
                r.StartedAt, r.EndedAt, r.PartsUsed, r.Result, r.Note,
                r.Parts.OrderBy(p => p.Id).Select(p => new MaintenanceRecordPartResponse(
                    p.Id, p.ProductId, p.Product?.Code ?? string.Empty, p.Product?.Name ?? string.Empty,
                    p.Product?.Unit ?? string.Empty,
                    p.LotId, p.Lot?.LotNumber ?? string.Empty,
                    p.LocationId, p.Location?.Code ?? string.Empty,
                    p.Quantity, p.Note)).ToList())).ToList());
}
