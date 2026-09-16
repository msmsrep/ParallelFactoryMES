using MesApp.Api.Policies;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Execution;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>業務処理の失敗の種類（HTTPの 404 / 400 / 409 に対応する）</summary>
public enum OutcomeError
{
    None,
    NotFound,
    Invalid,
    Conflict,
}

/// <summary>業務処理の結果（Errorが無ければ Value に結果が入る）</summary>
public sealed record Outcome<T>(T? Value, OutcomeError Kind = OutcomeError.None, string? Error = null)
{
    public bool Failed => Kind != OutcomeError.None;

    public static Outcome<T> Ok(T value) => new(value);
    public static Outcome<T> NotFound(string error) => new(default, OutcomeError.NotFound, error);
    public static Outcome<T> Invalid(string error) => new(default, OutcomeError.Invalid, error);
    public static Outcome<T> Conflict(string error) => new(default, OutcomeError.Conflict, error);
}

/// <summary>生産実績の登録結果（応答の組み立てに必要な付随情報を含む）</summary>
public sealed record ProductionRecordResult(ProductionRecord Record, WorkOrder WorkOrder, Lot? OutputLot, Shift? Shift);

/// <summary>
/// 作業指示の実行記録（Spec.md 3.2 製造実行）：段取り実績（B-20-50、B-40-40）、チェックリスト実施（B-30-10）、
/// 部材投入（B-30-20）、生産実績＋バックフラッシュ（B-40-10）、製造条件データ（B-30-30-04）。
/// 単票API（<c>WorkOrderExecutionController</c>）と実績CSV取込（<c>ActualCsvService</c>）の両方から呼ぶ。
/// <para>
/// 保存と監査ログまで行う。トランザクションは呼び出し側が張る。
/// <b>失敗を返したときは未保存の変更が追跡中に残ることがある</b>（バックフラッシュの途中で在庫不足になった場合など）。
/// 単票APIは要求の終わりで破棄されるが、同じ DbContext で続けて処理する呼び出し側は変更を捨てること。
/// </para>
/// </summary>
public sealed class WorkOrderExecutionService(
    MesAppDbContext db,
    WorkOrderStatusService workOrderStatus,
    InventoryService inventory,
    IBusinessDateService businessDate,
    IAuditLogger auditLogger)
{
    /// <summary>段取り実績（B-20-50 前段取り／B-40-40 後段取り）</summary>
    public async Task<Outcome<SetupRecord>> AddSetupRecordAsync(
        int workOrderId, SetupRecordRequest request, string userId, CancellationToken ct)
    {
        var workOrder = await db.WorkOrders.FirstOrDefaultAsync(w => w.Id == workOrderId, ct);
        if (workOrder is null)
        {
            return Outcome<SetupRecord>.NotFound("作業指示が存在しません。");
        }
        if (workOrder.Status is WorkOrderStatus.Approved or WorkOrderStatus.Canceled)
        {
            return Outcome<SetupRecord>.Conflict($"状態 '{workOrder.Status}' の作業指示には記録できません。");
        }

        var record = new SetupRecord
        {
            WorkOrderId = workOrderId,
            Type = request.Type,
            StartedAt = request.StartedAt,
            EndedAt = request.EndedAt,
            PerformedByUserId = userId,
            AbnormalityNote = request.AbnormalityNote,
        };
        db.SetupRecords.Add(record);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Execution", "SetupRecord", nameof(WorkOrder), workOrderId.ToString(),
            detail: $"type={request.Type}", ct: ct);
        return Outcome<SetupRecord>.Ok(record);
    }

    /// <summary>チェックリスト実施記録（B-30-10）。必須項目が未チェックの場合は登録を拒否する</summary>
    public async Task<Outcome<ChecklistRecord>> AddChecklistRecordAsync(
        int workOrderId, ChecklistRecordRequest request, string userId, CancellationToken ct)
    {
        if (!await db.WorkOrders.AnyAsync(w => w.Id == workOrderId, ct))
        {
            return Outcome<ChecklistRecord>.NotFound("作業指示が存在しません。");
        }
        var checklist = await db.Checklists.AsNoTracking().Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == request.ChecklistId, ct);
        if (checklist is null)
        {
            return Outcome<ChecklistRecord>.Invalid("存在しないチェックリストIDです。");
        }

        var resultByItem = request.Results.ToDictionary(r => r.ChecklistItemId);
        var itemIds = checklist.Items.Select(i => i.Id).ToHashSet();
        if (request.Results.Any(r => !itemIds.Contains(r.ChecklistItemId)))
        {
            return Outcome<ChecklistRecord>.Invalid("チェックリストに存在しない項目IDが含まれています。");
        }
        var missingRequired = checklist.Items
            .Where(i => i.IsRequired &&
                        (!resultByItem.TryGetValue(i.Id, out var r) || !r.IsChecked))
            .Select(i => i.Text)
            .ToList();
        if (missingRequired.Count > 0)
        {
            return Outcome<ChecklistRecord>.Invalid($"必須項目が未チェックです: {string.Join("、", missingRequired)}");
        }

        var record = new ChecklistRecord
        {
            ChecklistId = checklist.Id,
            WorkOrderId = workOrderId,
            PerformedByUserId = userId,
            Results = checklist.Items.Select(i => new ChecklistResultItem
            {
                ChecklistItemId = i.Id,
                IsChecked = resultByItem.TryGetValue(i.Id, out var r) && r.IsChecked,
                Note = resultByItem.TryGetValue(i.Id, out var r2) ? r2.Note : null,
            }).ToList(),
        };
        db.ChecklistRecords.Add(record);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Execution", "ChecklistRecord", nameof(WorkOrder), workOrderId.ToString(),
            detail: $"checklist={checklist.Code}", ct: ct);
        return Outcome<ChecklistRecord>.Ok(record);
    }

    /// <summary>部材投入（B-30-20-01 手動記録。在庫の払出と投入実績を同時記録）</summary>
    public async Task<Outcome<MaterialConsumption>> AddConsumptionAsync(
        int workOrderId, ConsumptionRequest request, string? userId, CancellationToken ct)
    {
        var workOrder = await db.WorkOrders.Include(w => w.Product)
            .FirstOrDefaultAsync(w => w.Id == workOrderId, ct);
        if (workOrder is null)
        {
            return Outcome<MaterialConsumption>.NotFound("作業指示が存在しません。");
        }
        if (workOrder.Status is WorkOrderStatus.Approved or WorkOrderStatus.Canceled)
        {
            return Outcome<MaterialConsumption>.Conflict($"状態 '{workOrder.Status}' の作業指示には記録できません。");
        }
        var lot = await db.Lots.Include(l => l.Product).FirstOrDefaultAsync(l => l.Id == request.LotId, ct);
        if (lot is null)
        {
            return Outcome<MaterialConsumption>.Invalid("存在しないロットIDです。");
        }
        // 投入可否（ステータス・有効期限）の判定は LotUsabilityPolicy に集約している
        if (LotUsabilityPolicy.CheckIssuable(lot, businessDate.Today) is string reason)
        {
            return Outcome<MaterialConsumption>.Invalid(reason);
        }
        // 指定外材料の投入を防ぐ（B-30-20）。基準は指図展開時に固定した予定材料であり、
        // 途中でMBOMが改訂されてもこの指図の照合条件は変わらない（Spec.md 5.7）
        var planned = await db.ManufacturingOrderMaterials.AsNoTracking()
            .Where(m => m.ManufacturingOrderId == workOrder.ManufacturingOrderId)
            .ToListAsync(ct);
        if (MaterialIssuePolicy.CheckAgainstBom(
                workOrder.Product!.Code, planned, lot.Product!, request.SubstituteReason) is string bomReason)
        {
            return Outcome<MaterialConsumption>.Invalid(bomReason);
        }
        var isSubstitute = MaterialIssuePolicy.IsSubstitute(planned, lot.Product!);

        try
        {
            await inventory.RemoveAsync(lot, request.LocationId, request.Quantity,
                InventoryTransactionType.ProcessIssue, userId, workOrderId: workOrderId,
                note: "部材投入", ct: ct);
        }
        catch (InventoryException ex)
        {
            return Outcome<MaterialConsumption>.Invalid(ex.Message);
        }

        var consumption = new MaterialConsumption
        {
            WorkOrderId = workOrderId,
            ProductId = lot.ProductId,
            Product = lot.Product,
            LotId = lot.Id,
            Lot = lot,
            LocationId = request.LocationId,
            Quantity = request.Quantity,
            Method = ConsumptionMethod.Manual,
            IsSubstitute = isSubstitute,
            SubstituteReason = isSubstitute ? request.SubstituteReason : null,
            RecordedByUserId = userId,
        };
        db.MaterialConsumptions.Add(consumption);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Execution", "Consumption", nameof(WorkOrder), workOrderId.ToString(),
            detail: new
            {
                lot = lot.LotNumber,
                quantity = request.Quantity,
                isSubstitute,
                substituteReason = isSubstitute ? request.SubstituteReason : null,
            }, ct: ct);
        return Outcome<MaterialConsumption>.Ok(consumption);
    }

    /// <summary>
    /// 実績入力（B-40-10-01 出来高、B-40-10-02 在庫計上、B-40-10-09 バックフラッシュ）。作業指示は完了状態になる（B-30-30-06）。
    /// 最終工程の実績では産出ロットへ在庫計上（outputLocationId必須）。backflush=trueでMBOM×(良品+不良)の部材を
    /// 先入れ先出しで自動消費。
    /// <para>
    /// <b>同じ作業指示に対して複数回呼べる（分割報告）。</b>1回の指示を数回に分けて報告する運用を想定しており、
    /// 完了状態でも受け付ける（拒否するのは承認済み・取消のみ）。呼ぶたびに実績が1件増え、
    /// 産出ロットの数量はその都度加算される。<b>backflushも呼ぶたびに走る</b>ため、
    /// 報告した数量ぶんの部材が都度消費される（同じ数量を二重に報告すれば部材も二重に減る）。
    /// </para>
    /// </summary>
    public async Task<Outcome<ProductionRecordResult>> AddProductionRecordAsync(
        int workOrderId, ProductionRecordRequest request, string userId, CancellationToken ct)
    {
        var workOrder = await db.WorkOrders
            .Include(w => w.ManufacturingOrder).ThenInclude(o => o!.OutputLot)
            .FirstOrDefaultAsync(w => w.Id == workOrderId, ct);
        if (workOrder is null)
        {
            return Outcome<ProductionRecordResult>.NotFound("作業指示が存在しません。");
        }
        if (workOrder.Status is WorkOrderStatus.Approved or WorkOrderStatus.Canceled)
        {
            return Outcome<ProductionRecordResult>.Conflict($"状態 '{workOrder.Status}' の作業指示には実績を記録できません。");
        }
        if (request.GoodQuantity + request.DefectQuantity <= 0)
        {
            return Outcome<ProductionRecordResult>.Invalid("良品数と不良数の合計は0より大きい必要があります。");
        }
        // 廃棄・再作業待ちは不良数の内訳（B-40-10-01）。残りは判定待ち・保留中の数量になる
        if (request.ScrapQuantity + request.ReworkQuantity > request.DefectQuantity)
        {
            return Outcome<ProductionRecordResult>.Invalid(
                $"廃棄数と再作業待ち数の合計（{request.ScrapQuantity + request.ReworkQuantity}）が" +
                $"不良数（{request.DefectQuantity}）を超えています。");
        }

        // 不良理由別の内訳（C-40-10-01）。合計は不良数を超えられない（残りは理由未分類）
        var defects = request.Defects ?? [];
        if (defects.Count > 0)
        {
            if (defects.Sum(d => d.Quantity) > request.DefectQuantity)
            {
                return Outcome<ProductionRecordResult>.Invalid(
                    $"不良理由別の内訳の合計（{defects.Sum(d => d.Quantity)}）が" +
                    $"不良数（{request.DefectQuantity}）を超えています。");
            }
            var reasonIds = defects.Select(d => d.DefectReasonId).ToList();
            if (reasonIds.Distinct().Count() != reasonIds.Count)
            {
                return Outcome<ProductionRecordResult>.Invalid("同じ不良理由が重複しています。");
            }
            var found = await db.DefectReasons.CountAsync(r => reasonIds.Contains(r.Id) && r.IsActive, ct);
            if (found != reasonIds.Count)
            {
                return Outcome<ProductionRecordResult>.Invalid("存在しない（または無効な）不良理由IDが含まれています。");
            }
        }

        var order = workOrder.ManufacturingOrder!;
        var isFinalStep = workOrder.RoutingSequence == await db.WorkOrders
            .Where(w => w.ManufacturingOrderId == order.Id && w.Status != WorkOrderStatus.Canceled)
            .MaxAsync(w => w.RoutingSequence, ct);

        Lot? outputLot = null;
        if (isFinalStep && request.GoodQuantity > 0)
        {
            if (request.OutputLocationId is null)
            {
                return Outcome<ProductionRecordResult>.Invalid("最終工程の実績には入庫先ロケーション（outputLocationId）が必要です。");
            }
            if (!await db.Locations.AnyAsync(l => l.Id == request.OutputLocationId && l.IsActive, ct))
            {
                return Outcome<ProductionRecordResult>.Invalid("存在しない（または無効な）入庫先ロケーションです。");
            }
            outputLot = order.OutputLot;
            if (outputLot is null)
            {
                return Outcome<ProductionRecordResult>.Conflict("産出ロットが未採番です（指図が正しく展開されていません）。");
            }
        }

        try
        {
            // バックフラッシュ（B-40-10-09）：MBOM×完了数量の部材を自動消費
            if (request.Backflush)
            {
                // 消費量の基準は展開時に固定した予定材料の原単位（Spec.md 5.7）
                var bom = await db.ManufacturingOrderMaterials
                    .Where(m => m.ManufacturingOrderId == workOrder.ManufacturingOrderId)
                    .ToListAsync(ct);
                if (bom.Count == 0)
                {
                    return Outcome<ProductionRecordResult>.Invalid("予定材料が未登録（展開時にMBOMが未登録）のためバックフラッシュできません。");
                }
                var totalProduced = request.GoodQuantity + request.DefectQuantity;
                foreach (var bomItem in bom)
                {
                    var required = bomItem.QuantityPer * totalProduced;
                    var allocations = await inventory.AllocateFefoAsync(bomItem.ChildProductId, required, ct);
                    foreach (var (lot, locationId, quantity) in allocations)
                    {
                        await inventory.RemoveAsync(lot, locationId, quantity,
                            InventoryTransactionType.ProcessIssue, userId, workOrderId: workOrderId,
                            note: "バックフラッシュ", ct: ct);
                        db.MaterialConsumptions.Add(new MaterialConsumption
                        {
                            WorkOrderId = workOrderId,
                            ProductId = bomItem.ChildProductId,
                            LotId = lot.Id,
                            LocationId = locationId,
                            Quantity = quantity,
                            Method = ConsumptionMethod.Backflush,
                            RecordedByUserId = userId,
                        });
                    }
                }
            }

            // 最終工程：産出ロットへの在庫計上（B-40-10-02）
            if (outputLot is not null)
            {
                outputLot.InitialQuantity += request.GoodQuantity;
                outputLot.SourceWorkOrderId = workOrderId;
                outputLot.ManufacturedOn = businessDate.Today;
                await inventory.AddAsync(outputLot, request.OutputLocationId!.Value, request.GoodQuantity,
                    InventoryTransactionType.PutAway, userId, workOrderId: workOrderId,
                    note: "完成品在庫計上", ct: ct);
            }
        }
        catch (InventoryException ex)
        {
            return Outcome<ProductionRecordResult>.Invalid(ex.Message);
        }

        // どの直の実績かを記録時に固定する（C-40-10-03 の直別集計。Spec.md 5.7）。
        // 集計のたびに時刻から引き直すと、直の時間帯定義を変えたときに過去の集計まで動く
        var shifts = await db.Shifts.AsNoTracking().Where(s => s.IsActive).ToListAsync(ct);
        var startedAtLocal = TimeOnly.FromDateTime(businessDate.ToFactoryTime(request.StartedAt).DateTime);
        var shift = ShiftSchedulePolicy.Resolve(shifts, startedAtLocal);

        var record = new ProductionRecord
        {
            WorkOrderId = workOrderId,
            PerformedByUserId = userId,
            ShiftId = shift?.Id,
            GoodQuantity = request.GoodQuantity,
            DefectQuantity = request.DefectQuantity,
            ScrapQuantity = request.ScrapQuantity,
            ReworkQuantity = request.ReworkQuantity,
            Defects = defects.Select(d => new ProductionDefect
            {
                DefectReasonId = d.DefectReasonId,
                Quantity = d.Quantity,
                Note = d.Note,
            }).ToList(),
            StartedAt = request.StartedAt,
            EndedAt = request.EndedAt,
            OutputLotId = outputLot?.Id,
            OutputLocationId = outputLot is not null ? request.OutputLocationId : null,
        };
        db.ProductionRecords.Add(record);
        workOrderStatus.ChangeStatus(workOrder, WorkOrderStatus.Completed,
            WorkOrderStatusChangeSource.ProductionRecord, userId);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Execution", "ProductionRecord", nameof(WorkOrder), workOrderId.ToString(),
            detail: new
            {
                good = request.GoodQuantity,
                defect = request.DefectQuantity,
                scrap = request.ScrapQuantity,
                rework = request.ReworkQuantity,
                backflush = request.Backflush,
            }, ct: ct);
        return Outcome<ProductionRecordResult>.Ok(new ProductionRecordResult(record, workOrder, outputLot, shift));
    }

    /// <summary>製造条件データ（B-30-30-04。手入力/CSV由来の値を記録）</summary>
    public async Task<Outcome<List<ProductionDataRecord>>> AddDataRecordsAsync(
        int workOrderId, List<DataRecordRequest> requests, string? userId, CancellationToken ct)
    {
        if (!await db.WorkOrders.AnyAsync(w => w.Id == workOrderId, ct))
        {
            return Outcome<List<ProductionDataRecord>>.NotFound("作業指示が存在しません。");
        }
        if (requests.Count == 0)
        {
            return Outcome<List<ProductionDataRecord>>.Invalid("記録する項目がありません。");
        }
        // 指示に紐づく記録は、展開時点のスナップショットと照合して逸脱を判定する（Spec.md 5.7）
        var instructionIds = requests
            .Where(r => r.WorkOrderControlItemId is not null)
            .Select(r => r.WorkOrderControlItemId!.Value)
            .Distinct()
            .ToList();
        var instructions = instructionIds.Count == 0
            ? []
            : await db.WorkOrderControlItems.AsNoTracking()
                .Where(i => i.WorkOrderId == workOrderId && instructionIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, ct);
        foreach (var request in requests.Where(r => r.WorkOrderControlItemId is not null))
        {
            if (!instructions.ContainsKey(request.WorkOrderControlItemId!.Value))
            {
                return Outcome<List<ProductionDataRecord>>.Invalid(
                    $"工程管理項目の指示（ID {request.WorkOrderControlItemId}）はこの作業指示のものではありません。");
            }
            if (request.NumericValue is null)
            {
                return Outcome<List<ProductionDataRecord>>.Invalid(
                    "工程管理項目の指示を指定した記録には、判定に使う数値（numericValue）が必要です。");
            }
        }

        var records = requests.Select(r =>
        {
            var instruction = r.WorkOrderControlItemId is { } instructionId
                ? instructions[instructionId]
                : null;
            return new ProductionDataRecord
            {
                WorkOrderId = workOrderId,
                WorkOrderControlItemId = r.WorkOrderControlItemId,
                Item = r.Item,
                Value = r.Value,
                NumericValue = r.NumericValue,
                IsDeviation = ControlItemDeviationPolicy.Judge(instruction, r.NumericValue),
                RecordedByUserId = userId,
            };
        }).ToList();
        db.ProductionDataRecords.AddRange(records);
        await db.SaveChangesAsync(ct);

        // 逸脱は後から「なぜ不良が出たか」を説明する根拠になるため、監査ログにも内容を残す
        var deviations = records
            .Where(r => r.IsDeviation == true)
            .Select(r => ControlItemDeviationPolicy.Describe(
                instructions[r.WorkOrderControlItemId!.Value], r.NumericValue!.Value))
            .ToList();
        await auditLogger.LogAsync("Execution", "DataRecord", nameof(WorkOrder), workOrderId.ToString(),
            detail: new
            {
                items = requests.Select(r => r.Item).ToList(),
                deviations,
            }, ct: ct);
        return Outcome<List<ProductionDataRecord>>.Ok(records);
    }
}
