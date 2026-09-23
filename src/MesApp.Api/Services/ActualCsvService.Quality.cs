using MesApp.Api.Localization;
using MesApp.Core.Contracts.Execution;
using MesApp.Core.Contracts.Maintenance;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Contracts.Quality;
using MesApp.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

// 実績CSVのうち検査・作業時間・トラブル・設備稼働記録（C-20 / B-30-30-02 / B-60-10 / B-40-20）
public sealed partial class ActualCsvService
{
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
                head.Fail(ApiText.T("工程内検査には OrderNo（指図番号）と Sequence（工程順序）が必要です。"));
            }
            int? lotId = null;
            if (type != InspectionOrderType.InProcess)
            {
                if (lotNumber is null)
                {
                    head.Fail(ApiText.T("工程内検査以外は LotNumber（対象ロット番号）が必要です。"));
                }
                else
                {
                    // 受入・生産実績の取込で前のファイルが作ったロットも指せるよう、DBを引く
                    lotId = await db.Lots.Where(l => l.LotNumber == lotNumber)
                        .Select(l => (int?)l.Id).FirstOrDefaultAsync(ct);
                    if (lotId is null)
                    {
                        head.Fail(ApiText.T("ロット '{0}' は登録されていません（LotNumber）。", lotNumber));
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

    /// <summary>設備稼働記録。1行＝1区間（記録者は取り込んだユーザー）</summary>
    private async Task<int> ImportEquipmentLogsAsync(
        CsvTable table, List<CsvImportError> errors, string? userId, CancellationToken ct)
    {
        var workOrders = await WorkOrderKeysAsync(ct);
        var equipmentIds = await db.Equipments.AsNoTracking()
            .ToDictionaryAsync(e => e.AssetNo, e => e.Id, StringComparer.Ordinal, ct);
        var created = 0;
        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            reader.RequiredText("EquipmentAssetNo");
            var equipmentId = reader.Reference("EquipmentAssetNo", null, equipmentIds, "設備");
            reader.RequiredText("Status");
            var status = reader.Enum("Status", EquipmentLogStatus.Running, CsvEnumLabels.EquipmentLogStatuses);
            var startedAt = RequiredDateTime(reader, "StartedAt");
            var endedAt = reader.DateTimeOrNull("EndedAt", FactoryOffset);
            var stopCause = reader.Text("StopCause", null, 500);
            var workOrderId = ResolveOptionalWorkOrder(reader, workOrders);
            var note = reader.Text("Note", null, 500);
            if (reader.Failed)
            {
                continue;
            }

            var outcome = await reports.AddEquipmentLogAsync(
                new EquipmentLogRequest(equipmentId!.Value, status, startedAt!.Value, endedAt, stopCause, note, workOrderId),
                userId, ct);
            if (outcome.Failed)
            {
                FailRow(reader, outcome.Error!);
                continue;
            }
            created++;
        }
        return created;
    }
}
