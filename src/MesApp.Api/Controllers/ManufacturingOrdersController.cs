using System.Security.Claims;
using MesApp.Api.Services;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Common;
using MesApp.Core.Contracts.Production;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 製造指図（Spec.md 3.1：A-20 発行・承認・変更、B-10-10 工程展開・産出ロット採番、
/// A-30 進捗モニタリング、B-70-10 リワーク指図、B-10-10-04 突発指図）
/// </summary>
[ApiController]
[Route("api/manufacturing-orders")]
[Authorize]
public class ManufacturingOrdersController(
    MesAppDbContext db,
    WorkOrderStatusService workOrderStatus,
    ManufacturingOrderService orders,
    IBusinessDateService businessDate,
    IAuditLogger auditLogger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ManufacturingOrderResponse>>> List(
        [FromQuery] PageQuery paging,
        [FromQuery] ManufacturingOrderStatus? status = null,
        [FromQuery] int? productId = null,
        CancellationToken ct = default)
    {
        var query = db.ManufacturingOrders.AsNoTracking()
            .Include(o => o.Product).Include(o => o.OutputLot)
            .AsQueryable();
        if (status is not null)
        {
            query = query.Where(o => o.Status == status);
        }
        if (productId is not null)
        {
            query = query.Where(o => o.ProductId == productId);
        }
        var orders = await query.OrderByDescending(o => o.Id).ToPagedResultAsync(paging, ct);
        return orders.Map(ToResponse);
    }

    /// <summary>進捗モニタリング（A-30-10-01 指図単位の進捗、A-30-20-01 納期遅延の把握）</summary>
    [HttpGet("progress")]
    public async Task<ActionResult<List<OrderProgressResponse>>> Progress(
        [FromQuery] bool overdueOnly = false, CancellationToken ct = default)
    {
        var today = businessDate.Today;
        var rows = await db.ManufacturingOrders.AsNoTracking()
            .Where(o => o.Status != ManufacturingOrderStatus.Canceled)
            .OrderBy(o => o.DueDate == null).ThenBy(o => o.DueDate).ThenBy(o => o.Id)
            .Select(o => new OrderProgressResponse(
                o.Id, o.OrderNo, o.Product!.Code, o.Product!.Name,
                o.Quantity, o.DueDate, o.OrderType, o.Status,
                o.WorkOrders.Count(w => w.Status != WorkOrderStatus.Canceled),
                o.WorkOrders.Count(w =>
                    w.Status == WorkOrderStatus.Completed || w.Status == WorkOrderStatus.Approved),
                o.Status != ManufacturingOrderStatus.Completed && o.DueDate != null && o.DueDate < today))
            .ToListAsync(ct);
        return overdueOnly ? rows.Where(r => r.IsOverdue).ToList() : rows;
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ManufacturingOrderDetailResponse>> Get(int id, CancellationToken ct)
    {
        var order = await db.ManufacturingOrders.AsNoTracking()
            .Include(o => o.Product).Include(o => o.OutputLot)
            .Include(o => o.WorkOrders).ThenInclude(w => w.Process)
            .Include(o => o.WorkOrders).ThenInclude(w => w.AssignedUser)
            .Include(o => o.WorkOrders).ThenInclude(w => w.AssignedEquipment)
            .Include(o => o.Materials).ThenInclude(m => m.ChildProduct)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return NotFound();
        }
        return new ManufacturingOrderDetailResponse(
            ToResponse(order),
            order.WorkOrders.OrderBy(w => w.RoutingSequence)
                .Select(w => WorkOrdersController.ToResponse(w, order)).ToList(),
            order.Materials.OrderBy(m => m.ChildProduct!.Code)
                .Select(m => new OrderMaterialResponse(
                    m.ChildProductId, m.ChildProduct!.Code, m.ChildProduct!.Name,
                    m.QuantityPer, m.PlannedQuantity, m.AlternativeGroup)).ToList());
    }

    /// <summary>指図登録（A-20-10-01 手動登録、B-10-10-04 突発、B-70-10-01 リワーク）</summary>
    [HttpPost]
    [Authorize(Roles = MesRoleGroups.ProductionManage)]
    public async Task<ActionResult<ManufacturingOrderResponse>> Create(
        CreateManufacturingOrderRequest request, CancellationToken ct)
    {
        var outcome = await orders.CreateAsync(request, null, User.FindFirstValue(ClaimTypes.NameIdentifier), ct);
        if (outcome.Order is not { } order)
        {
            return ToProblem(outcome);
        }
        return CreatedAtAction(nameof(Get), new { id = order.Id }, ToResponse(order));
    }

    /// <summary>指図変更（A-20-20-02。承認済みの指図を変更すると未承認に戻り再承認が必要）</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = MesRoleGroups.ProductionManage)]
    public async Task<ActionResult<ManufacturingOrderResponse>> Update(
        int id, UpdateManufacturingOrderRequest request, CancellationToken ct)
    {
        var order = await db.ManufacturingOrders.Include(o => o.Product).Include(o => o.OutputLot)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return NotFound();
        }
        if (order.Status is not (ManufacturingOrderStatus.Draft or ManufacturingOrderStatus.Approved))
        {
            return Conflict(new ProblemDetails
            {
                Title = $"状態 '{order.Status}' の指図は変更できません（展開済み以降は取消のみ可能です）。",
            });
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
        await auditLogger.LogAsync("Production", "Update", nameof(ManufacturingOrder), id.ToString(),
            detail: $"orderNo={order.OrderNo}, reapprovalRequired={reapproval}", ct: ct);
        return ToResponse(order);
    }

    /// <summary>指図承認（A-20-20-01。単段階承認：Spec.md 3.9）</summary>
    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = MesRoleGroups.ProductionManage)]
    public async Task<ActionResult<ManufacturingOrderResponse>> Approve(int id, CancellationToken ct)
    {
        var order = await db.ManufacturingOrders.Include(o => o.Product).Include(o => o.OutputLot)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return NotFound();
        }
        var outcome = await orders.ApproveAsync(order, User.FindFirstValue(ClaimTypes.NameIdentifier), ct);
        return outcome.Order is null ? ToProblem(outcome) : ToResponse(order);
    }

    /// <summary>指図取消（A-20-20-02。取消時は未完了の作業指示も取消する）</summary>
    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = MesRoleGroups.ProductionManage)]
    public async Task<ActionResult<ManufacturingOrderResponse>> Cancel(int id, CancellationToken ct)
    {
        var order = await db.ManufacturingOrders
            .Include(o => o.Product).Include(o => o.OutputLot).Include(o => o.WorkOrders)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return NotFound();
        }
        if (order.Status is ManufacturingOrderStatus.Completed or ManufacturingOrderStatus.Canceled)
        {
            return Conflict(new ProblemDetails { Title = $"状態 '{order.Status}' の指図は取消できません。" });
        }

        order.Status = ManufacturingOrderStatus.Canceled;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        foreach (var workOrder in order.WorkOrders.Where(w =>
                     w.Status is not (WorkOrderStatus.Completed or WorkOrderStatus.Approved)))
        {
            workOrderStatus.ChangeStatus(workOrder, WorkOrderStatus.Canceled,
                WorkOrderStatusChangeSource.OrderCancel, User.FindFirstValue(ClaimTypes.NameIdentifier),
                $"指図 {order.OrderNo} の取消に連動");
        }
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Production", "Cancel", nameof(ManufacturingOrder), id.ToString(),
            detail: $"orderNo={order.OrderNo}", ct: ct);
        return ToResponse(order);
    }

    /// <summary>
    /// 工程展開（B-10-10-01：工順に基づき工程単位の作業指示に展開）＋産出ロット採番（B-10-10-05：
    /// 自動＝品目コード-日付-連番、または手入力）。承認済みの指図のみ展開できる。
    /// </summary>
    [HttpPost("{id:int}/expand")]
    [Authorize(Roles = MesRoleGroups.ProductionManage)]
    public async Task<ActionResult<ManufacturingOrderDetailResponse>> Expand(
        int id, ExpandRequest request, CancellationToken ct)
    {
        var order = await db.ManufacturingOrders.Include(o => o.Product)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return NotFound();
        }
        var outcome = await orders.ExpandAsync(order, request.LotNumber, ct);
        return outcome.Order is null ? ToProblem(outcome) : await Get(id, ct);
    }

    private ActionResult ToProblem(OrderOutcome outcome)
    {
        var problem = new ProblemDetails { Title = outcome.Error };
        return outcome.IsConflict ? Conflict(problem) : BadRequest(problem);
    }

    private static ManufacturingOrderResponse ToResponse(ManufacturingOrder o) =>
        new(o.Id, o.OrderNo, o.ProductId, o.Product!.Code, o.Product!.Name,
            o.Quantity, o.DueDate, o.OrderType, o.Status,
            o.ApprovedByUserId, o.ApprovedAt, o.SourceOrderId,
            o.OutputLot?.LotNumber, o.Note, o.CreatedAt);
}
