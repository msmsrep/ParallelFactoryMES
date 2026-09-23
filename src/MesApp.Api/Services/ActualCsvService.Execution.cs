using MesApp.Api.Localization;
using System.Globalization;
using MesApp.Core.Contracts.Execution;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

// 実績CSVのうち作業指示の実行記録（B-20-50 / B-30-10 / B-30-20 / B-40-10 / B-30-30-04。WorkOrderExecutionService）
public sealed partial class ActualCsvService
{
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
                head.Fail(ApiText.T("チェックリスト '{0}' は登録されていません（ChecklistCode）。", code));
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
                    reader.Fail(ApiText.T("ItemSequence（項目の表示順）は必須です。"));
                }
                var item = checklist.Items.FirstOrDefault(i => i.Sequence == itemSequence);
                if (itemSequence is not null && item is null)
                {
                    reader.Fail(ApiText.T("チェックリスト '{0}' に表示順 {1} の項目はありません。", code, itemSequence));
                }
                else if (item is not null && results.Any(r => r.ChecklistItemId == item.Id))
                {
                    reader.Fail(ApiText.T("チェックリスト '{0}' の表示順 {1} が重複しています。", code, itemSequence));
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
                reader.Fail(ApiText.T("Quantity（投入数量）は必須です。"));
            }
            // 前のファイル（受入・工程展開）や前の行で作られたロットも指せるよう、行ごとにDBを引く
            int? lotId = null;
            if (lotNumber.Length > 0)
            {
                lotId = await db.Lots.Where(l => l.LotNumber == lotNumber)
                    .Select(l => (int?)l.Id).FirstOrDefaultAsync(ct);
                if (lotId is null)
                {
                    reader.Fail(ApiText.T("ロット '{0}' は登録されていません（LotNumber）。", lotNumber));
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
                    reader.Fail(ApiText.T("工程管理項目 '{0}' はこの作業指示に展開されていません（ControlItemCode）。", controlItemCode));
                    continue;
                }
                if (numericValue is null)
                {
                    reader.Fail(ApiText.T("ControlItemCode を指定した行には NumericValue（数値）が必要です。"));
                    continue;
                }
                instructionId = instruction.Id;
                item ??= instruction.ItemName;
                value ??= $"{numericValue}{instruction.Unit}";
            }
            if (item is null || value is null)
            {
                reader.Fail(ApiText.T("ControlItemCode を省略した行には Item（項目）と Value（値）が必要です。"));
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
                reader.Fail(ApiText.T("Defects は 不良理由コード=数量 をセミコロンで区切って指定してください（'{0}'）。", part));
                continue;
            }
            if (!reasonIds.TryGetValue(pair[0], out var reasonId))
            {
                reader.Fail(ApiText.T("不良理由 '{0}' は登録されていません（Defects）。", pair[0]));
                continue;
            }
            result.Add(new ProductionDefectRequest(reasonId, quantity));
        }
        return result;
    }
}
