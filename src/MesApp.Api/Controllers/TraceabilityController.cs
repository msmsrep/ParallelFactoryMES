using MesApp.Core.Contracts.Quality;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// ロットトレーサビリティ（Spec.md 3.7：H-30-10）。
/// 追跡の連鎖：部材ロット → 部材投入（作業指示に紐付く）→ 作業指示 → 生産実績 → 産出ロット。
/// トレースバック（製造元特定）は産出ロットから部材側へ、トレースフォワード（使用先特定）は
/// 部材ロットから産出側へ辿る。分割・振替の系譜（Lot.ParentLotId）も追跡に含める。
/// </summary>
[ApiController]
[Route("api/traceability")]
[Authorize]
public class TraceabilityController(MesAppDbContext db) : ControllerBase
{
    private const int MaxDepth = 10;

    /// <summary>トレースバック（H-30-10-01：問題品目の製造元特定。産出ロット→投入部材ロット）</summary>
    [HttpGet("{lotId:int}/back")]
    public async Task<ActionResult<TraceResponse>> TraceBack(int lotId, CancellationToken ct)
    {
        var lot = await db.Lots.AsNoTracking().Include(l => l.Product)
            .FirstOrDefaultAsync(l => l.Id == lotId, ct);
        if (lot is null)
        {
            return NotFound();
        }
        var nodes = await BuildBackNodesAsync(lot, [lot.Id], 0, ct);
        return new TraceResponse(lot.Id, lot.LotNumber, lot.Product!.Code, lot.Product!.Name, nodes);
    }

    /// <summary>トレースフォワード（H-30-10-02：使用先特定。部材ロット→産出ロット）</summary>
    [HttpGet("{lotId:int}/forward")]
    public async Task<ActionResult<TraceResponse>> TraceForward(int lotId, CancellationToken ct)
    {
        var lot = await db.Lots.AsNoTracking().Include(l => l.Product)
            .FirstOrDefaultAsync(l => l.Id == lotId, ct);
        if (lot is null)
        {
            return NotFound();
        }
        var nodes = await BuildForwardNodesAsync(lot, [lot.Id], 0, ct);
        return new TraceResponse(lot.Id, lot.LotNumber, lot.Product!.Code, lot.Product!.Name, nodes);
    }

    /// <summary>製造・検査・在庫履歴の閲覧（H-30-10-03〜05）</summary>
    [HttpGet("{lotId:int}/history")]
    public async Task<ActionResult<LotHistoryResponse>> History(int lotId, CancellationToken ct)
    {
        var lot = await db.Lots.AsNoTracking().Include(l => l.Product)
            .FirstOrDefaultAsync(l => l.Id == lotId, ct);
        if (lot is null)
        {
            return NotFound();
        }

        var production = await db.ProductionRecords.AsNoTracking()
            .Where(r => r.OutputLotId == lotId)
            .OrderBy(r => r.Id)
            .Select(r => $"{r.WorkOrder!.WorkOrderNo}: 良品{r.GoodQuantity} 不良{r.DefectQuantity} " +
                         $"(作業者: {r.PerformedBy!.DisplayName})")
            .ToListAsync(ct);

        var inspections = await db.InspectionOrders.AsNoTracking()
            .Where(i => i.TargetLotId == lotId)
            .OrderBy(i => i.Id)
            .Select(i => $"{i.OrderNo} [{i.Type}] {i.Status}" +
                         (i.OverallJudgment != null ? $" 判定: {i.OverallJudgment}" : string.Empty))
            .ToListAsync(ct);

        var transactions = await db.InventoryTransactions.AsNoTracking()
            .Where(t => t.LotId == lotId)
            .OrderBy(t => t.Id)
            .Select(t => $"[{t.Type}] 数量{t.Quantity}" +
                         (t.FromLocation != null ? $" from {t.FromLocation.Code}" : string.Empty) +
                         (t.ToLocation != null ? $" to {t.ToLocation.Code}" : string.Empty) +
                         (t.WorkOrder != null ? $" ({t.WorkOrder.WorkOrderNo})" : string.Empty))
            .ToListAsync(ct);

        return new LotHistoryResponse(lot.Id, lot.LotNumber, lot.Product!.Code, lot.Product!.Name,
            production, inspections, transactions);
    }

