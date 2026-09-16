using System.Globalization;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Execution;
using MesApp.Core.Contracts.Inventory;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Contracts.Production;
using MesApp.Core.Contracts.Quality;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// 実績（取引データ）のCSV一括取込（Spec.md 3.8）。
/// <para>
/// 行ごとに<b>単票APIと同じ業務サービスを呼ぶ</b>（受入なら <see cref="ReceivingService"/>）。
/// 実績は在庫・ロット・状態履歴を連鎖して動かすため、マスタCSVのように行をエンティティへ
/// 直接写すと、判定や監査ログの付け忘れがそのまま迂回経路になる。
/// </para>
/// <para>
/// 全行を1つのトランザクションで処理し、<b>1行でもエラーがあれば全件ロールバック</b>する（ZIPの一括取込では全ファイルで1つ）。
/// 行は保存しながら進めるため、後の行は前の行の結果（採番済みロット等）を前提にできる。
/// 検証のみ（dryRun）も実際に登録してからロールバックする——ロット番号の重複のように
/// 前の行を登録しないと判定できない条件があるため。
/// </para>
/// </summary>
public sealed class ActualCsvService(
    MesAppDbContext db,
    ReceivingService receiving,
    ManufacturingOrderService orders,
    WorkOrderExecutionService execution,
    InspectionService inspections,
    ShopFloorReportService reports,
    IBusinessDateService businessDate,
    IAuditLogger auditLogger)
{
    public async Task<CsvImportResult> ImportAsync(
        ActualCsvKind kind, string csvText, bool dryRun, string? userId, CancellationToken ct)
    {
        var bundle = await ImportBundleAsync(
            [new CsvBundleFile<ActualCsvKind>(kind.Info.Kind, kind, csvText)], dryRun, userId, ct);
        return bundle.Files[0].Result;
    }

    /// <summary>
    /// 複数ファイルの一括取込。全ファイルを1つのトランザクションで順に取り込み、
    /// どれか1つでもエラーがあれば全ファイルを取り消す（<see cref="CsvBundle"/>）
    /// </summary>
    public Task<CsvBundleImportResult> ImportBundleAsync(
        IReadOnlyList<CsvBundleFile<ActualCsvKind>> files, bool dryRun, string? userId, CancellationToken ct) =>
        CsvBundle.ImportAsync(db, files, kind => kind.Info, dryRun,
            (kind, table, errors, token) => ImportOneAsync(kind, table, errors, userId, token), ct);

    /// <summary>1ファイル分を取り込み、エラーが無ければ監査ログを残す（行は保存しながら進む。トランザクションは呼び出し側）</summary>
    private async Task<CsvFileImportCount> ImportOneAsync(
        ActualCsvKind kind, CsvTable table, List<CsvImportError> errors, string? userId, CancellationToken ct)
    {
        var created = kind.Info.Kind switch
        {
            ActualCsvKinds.Receiving => await ImportReceivingAsync(table, errors, userId, ct),
            ActualCsvKinds.ManufacturingOrders => await ImportManufacturingOrdersAsync(table, errors, userId, ct),
            ActualCsvKinds.SetupRecords => await ImportSetupRecordsAsync(table, errors, userId!, ct),
            ActualCsvKinds.ChecklistRecords => await ImportChecklistRecordsAsync(table, errors, userId!, ct),
            ActualCsvKinds.Consumptions => await ImportConsumptionsAsync(table, errors, userId, ct),
            ActualCsvKinds.ProductionRecords => await ImportProductionRecordsAsync(table, errors, userId!, ct),
            ActualCsvKinds.DataRecords => await ImportDataRecordsAsync(table, errors, userId, ct),
            ActualCsvKinds.Inspections => await ImportInspectionsAsync(table, errors, userId!, ct),
            ActualCsvKinds.WorkTimeRecords => await ImportWorkTimeRecordsAsync(table, errors, userId!, ct),
            ActualCsvKinds.TroubleReports => await ImportTroubleReportsAsync(table, errors, userId!, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        if (errors.Count == 0)
        {
            await auditLogger.LogAsync("Actual", "CsvImport", kind.Info.Kind, null,
                detail: $"rows={table.Rows.Count}, created={created}", ct: ct);
        }
        return new CsvFileImportCount(created, 0);
    }

    /// <summary>受入（D-10-10-02）。1行＝1ロットの受入登録</summary>
    private async Task<int> ImportReceivingAsync(
        CsvTable table, List<CsvImportError> errors, string? userId, CancellationToken ct)
    {
        // 無効なマスタは候補に入れない（単票APIも無効な品目・ロケーションへの受入を拒否する）
        var productIds = await db.Products.AsNoTracking().Where(p => p.IsActive)
            .ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.Ordinal, ct);
        var locationIds = await db.Locations.AsNoTracking().Where(l => l.IsActive)
            .ToDictionaryAsync(l => l.Code, l => l.Id, StringComparer.Ordinal, ct);

        var created = 0;
        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            reader.RequiredText("ProductCode");
            var productId = reader.Reference("ProductCode", null, productIds, "有効な品目");
            var quantity = reader.NumberOrNull("Quantity", null, 0.000001m);
            reader.RequiredText("LocationCode");
            var locationId = reader.Reference("LocationCode", null, locationIds, "有効なロケーション");
            var lotNumber = reader.Text("LotNumber", null, 60);
            var expiresOn = reader.DateOrNull("ExpiresOn", null);
            var note = reader.Text("Note", null, 500);
            if (quantity is null && !reader.Failed)
            {
                reader.Fail("Quantity（受入数量）は必須です。");
            }
            if (reader.Failed)
            {
                continue;
            }

            var outcome = await receiving.ReceiveAsync(
                new ReceivingRequest(productId!.Value, quantity!.Value, locationId!.Value, lotNumber, expiresOn, note),
                userId, ct);
            if (outcome.Error is not null)
            {
                FailRow(reader, outcome.Error);
                continue;
            }
            created++;
        }
        return created;
    }

    /// <summary>
    /// 製造指図（A-20-10-01）。1行＝1指図で、列の指定に応じて承認（A-20-20-01）・工程展開（B-10-10-01）まで進める。
    /// 後続の実績CSVは作業指示を「指図番号＋工程順序」で指すため、指図番号はここで確定させる
    /// </summary>
    private async Task<int> ImportManufacturingOrdersAsync(
        CsvTable table, List<CsvImportError> errors, string? userId, CancellationToken ct)
    {
        var productIds = await db.Products.AsNoTracking().Where(p => p.IsActive)
            .ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.Ordinal, ct);

        var created = 0;
        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            // 作業指示番号は「指図番号-工程順序2桁」になるため、その分を残す
            var orderNo = reader.Text("OrderNo", null, 46);
            reader.RequiredText("ProductCode");
            var productId = reader.Reference("ProductCode", null, productIds, "有効な品目");
            var quantity = reader.NumberOrNull("Quantity", null, 0.000001m);
            var dueDate = reader.DateOrNull("DueDate", null);
            var orderType = reader.Enum("OrderType", ManufacturingOrderType.Normal, CsvEnumLabels.OrderTypes);
            var sourceOrderNo = reader.Text("SourceOrderNo", null);
            var note = reader.Text("Note", null, 1000);
            var approve = reader.Bool("Approve", false);
            var expand = reader.Bool("Expand", false);
            var outputLotNumber = reader.Text("OutputLotNumber", null, 60);
            if (quantity is null && !reader.Failed)
            {
                reader.Fail("Quantity（指図数量）は必須です。");
            }
            if (expand && !approve)
            {
                reader.Fail("工程展開は承認済みの指図だけに行えます。Expand を true にするときは Approve も true にしてください。");
            }
            if (outputLotNumber is not null && !expand)
            {
                reader.Fail("OutputLotNumber（産出ロット番号）は工程展開するとき（Expand=true）だけ指定できます。");
            }

            int? sourceOrderId = null;
            if (sourceOrderNo is not null)
            {
                // 前の行で登録した指図も保存済みなので、DBを引けば同じファイル内の指図を指せる
                sourceOrderId = await db.ManufacturingOrders.Where(o => o.OrderNo == sourceOrderNo)
                    .Select(o => (int?)o.Id).FirstOrDefaultAsync(ct);
                if (sourceOrderId is null)
                {
                    reader.Fail($"元指図 '{sourceOrderNo}' は登録されていません（SourceOrderNo）。");
                }
            }
            if (reader.Failed)
            {
                continue;
            }

            var outcome = await orders.CreateAsync(
                new CreateManufacturingOrderRequest(productId!.Value, quantity!.Value, dueDate, orderType, sourceOrderId, note),
                orderNo, userId, ct);
            if (outcome.Order is { } order && approve)
            {
                outcome = await orders.ApproveAsync(order, userId, ct);
                if (outcome.Order is not null && expand)
                {
                    outcome = await orders.ExpandAsync(order, outputLotNumber, ct);
                }
            }
            if (outcome.Error is not null)
            {
                FailRow(reader, outcome.Error);
                continue;
            }
            created++;
        }
        return created;
    }

    // ---- 作業指示の実行記録（B-20-50 / B-30-10 / B-30-20 / B-40-10 / B-30-30-04）----

    /// <summary>段取り実績。1行＝1記録</summary>
    private async Task<int> ImportSetupRecordsAsync(
        CsvTable table, List<CsvImportError> errors, string userId, CancellationToken ct)
    {
        var workOrders = await WorkOrderKeysAsync(ct);
        var created = 0;
        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            var workOrderId = ResolveWorkOrder(reader, workOrders);
            reader.RequiredText("Type");
            var type = reader.Enum("Type", SetupType.Pre, CsvEnumLabels.SetupTypes);
            var startedAt = RequiredDateTime(reader, "StartedAt");
            var endedAt = reader.DateTimeOrNull("EndedAt", FactoryOffset);
            var note = reader.Text("AbnormalityNote", null, 1000);
            if (reader.Failed)
            {
                continue;
            }

            var outcome = await execution.AddSetupRecordAsync(
                workOrderId!.Value, new SetupRecordRequest(type, startedAt!.Value, endedAt, note), userId, ct);
            if (outcome.Failed)
            {
                FailRow(reader, outcome.Error!);
                continue;
            }
            created++;
        }
        return created;
    }

    /// <summary>
    /// チェックリスト実施。指図番号・工程順序・チェックリストコードが同じ行を1回の実施としてまとめる
    /// （マスタCSVのチェックリストと同じく、1行＝1項目）
    /// </summary>
    private async Task<int> ImportChecklistRecordsAsync(
        CsvTable table, List<CsvImportError> errors, string userId, CancellationToken ct)
    {
        var workOrders = await WorkOrderKeysAsync(ct);
        var checklists = await db.Checklists.AsNoTracking().Include(c => c.Items)
            .ToDictionaryAsync(c => c.Code, StringComparer.Ordinal, ct);

        var groups = table.Rows
            .GroupBy(row => (table.Value(row, "OrderNo"), table.Value(row, "Sequence"), table.Value(row, "ChecklistCode")))
            .Select(g => g.ToList())
            .ToList();

        var created = 0;
        foreach (var group in groups)
        {
            var head = new CsvRowReader(table, group[0], errors);
            var workOrderId = ResolveWorkOrder(head, workOrders);
            var code = head.RequiredText("ChecklistCode");
            if (head.Failed)
            {
                continue;
            }
            if (!checklists.TryGetValue(code, out var checklist))
            {
                head.Fail($"チェックリスト '{code}' は登録されていません（ChecklistCode）。");
                continue;
            }

            var results = new List<ChecklistResultRequest>();
            var failed = false;
            foreach (var row in group)
            {
                var reader = new CsvRowReader(table, row, errors);
                var itemSequence = reader.IntOrNull("ItemSequence", null, 1);
                var isChecked = reader.Bool("IsChecked", true);
                var note = reader.Text("Note", null, 500);
                if (itemSequence is null && !reader.Failed)
                {
                    reader.Fail("ItemSequence（項目の表示順）は必須です。");
                }
                var item = checklist.Items.FirstOrDefault(i => i.Sequence == itemSequence);
                if (itemSequence is not null && item is null)
                {
                    reader.Fail($"チェックリスト '{code}' に表示順 {itemSequence} の項目はありません。");
                }
                else if (item is not null && results.Any(r => r.ChecklistItemId == item.Id))
                {
                    reader.Fail($"チェックリスト '{code}' の表示順 {itemSequence} が重複しています。");
                }
                if (reader.Failed)
                {
                    failed = true;
                    continue;
                }
                results.Add(new ChecklistResultRequest(item!.Id, isChecked, note));
            }
            if (failed)
            {
                continue;
            }

            var outcome = await execution.AddChecklistRecordAsync(
                workOrderId!.Value, new ChecklistRecordRequest(checklist.Id, results), userId, ct);
            if (outcome.Failed)
            {
                FailRow(head, outcome.Error!);
                continue;
            }
            created++;
        }
        return created;
    }

    /// <summary>部材投入。1行＝1ロットの投入（在庫の払出と同時）</summary>
    private async Task<int> ImportConsumptionsAsync(
        CsvTable table, List<CsvImportError> errors, string? userId, CancellationToken ct)
    {
        var workOrders = await WorkOrderKeysAsync(ct);
        var locationIds = await db.Locations.AsNoTracking()
            .ToDictionaryAsync(l => l.Code, l => l.Id, StringComparer.Ordinal, ct);

        var created = 0;
        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            var workOrderId = ResolveWorkOrder(reader, workOrders);
            var lotNumber = reader.RequiredText("LotNumber");
            reader.RequiredText("LocationCode");
            var locationId = reader.Reference("LocationCode", null, locationIds, "ロケーション");
            var quantity = reader.NumberOrNull("Quantity", null, 0.000001m);
            var substituteReason = reader.Text("SubstituteReason", null, 500);
            if (quantity is null && !reader.Failed)
            {
                reader.Fail("Quantity（投入数量）は必須です。");
            }
            // 前のファイル（受入・工程展開）や前の行で作られたロットも指せるよう、行ごとにDBを引く
            int? lotId = null;
            if (lotNumber.Length > 0)
            {
                lotId = await db.Lots.Where(l => l.LotNumber == lotNumber)
                    .Select(l => (int?)l.Id).FirstOrDefaultAsync(ct);
                if (lotId is null)
                {
                    reader.Fail($"ロット '{lotNumber}' は登録されていません（LotNumber）。");
                }
            }
            if (reader.Failed)
            {
                continue;
            }

            var outcome = await execution.AddConsumptionAsync(workOrderId!.Value,
                new ConsumptionRequest(lotId!.Value, locationId!.Value, quantity!.Value, substituteReason), userId, ct);
            if (outcome.Failed)
            {
                FailRow(reader, outcome.Error!);
                continue;
            }
            created++;
        }
        return created;
    }

    /// <summary>生産実績。1行＝1回の実績報告（分割報告は行を分ける）</summary>
    private async Task<int> ImportProductionRecordsAsync(
        CsvTable table, List<CsvImportError> errors, string userId, CancellationToken ct)
    {
        var workOrders = await WorkOrderKeysAsync(ct);
        var locationIds = await db.Locations.AsNoTracking()
            .ToDictionaryAsync(l => l.Code, l => l.Id, StringComparer.Ordinal, ct);
        var defectReasonIds = await db.DefectReasons.AsNoTracking()
            .ToDictionaryAsync(r => r.Code, r => r.Id, StringComparer.Ordinal, ct);

        var created = 0;
        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            var workOrderId = ResolveWorkOrder(reader, workOrders);
            reader.RequiredText("GoodQuantity");
            var good = reader.Number("GoodQuantity", 0m, 0);
            var defect = reader.Number("DefectQuantity", 0m, 0);
            var scrap = reader.Number("ScrapQuantity", 0m, 0);
            var rework = reader.Number("ReworkQuantity", 0m, 0);
            var startedAt = RequiredDateTime(reader, "StartedAt");
            var endedAt = reader.DateTimeOrNull("EndedAt", FactoryOffset);
            var locationId = reader.Reference("OutputLocationCode", null, locationIds, "入庫先ロケーション");
            var backflush = reader.Bool("Backflush", false);
            var defects = ParseDefects(reader, table.Value(row, "Defects"), defectReasonIds);
            if (reader.Failed)
            {
                continue;
            }

            var outcome = await execution.AddProductionRecordAsync(workOrderId!.Value,
                new ProductionRecordRequest(good, defect, startedAt!.Value, endedAt, locationId, backflush,
                    scrap, rework, defects), userId, ct);
            if (outcome.Failed)
            {
                FailRow(reader, outcome.Error!);
                continue;
            }
            created++;
        }
        return created;
    }

    /// <summary>製造条件データ。1行＝1項目の記録</summary>
    private async Task<int> ImportDataRecordsAsync(
        CsvTable table, List<CsvImportError> errors, string? userId, CancellationToken ct)
    {
        var workOrders = await WorkOrderKeysAsync(ct);

        var created = 0;
        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            var workOrderId = ResolveWorkOrder(reader, workOrders);
            var controlItemCode = reader.Text("ControlItemCode", null);
            var numericValue = reader.NumberOrNull("NumericValue", null);
            var item = reader.Text("Item", null, 100);
            var value = reader.Text("Value", null, 500);
            if (reader.Failed)
            {
                continue;
            }

            int? instructionId = null;
            if (controlItemCode is not null)
            {
                // 展開時に作業指示へ写した指示（スナップショット）を引く。マスタの現在値では判定しない（Spec.md 5.7）
                var instruction = await db.WorkOrderControlItems.AsNoTracking()
                    .FirstOrDefaultAsync(i => i.WorkOrderId == workOrderId && i.ItemCode == controlItemCode, ct);
                if (instruction is null)
                {
                    reader.Fail($"工程管理項目 '{controlItemCode}' はこの作業指示に展開されていません（ControlItemCode）。");
                    continue;
                }
                if (numericValue is null)
                {
                    reader.Fail("ControlItemCode を指定した行には NumericValue（数値）が必要です。");
                    continue;
                }
                instructionId = instruction.Id;
                item ??= instruction.ItemName;
                value ??= $"{numericValue}{instruction.Unit}";
            }
            if (item is null || value is null)
            {
                reader.Fail("ControlItemCode を省略した行には Item（項目）と Value（値）が必要です。");
                continue;
            }

            var outcome = await execution.AddDataRecordsAsync(workOrderId!.Value,
                [new DataRecordRequest(item, value, instructionId, numericValue)], userId, ct);
            if (outcome.Failed)
            {
                FailRow(reader, outcome.Error!);
                continue;
            }
            created++;
        }
        return created;
    }

    // ---- 検査・作業時間・トラブル（C-20 / B-30-30-02 / B-60-10）----

    /// <summary>
    /// 検査。InspectionKey が同じ行を1件の検査指示にまとめ、指示の発行（C-20-10-02）→実績の登録（C-20-10-03）
    /// →（Judge=true なら）総合判定（C-20-10-04）まで進める。1行＝1項目×1サンプルの実績
    /// </summary>
    private async Task<int> ImportInspectionsAsync(
        CsvTable table, List<CsvImportError> errors, string userId, CancellationToken ct)
    {
        var workOrders = await WorkOrderKeysAsync(ct);
        var itemIds = await db.InspectionItems.AsNoTracking()
            .ToDictionaryAsync(i => i.Code, i => i.Id, StringComparer.Ordinal, ct);
        var deviceIds = await db.InspectionDevices.AsNoTracking()
            .ToDictionaryAsync(d => d.Code, d => d.Id, StringComparer.Ordinal, ct);

        var created = 0;
        foreach (var group in table.Rows.GroupBy(row => table.Value(row, "InspectionKey")).Select(g => g.ToList()))
        {
            var head = new CsvRowReader(table, group[0], errors);
            head.RequiredText("InspectionKey");
            head.RequiredText("Type");
            var type = head.Enum("Type", InspectionOrderType.Receiving, CsvEnumLabels.InspectionOrderTypes);
            var lotNumber = head.Text("LotNumber", null);
            var workOrderId = type == InspectionOrderType.InProcess ? ResolveOptionalWorkOrder(head, workOrders) : null;
            var judge = head.Bool("Judge", false);
            var grade = head.Text("Grade", null, 50);
            var note = head.Text("Note", null, 1000);
            if (type == InspectionOrderType.InProcess && workOrderId is null && !head.Failed)
            {
                head.Fail("工程内検査には OrderNo（指図番号）と Sequence（工程順序）が必要です。");
            }
            int? lotId = null;
            if (type != InspectionOrderType.InProcess)
            {
                if (lotNumber is null)
                {
                    head.Fail("工程内検査以外は LotNumber（対象ロット番号）が必要です。");
                }
                else
                {
                    // 受入・生産実績の取込で前のファイルが作ったロットも指せるよう、DBを引く
                    lotId = await db.Lots.Where(l => l.LotNumber == lotNumber)
                        .Select(l => (int?)l.Id).FirstOrDefaultAsync(ct);
                    if (lotId is null)
                    {
                        head.Fail($"ロット '{lotNumber}' は登録されていません（LotNumber）。");
                    }
                }
            }

            var results = new List<InspectionResultRequest>();
            var failed = head.Failed;
            foreach (var row in group)
            {
                var reader = row == group[0] ? head : new CsvRowReader(table, row, errors);
                reader.RequiredText("ItemCode");
                var itemId = reader.Reference("ItemCode", null, itemIds, "検査項目");
                var sampleNo = reader.IntOrNull("SampleNo", null, 1);
                var measured = reader.NumberOrNull("MeasuredValue", null);
                var text = reader.Text("TextValue", null, 500);
                InspectionJudgment? judgment = table.Value(row, "Judgment") is null
                    ? null
                    : reader.Enum("Judgment", InspectionJudgment.Pass, CsvEnumLabels.InspectionJudgments);
                var deviceId = reader.Reference("DeviceCode", null, deviceIds, "検査機");
                if (reader.Failed)
                {
                    failed = true;
                    continue;
                }
                results.Add(new InspectionResultRequest(itemId!.Value, sampleNo, measured, text, judgment, deviceId));
            }
            if (failed)
            {
                continue;
            }

            var outcome = await inspections.CreateAsync(new InspectionOrderCreateRequest(
                type, lotId, workOrderId, [.. results.Select(r => r.InspectionItemId).Distinct()], note), userId, ct);
            if (outcome.Value is { } order)
            {
                outcome = await inspections.AddResultsAsync(order.Id, results, userId, ct);
                if (!outcome.Failed && judge)
                {
                    outcome = await inspections.JudgeAsync(order.Id, grade, userId, ct);
                }
            }
            if (outcome.Failed)
            {
                FailRow(head, outcome.Error!);
                continue;
            }
            created++;
        }
        return created;
    }

    /// <summary>作業時間。1行＝1記録（記録者は取り込んだユーザー）</summary>
    private async Task<int> ImportWorkTimeRecordsAsync(
        CsvTable table, List<CsvImportError> errors, string userId, CancellationToken ct)
    {
        var workOrders = await WorkOrderKeysAsync(ct);
        var created = 0;
        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            reader.RequiredText("Type");
            var type = reader.Enum("Type", WorkTimeType.Direct, CsvEnumLabels.WorkTimeTypes);
            var category = reader.Text("IndirectCategory", null, 100);
            var workOrderId = ResolveOptionalWorkOrder(reader, workOrders);
            var startedAt = RequiredDateTime(reader, "StartedAt");
            var endedAt = reader.DateTimeOrNull("EndedAt", FactoryOffset);
            var note = reader.Text("Note", null, 500);
            if (reader.Failed)
            {
                continue;
            }

            var outcome = await reports.AddWorkTimeAsync(
                new WorkTimeRequest(type, category, workOrderId, startedAt!.Value, endedAt, note), userId, ct);
            if (outcome.Failed)
            {
                FailRow(reader, outcome.Error!);
                continue;
            }
            created++;
        }
        return created;
    }

    /// <summary>製造トラブル報告。1行＝1報告（報告者は取り込んだユーザー）</summary>
    private async Task<int> ImportTroubleReportsAsync(
        CsvTable table, List<CsvImportError> errors, string userId, CancellationToken ct)
    {
        var workOrders = await WorkOrderKeysAsync(ct);
        var equipmentIds = await db.Equipments.AsNoTracking()
            .ToDictionaryAsync(e => e.AssetNo, e => e.Id, StringComparer.Ordinal, ct);
        var created = 0;
        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            var occurredAt = RequiredDateTime(reader, "OccurredAt");
            reader.RequiredText("Category");
            var category = reader.Enum("Category", TroubleCategory.Quality, CsvEnumLabels.TroubleCategories);
            var workOrderId = ResolveOptionalWorkOrder(reader, workOrders);
            var equipmentId = reader.Reference("EquipmentAssetNo", null, equipmentIds, "設備");
            var content = reader.RequiredText("Content", 2000);
            if (reader.Failed)
            {
                continue;
            }

            var outcome = await reports.AddTroubleReportAsync(
                new TroubleReportRequest(occurredAt!.Value, category, workOrderId, equipmentId, content), userId, ct);
            if (outcome.Failed)
            {
                FailRow(reader, outcome.Error!);
                continue;
            }
            created++;
        }
        return created;
    }

    /// <summary>OrderNo を書いた行だけ作業指示を解決する（空欄なら紐づけない）</summary>
    private static int? ResolveOptionalWorkOrder(
        CsvRowReader reader, Dictionary<(string OrderNo, int Sequence), int> workOrders) =>
        reader.Text("OrderNo", null) is null ? null : ResolveWorkOrder(reader, workOrders);

    // ---- 共通 ----

    /// <summary>オフセットの無い日時を工場の時刻として読むためのオフセット</summary>
    private TimeSpan FactoryOffset => businessDate.ToFactoryTime(DateTimeOffset.UtcNow).Offset;

    /// <summary>
    /// 業務サービスが失敗を返した行をエラーにし、未保存の変更を捨てる。
    /// バックフラッシュの途中で在庫不足になった場合などに払出が追跡中に残り、
    /// 次の行の保存に混ざるのを防ぐ（成功した行は保存済みなので捨てても失われない）
    /// </summary>
    private void FailRow(CsvRowReader reader, string error)
    {
        reader.Fail(error);
        db.ChangeTracker.Clear();
    }

    /// <summary>「指図番号＋工程順序」→作業指示ID（取消済みの作業指示は含めない）</summary>
    private async Task<Dictionary<(string OrderNo, int Sequence), int>> WorkOrderKeysAsync(CancellationToken ct) =>
        (await db.WorkOrders.AsNoTracking()
            .Where(w => w.Status != WorkOrderStatus.Canceled)
            .Select(w => new { w.ManufacturingOrder!.OrderNo, w.RoutingSequence, w.Id })
            .ToListAsync(ct))
        .ToDictionary(x => (x.OrderNo, x.RoutingSequence), x => x.Id);

    private static int? ResolveWorkOrder(CsvRowReader reader, Dictionary<(string OrderNo, int Sequence), int> workOrders)
    {
        var orderNo = reader.RequiredText("OrderNo");
        var sequence = reader.IntOrNull("Sequence", null, 1);
        if (sequence is null && !reader.Failed)
        {
            reader.Fail("Sequence（工程順序）は必須です。");
        }
        if (reader.Failed)
        {
            return null;
        }
        if (workOrders.TryGetValue((orderNo, sequence!.Value), out var id))
        {
            return id;
        }
        reader.Fail($"指図 '{orderNo}' の工程順序 {sequence} の作業指示はありません（展開済みか、工程順序を確認してください）。");
        return null;
    }

    private DateTimeOffset? RequiredDateTime(CsvRowReader reader, string column)
    {
        var text = reader.RequiredText(column);
        return text.Length == 0 ? null : reader.DateTimeOrNull(column, FactoryOffset);
    }

    /// <summary>不良理由別の内訳（DR-02=2;DR-03=1）を読む</summary>
    private static List<ProductionDefectRequest>? ParseDefects(
        CsvRowReader reader, string? raw, IReadOnlyDictionary<string, int> reasonIds)
    {
        if (raw is null)
        {
            return null;
        }
        var result = new List<ProductionDefectRequest>();
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2
                || !decimal.TryParse(pair[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity)
                || quantity < 0)
            {
                reader.Fail($"Defects は 不良理由コード=数量 をセミコロンで区切って指定してください（'{part}'）。");
                continue;
            }
            if (!reasonIds.TryGetValue(pair[0], out var reasonId))
            {
                reader.Fail($"不良理由 '{pair[0]}' は登録されていません（Defects）。");
                continue;
            }
            result.Add(new ProductionDefectRequest(reasonId, quantity));
        }
        return result;
    }
}
