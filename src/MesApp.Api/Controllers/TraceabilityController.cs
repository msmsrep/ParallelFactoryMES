using MesApp.Core.Localization;
using MesApp.Core.Abstractions;
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
/// 部材ロットから産出側へ辿る。分割・統合・振替の系譜（LotGenealogy）も追跡に含める。
/// </summary>
[ApiController]
[Route("api/traceability")]
[Authorize]
public class TraceabilityController(MesAppDbContext db, IBusinessDateService businessDate) : ControllerBase
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

        // 直は「夜勤だけ不良が出る」のような追い方をするときに要るので、作業者と並べて出す。
        // 組み立てはSQLに載せず、取り出してから行う（直なしの分岐をSQL側に持たせない）
        var production = (await db.ProductionRecords.AsNoTracking()
                .Where(r => r.OutputLotId == lotId)
                .OrderBy(r => r.Id)
                .Select(r => new
                {
                    r.WorkOrder!.WorkOrderNo,
                    r.GoodQuantity,
                    r.DefectQuantity,
                    Performer = r.PerformedBy!.DisplayName,
                    ShiftCode = r.Shift!.Code,
                    ShiftName = r.Shift!.Name,
                })
                .ToListAsync(ct))
            .Select(r => $"{r.WorkOrderNo}: 良品{r.GoodQuantity} 不良{r.DefectQuantity} (作業者: {r.Performer}"
                         + (r.ShiftCode is null ? string.Empty : $" / 直: {r.ShiftCode} {r.ShiftName}")
                         + ")")
            .ToList();

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

        // 状態履歴（保留・解除などの遷移。誰が・いつ・なぜ止め、どの判断で解除したか）
        var statusHistory = await db.LotStatusHistories.AsNoTracking()
            .Where(h => h.LotId == lotId)
            .OrderBy(h => h.Id)
            .Select(h => new LotStatusHistoryEntry(
                h.FromStatus, h.ToStatus, h.Source, h.Reason,
                db.Users.Where(u => u.Id == h.ChangedByUserId).Select(u => u.DisplayName).FirstOrDefault(),
                h.ChangedAt))
            .ToListAsync(ct);

        // 実績訂正の履歴（B-70-30-01）。このロットを産出した実績への訂正を時系列で出す
        var correctionHistory = await db.ProductionRecordCorrections.AsNoTracking()
            .Where(c => c.ProductionRecord!.OutputLotId == lotId)
            .OrderBy(c => c.Id)
            .Select(c => new ProductionCorrectionEntry(
                c.WorkOrder!.WorkOrderNo,
                c.BeforeGoodQuantity, c.BeforeDefectQuantity,
                c.BeforeScrapQuantity, c.BeforeReworkQuantity,
                c.AfterGoodQuantity, c.AfterDefectQuantity,
                c.AfterScrapQuantity, c.AfterReworkQuantity,
                c.Reason, c.CorrectedBy!.DisplayName, c.CorrectedAt))
            .ToListAsync(ct);

        // 設備稼働履歴（H-30-10-04）。このロットを産出した作業指示に紐づく稼働区間を出す。
        // 製造品の実績（PQC）と設備の実績（EQC）の交差点にあたり、品質不良の原因を
        // 設備の状態から追えるようにするためのもの（Spec.md 5.7 2軸データの紐付け）
        var producingWorkOrderIds = await db.ProductionRecords.AsNoTracking()
            .Where(r => r.OutputLotId == lotId)
            .Select(r => r.WorkOrderId)
            .Distinct()
            .ToListAsync(ct);
        // 状態は日本語で出すため、文字列の組み立てはDBから取り出したあとに行う
        var equipmentLogs = await db.EquipmentLogs.AsNoTracking()
            .Where(l => l.WorkOrderId != null && producingWorkOrderIds.Contains(l.WorkOrderId.Value))
            .OrderBy(l => l.Id)
            .Select(l => new
            {
                l.Equipment!.AssetNo,
                EquipmentName = l.Equipment!.Name,
                l.Status,
                l.StartedAt,
                l.EndedAt,
                WorkOrderNo = l.WorkOrder!.WorkOrderNo,
                l.StopCause,
            })
            .ToListAsync(ct);
        var equipmentHistory = equipmentLogs
            .Select(l => $"{l.AssetNo} {l.EquipmentName}: [{EnumLabels.Of(l.Status)}] " +
                         $"{businessDate.ToFactoryTime(l.StartedAt):yyyy-MM-dd HH:mm}〜" +
                         (l.EndedAt is { } ended
                             ? $"{businessDate.ToFactoryTime(ended):yyyy-MM-dd HH:mm}"
                             : "（継続中）") +
                         $" ({l.WorkOrderNo})" +
                         (l.StopCause is not null ? $" 原因: {l.StopCause}" : string.Empty))
            .ToList();

        // 製造条件の逸脱（B-30-30-04）。不良の原因を「どの条件が外れていたか」から追えるようにする。
        // 範囲内の記録まで並べると件数が多くなり、見るべきものが埋もれる
        var deviations = await db.ProductionDataRecords.AsNoTracking()
            .Where(r => r.IsDeviation == true && producingWorkOrderIds.Contains(r.WorkOrderId))
            .OrderBy(r => r.Id)
            .Select(r => new
            {
                r.Item,
                r.Value,
                r.RecordedAt,
                WorkOrderNo = r.WorkOrder!.WorkOrderNo,
                Lower = r.WorkOrderControlItem!.LowerLimit,
                Upper = r.WorkOrderControlItem!.UpperLimit,
                Unit = r.WorkOrderControlItem!.Unit,
            })
            .ToListAsync(ct);
        var controlItemDeviations = deviations
            .Select(d => $"{d.Item}: 実績 {d.Value}（許容 {d.Lower?.ToString() ?? "-"}〜" +
                         $"{d.Upper?.ToString() ?? "-"}{d.Unit}） " +
                         $"{businessDate.ToFactoryTime(d.RecordedAt):yyyy-MM-dd HH:mm} ({d.WorkOrderNo})")
            .ToList();

        return new LotHistoryResponse(lot.Id, lot.LotNumber, lot.Product!.Code, lot.Product!.Name,
            production, inspections, transactions, statusHistory, correctionHistory,
            equipmentHistory, controlItemDeviations);
    }

    /// <summary>稼働状態の日本語名（履歴は人が読む前提のため）</summary>

    /// <summary>産出ロット→（生成元作業指示の指図の全作業指示）→投入部材ロットを再帰的に辿る</summary>
    private async Task<List<TraceNode>> BuildBackNodesAsync(
        Lot lot, HashSet<int> visited, int depth, CancellationToken ct)
    {
        if (depth >= MaxDepth)
        {
            return [];
        }
        var nodes = new List<TraceNode>();

        // 系譜（分割・統合・振替の由来元ロット）を辿る。統合では親が複数になりうる
        var origins = await db.LotGenealogies.AsNoTracking()
            .Include(g => g.ParentLot).ThenInclude(l => l!.Product)
            .Where(g => g.ChildLotId == lot.Id)
            .OrderBy(g => g.Id)
            .ToListAsync(ct);
        foreach (var origin in origins.Where(g => visited.Add(g.ParentLotId)))
        {
            var parent = origin.ParentLot!;
            nodes.Add(new TraceNode(parent.Id, parent.LotNumber, parent.Product!.Code, parent.Product!.Name,
                parent.StockStatus, null, origin.Quantity, origin.RelationType,
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
                    consumption.WorkOrder!.WorkOrderNo, consumption.Quantity, Relation: null, children));
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

        // 系譜（このロットから分割・統合・振替された先のロット）
        var derived = await db.LotGenealogies.AsNoTracking()
            .Include(g => g.ChildLot).ThenInclude(l => l!.Product)
            .Where(g => g.ParentLotId == lot.Id)
            .OrderBy(g => g.Id)
            .ToListAsync(ct);
        foreach (var relation in derived.Where(g => visited.Add(g.ChildLotId)))
        {
            var child = relation.ChildLot!;
            nodes.Add(new TraceNode(child.Id, child.LotNumber, child.Product!.Code, child.Product!.Name,
                child.StockStatus, null, relation.Quantity, relation.RelationType,
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
                usage.WorkOrder!.WorkOrderNo, usage.Quantity, Relation: null, children));
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
