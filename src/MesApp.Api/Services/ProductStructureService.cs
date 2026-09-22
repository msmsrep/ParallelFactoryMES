using MesApp.Api.Localization;
using MesApp.Api.Policies;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// 品目の構成（MBOM A-40-10・工順/BOP A-40-20）の一括置換と、設計変更の影響確認（J-40-40-01/03）。
/// 置換は検証・保存・監査ログまで行い、問題があれば日本語の理由を返して何も反映しない。
/// 品目の存在確認は呼び出し側（Controller）で済ませてから呼ぶ。
/// </summary>
public sealed class ProductStructureService(MesAppDbContext db, IAuditLogger auditLogger)
{
    /// <summary>MBOM明細の一括置換（設計変更 A-40-10-05 も本処理で反映）</summary>
    public async Task<string?> ReplaceBomAsync(int productId, List<BomItemRequest> items, CancellationToken ct)
    {
        if (items.Any(i => i.ChildProductId == productId))
        {
            return ApiText.T("品目自身をMBOMの子品目にはできません。");
        }
        if (items.GroupBy(i => i.ChildProductId).Any(g => g.Count() > 1))
        {
            return ApiText.T("同一の子品目が重複しています。");
        }

        var childIds = items.Select(i => i.ChildProductId).ToList();
        var validChildIds = await db.Products
            .Where(p => childIds.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct);
        if (childIds.Except(validChildIds).Any())
        {
            return ApiText.T("存在しない子品目IDが含まれています。");
        }

        var existing = await db.BomItems.Where(b => b.ParentProductId == productId).ToListAsync(ct);
        db.BomItems.RemoveRange(existing);
        db.BomItems.AddRange(items.Select(i => new BomItem
        {
            ParentProductId = productId,
            ChildProductId = i.ChildProductId,
            QuantityPer = i.QuantityPer,
            MakeOrBuy = i.MakeOrBuy,
            AlternativeGroup = i.AlternativeGroup,
            IsAlternative = i.IsAlternative,
            RoutingSequence = i.RoutingSequence,
        }));
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Update", "Bom", productId.ToString(),
            detail: $"items={items.Count}", ct: ct);
        return null;
    }

    /// <summary>工順（BOP）の一括置換（工程変更 A-40-20-03、I-50-30 も本処理で反映）</summary>
    public async Task<string?> ReplaceRoutingAsync(int productId, List<RoutingStepRequest> steps, CancellationToken ct)
    {
        if (steps.GroupBy(s => s.Sequence).Any(g => g.Count() > 1))
        {
            return ApiText.T("工程順序が重複しています。");
        }

        var processIds = steps.Select(s => s.ProcessId).Distinct().ToList();
        var validProcessCount = await db.Processes.CountAsync(p => processIds.Contains(p.Id), ct);
        if (validProcessCount != processIds.Count)
        {
            return ApiText.T("存在しない工程IDが含まれています。");
        }
        foreach (var (ids, set, label) in new[]
        {
            (steps.Where(s => s.RequiredSkillId != null).Select(s => s.RequiredSkillId!.Value), db.Skills.Select(x => x.Id), ApiText.T("スキル")),
            (steps.Where(s => s.EquipmentId != null).Select(s => s.EquipmentId!.Value), db.Equipments.Select(x => x.Id), ApiText.T("設備")),
            (steps.Where(s => s.ToolId != null).Select(s => s.ToolId!.Value), db.Tools.Select(x => x.Id), ApiText.T("治工具")),
            (steps.Where(s => s.ChecklistId != null).Select(s => s.ChecklistId!.Value), db.Checklists.Select(x => x.Id), ApiText.T("チェックリスト")),
            (steps.Where(s => s.WorkProcedureId != null).Select(s => s.WorkProcedureId!.Value),
                ProductStructurePolicy.AssignableWorkProcedures(db.WorkProcedures).Select(x => x.Id), ApiText.T("作業手順書")),
            (steps.SelectMany(s => s.EquipmentIds ?? []), db.Equipments.Select(x => x.Id), ApiText.T("候補設備")),
            (steps.Where(s => s.WorkCenterId != null).Select(s => s.WorkCenterId!.Value),
                ProductStructurePolicy.AssignableWorkCenters(db.WorkCenters).Select(x => x.Id), ApiText.T("作業区")),
        })
        {
            var wanted = ids.Distinct().ToList();
            if (wanted.Count > 0)
            {
                var found = await set.Where(x => wanted.Contains(x)).CountAsync(ct);
                if (found != wanted.Count)
                {
                    return ApiText.T("存在しない{0}IDが含まれています。", label);
                }
            }
        }

        var existing = await db.Routings.Where(r => r.ProductId == productId).ToListAsync(ct);
        db.Routings.RemoveRange(existing);
        db.Routings.AddRange(steps.Select(s => new Routing
        {
            ProductId = productId,
            Sequence = s.Sequence,
            ProcessId = s.ProcessId,
            StandardWorkMinutes = s.StandardWorkMinutes,
            StandardSetupMinutes = s.StandardSetupMinutes,
            RequiredSkillId = s.RequiredSkillId,
            EquipmentId = s.EquipmentId,
            EquipmentCandidates =
            [
                .. ProductStructurePolicy.CandidateEquipmentIds(s.EquipmentIds, s.EquipmentId)
                    .Select(x => new RoutingEquipment { EquipmentId = x }),
            ],
            ToolId = s.ToolId,
            WorkCenterId = s.WorkCenterId,
            ControlItems = s.ControlItems,
            ChecklistId = s.ChecklistId,
            WorkProcedureId = s.WorkProcedureId,
        }));
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Update", "Routing", productId.ToString(),
            detail: $"steps={steps.Count}", ct: ct);
        return null;
    }

    /// <summary>
    /// 設計変更（MBOM・工順の改訂）の影響範囲（J-40-40-01/03）。
    /// <para>
    /// 指図展開時のスナップショット方式（Spec.md 5.7）のため、**マスタを直しても展開済みの指図は変わらない**。
    /// 改訂前にこれを見せ、改訂がどの指図に届き／届かないか、外した部材の在庫がどれだけ残るかを把握させる。
    /// 改訂そのものを止める判定は入れない（止めるべきかは業務側の判断で、機械的には決まらない）
    /// </para>
    /// </summary>
    public async Task<DesignChangeImpactResponse?> GetChangeImpactAsync(int productId, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product is null)
        {
            return null;
        }

        // 完了・取消は設計変更の影響を受けない（作り終わっている）
        var orders = await db.ManufacturingOrders.AsNoTracking()
            .Where(o => o.ProductId == productId
                        && o.Status != ManufacturingOrderStatus.Completed
                        && o.Status != ManufacturingOrderStatus.Canceled)
            .OrderBy(o => o.OrderNo)
            .Select(o => new DesignChangeOrderRow(
                o.Id, o.OrderNo, o.Status, o.Quantity, o.DueDate,
                db.WorkOrders.Count(w => w.ManufacturingOrderId == o.Id
                                         && w.Status != WorkOrderStatus.Canceled),
                db.WorkOrders.Count(w => w.ManufacturingOrderId == o.Id
                                         && w.Status == WorkOrderStatus.Started),
                o.Status == ManufacturingOrderStatus.Released))
            .ToListAsync(ct);

        var orderIds = orders.Select(o => o.OrderId).ToList();

        // 現行MBOMの部材と、進行中指図がスナップショットで持っている部材は一致するとは限らない。
        // 一致しない部材こそ改訂者が見たいもの（外した部材の在庫・まだ要る部材）なので和集合にする
        var bom = await db.BomItems.AsNoTracking()
            .Where(b => b.ParentProductId == productId)
            .Select(b => new { b.ChildProductId, b.QuantityPer })
            .ToListAsync(ct);
        var planned = await db.ManufacturingOrderMaterials.AsNoTracking()
            .Where(m => orderIds.Contains(m.ManufacturingOrderId))
            .GroupBy(m => m.ChildProductId)
            .Select(g => new { ChildProductId = g.Key, Quantity = g.Sum(m => m.PlannedQuantity) })
            .ToListAsync(ct);

        var materialIds = bom.Select(b => b.ChildProductId)
            .Union(planned.Select(p => p.ChildProductId)).ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => materialIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Code, p.Name, p.Unit })
            .ToListAsync(ct);
        var stocks = await db.InventoryStocks.AsNoTracking()
            .Where(s => materialIds.Contains(s.ProductId))
            .GroupBy(s => s.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(s => s.Quantity) })
            .ToListAsync(ct);

        var materials = products
            .Select(p =>
            {
                var line = bom.FirstOrDefault(b => b.ChildProductId == p.Id);
                return new DesignChangeMaterialRow(
                    p.Id, p.Code, p.Name, p.Unit,
                    line is not null, line?.QuantityPer,
                    planned.FirstOrDefault(x => x.ChildProductId == p.Id)?.Quantity ?? 0m,
                    stocks.FirstOrDefault(x => x.ProductId == p.Id)?.Quantity ?? 0m);
            })
            .OrderBy(m => m.Code)
            .ToList();

        return new DesignChangeImpactResponse(
            product.Id, product.Code, product.Name, orders, materials);
    }
}