    /// <summary>産出ロット→（生成元作業指示の指図の全作業指示）→投入部材ロットを再帰的に辿る</summary>
    private async Task<List<TraceNode>> BuildBackNodesAsync(
        Lot lot, HashSet<int> visited, int depth, CancellationToken ct)
    {
        if (depth >= MaxDepth)
        {
            return [];
        }
        var nodes = new List<TraceNode>();

        // 系譜（分割・振替の親ロット）を辿る
        if (lot.ParentLotId is int parentId && visited.Add(parentId))
        {
            var parent = await db.Lots.AsNoTracking().Include(l => l.Product)
                .FirstAsync(l => l.Id == parentId, ct);
            nodes.Add(new TraceNode(parent.Id, parent.LotNumber, parent.Product!.Code, parent.Product!.Name,
                parent.StockStatus, null, null, IsLineage: true,
                await BuildBackNodesAsync(parent, visited, depth + 1, ct)));
        }

        // 産出ロット → 同一指図の作業指示群 → 部材投入
        var workOrderIds = await GetProducingWorkOrderIdsAsync(lot.Id, ct);
        if (workOrderIds.Count > 0)
        {
            var consumptions = await db.MaterialConsumptions.AsNoTracking()
                .Include(c => c.Lot).ThenInclude(l => l!.Product)
                .Include(c => c.WorkOrder)
                .Where(c => workOrderIds.Contains(c.WorkOrderId))
                .OrderBy(c => c.Id)
                .ToListAsync(ct);
            foreach (var consumption in consumptions)
            {
                var materialLot = consumption.Lot!;
                var children = visited.Add(materialLot.Id)
                    ? await BuildBackNodesAsync(materialLot, visited, depth + 1, ct)
                    : [];
                nodes.Add(new TraceNode(materialLot.Id, materialLot.LotNumber,
                    materialLot.Product!.Code, materialLot.Product!.Name, materialLot.StockStatus,
                    consumption.WorkOrder!.WorkOrderNo, consumption.Quantity, IsLineage: false, children));
            }
        }
        return nodes;
    }

    /// <summary>部材ロット→投入先作業指示→指図の産出ロット、および派生ロットを再帰的に辿る</summary>
    private async Task<List<TraceNode>> BuildForwardNodesAsync(
        Lot lot, HashSet<int> visited, int depth, CancellationToken ct)
    {
        if (depth >= MaxDepth)
        {
            return [];
        }
        var nodes = new List<TraceNode>();

        // 系譜（このロットから分割・振替された子ロット）
        var childLots = await db.Lots.AsNoTracking().Include(l => l.Product)
            .Where(l => l.ParentLotId == lot.Id)
            .ToListAsync(ct);
        foreach (var child in childLots.Where(c => visited.Add(c.Id)))
        {
            nodes.Add(new TraceNode(child.Id, child.LotNumber, child.Product!.Code, child.Product!.Name,
                child.StockStatus, null, null, IsLineage: true,
                await BuildForwardNodesAsync(child, visited, depth + 1, ct)));
        }

        // このロットが投入された作業指示 → その指図の産出ロット
        var usages = await db.MaterialConsumptions.AsNoTracking()
            .Include(c => c.WorkOrder).ThenInclude(w => w!.ManufacturingOrder).ThenInclude(o => o!.OutputLot)
            .ThenInclude(l => l!.Product)
            .Where(c => c.LotId == lot.Id)
            .OrderBy(c => c.Id)
            .ToListAsync(ct);
        foreach (var usage in usages)
        {
            var outputLot = usage.WorkOrder!.ManufacturingOrder!.OutputLot;
            if (outputLot is null)
            {
                continue;
            }
            var children = visited.Add(outputLot.Id)
                ? await BuildForwardNodesAsync(outputLot, visited, depth + 1, ct)
                : [];
            nodes.Add(new TraceNode(outputLot.Id, outputLot.LotNumber,
                outputLot.Product!.Code, outputLot.Product!.Name, outputLot.StockStatus,
                usage.WorkOrder!.WorkOrderNo, usage.Quantity, IsLineage: false, children));
        }
        return nodes;
    }

    /// <summary>ロットを産出した指図の作業指示ID群（部材投入の紐付け先）を取得</summary>
    private async Task<List<int>> GetProducingWorkOrderIdsAsync(int lotId, CancellationToken ct)
    {
        var orderId = await db.ManufacturingOrders.AsNoTracking()
            .Where(o => o.OutputLotId == lotId)
            .Select(o => (int?)o.Id)
            .FirstOrDefaultAsync(ct);
        if (orderId is null)
        {
            return [];
        }
        return await db.WorkOrders.AsNoTracking()
            .Where(w => w.ManufacturingOrderId == orderId)
            .Select(w => w.Id)
            .ToListAsync(ct);
    }
}
