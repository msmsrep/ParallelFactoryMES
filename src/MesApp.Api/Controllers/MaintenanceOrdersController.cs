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
    MaintenanceOrderService maintenanceOrders,
    IBusinessDateService businessDate) : ControllerBase
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
            && !Core.Constants.MesRoleGroups.IsInGroup(User, MaintenanceOrderService.PlannedOrderRoles))
        {
            return Forbid();
        }
        var outcome = await maintenanceOrders.CreateAsync(request, null, CurrentUserId, ct);
        if (outcome.Failed)
        {
            return ToProblem(outcome);
        }
        var saved = await BaseQuery().FirstAsync(o => o.Id == outcome.Value!.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = saved.Id }, ToResponse(saved));
    }

    /// <summary>
    /// 保全実績の登録（E-40-30-01）。登録と同時に指示は完了。元計画は完了、
    /// 治工具メンテでresetToolLife指定時は寿命カウンタをリセットする（E-60-30）。
    /// </summary>
    [HttpPost("{id:int}/record")]
    public async Task<ActionResult<MaintenanceOrderResponse>> AddRecord(
        int id, MaintenanceRecordRequest request, CancellationToken ct)
    {
        var outcome = await maintenanceOrders.AddRecordAsync(id, request, CurrentUserId!, ct);
        if (outcome.Failed)
        {
            return ToProblem(outcome);
        }
        var saved = await BaseQuery().FirstAsync(o => o.Id == id, ct);
        return ToResponse(saved);
    }

    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = MesRoleGroups.MaintenanceManage)]
    public async Task<ActionResult<MaintenanceOrderResponse>> Cancel(int id, CancellationToken ct)
    {
        var outcome = await maintenanceOrders.CancelAsync(id, ct);
        if (outcome.Failed)
        {
            return ToProblem(outcome);
        }
        var saved = await BaseQuery().FirstAsync(o => o.Id == id, ct);
        return ToResponse(saved);
    }

    /// <summary>
    /// 消耗材モニタリング（E-20-10-04）。期間内の保全実績で引き落とした部材を品目ごとに集計し、
    /// 現在の在庫合計を並べて返す。補充の要否をこの1画面で判断できるようにする。
    /// <para>期間は製造日（業務日付）基準（Spec.md 3.9）。他の期間APIと同じ半開区間で切る。</para>
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

        // 期間の区切りは製造日の境界（既定6時・工場TZ。Spec.md 3.9）に揃える。
        // 暦日のUTC 0時で切ると、工場の時刻とUTCがずれる時間帯（日本なら0〜9時）の保全実績が
        // 隣の日へ寄り、他の期間API（品質分析・稼働サマリ等）と数字が合わなくなる
        if (from is { } fromDate)
        {
            var fromMoment = businessDate.GetRange(fromDate).Start;
            lines = [.. lines.Where(p => p.StartedAt >= fromMoment)];
        }
        if (to is { } toDate)
        {
            // 終端は翌製造日の開始時刻。他の期間APIと同じ半開区間にして二重計上を防ぐ
            var toMoment = businessDate.GetRange(toDate).End;
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

    private ActionResult ToProblem<T>(Outcome<T> outcome) => outcome.Kind switch
    {
        OutcomeError.NotFound => NotFound(),
        OutcomeError.Conflict => this.ConflictProblem(outcome.Error),
        _ => this.BadRequestProblem(outcome.Error),
    };

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
