using MesApp.Api.Localization;
using MesApp.Core.Contracts.Execution;
using MesApp.Core.Contracts.Inventory;
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

    /// <summary>
    /// 不適合（B-40-30-01〜02 記録、C-30-20 対応指示・実績・承認）。1行＝1件の不適合で、
    /// 不適合を ReportNo（未登録なら新規起票。起票は番号を手入力し NC〜 は拒否）か、
    /// ReportNo を空欄にして LotNumber（そのロットの未完了の不適合が1件だけのとき）で指し、
    /// Action→ActionRecord→Approve の列があればその順に進める。
    /// 検査の不合格で自動起票された不適合は番号が取り込むまで分からないので、ロット番号で指す
    /// </summary>
    private async Task<int> ImportNonconformancesAsync(
        CsvTable table, List<CsvImportError> errors, string? userId, CancellationToken ct)
    {
        var workOrders = await WorkOrderKeysAsync(ct);
        // 対応指示・承認は品質管理の権限（記録と対応実績は誰でも。単票APIと同じ）
        var canManage = await IsInGroupAsync(userId, MesRoleGroups.QualityManage, ct);

        var created = 0;
        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            var reportNo = reader.Text("ReportNo", null, 50);
            var lotNumber = reader.Text("LotNumber", null);
            var source = reader.Enum("Source", NonconformanceSource.Production, CsvEnumLabels.NonconformanceSources);
            var content = reader.Text("Content", null, 2000);
            var workOrderId = ResolveOptionalWorkOrder(reader, workOrders);
            var causeCategory = reader.Text("CauseCategory", null, 100);
            var causeDetail = reader.Text("CauseDetail", null, 2000);
            var action = reader.Text("Action", null) is null
                ? (NonconformanceAction?)null
                : reader.Enum("Action", NonconformanceAction.Hold, CsvEnumLabels.NonconformanceActions);
            var instruction = reader.Text("ActionInstruction", null, 2000);
            var actionRecord = reader.Text("ActionRecord", null, 2000);
            var approve = reader.Bool("Approve", false);
            if ((action is not null || approve) && !canManage && !reader.Failed)
            {
                reader.Fail(ApiText.T("不適合の対応指示・承認は品質管理の担当者だけが登録できます。"));
            }
            if (reportNo is null && lotNumber is null && !reader.Failed)
            {
                reader.Fail(ApiText.T("ReportNo（不適合番号）か LotNumber（ロット番号）のどちらかが必要です。"));
            }
            var lotId = lotNumber is null || reader.Failed ? null : await LotIdAsync(reader, lotNumber, "LotNumber", ct);
            if (reader.Failed)
            {
                continue;
            }

            int? reportId;
            if (reportNo is not null)
            {
                reportId = await db.NonconformanceReports.Where(n => n.ReportNo == reportNo)
                    .Select(n => (int?)n.Id).FirstOrDefaultAsync(ct);
                if (reportId is null)
                {
                    if (content is null)
                    {
                        reader.Fail(ApiText.T("不適合を起票する行には Content（内容）が必要です。"));
                        continue;
                    }
                    var createdReport = await nonconformances.CreateAsync(
                        new NonconformanceCreateRequest(source, lotId, workOrderId, null, content, causeCategory, causeDetail),
                        reportNo, userId, ct);
                    if (createdReport.Failed)
                    {
                        FailRow(reader, createdReport.Error!);
                        continue;
                    }
                    reportId = createdReport.Value!.Id;
                }
            }
            else
            {
                // 番号の分からない不適合（検査の不合格で自動起票されたもの等）は、ロットで1件に決まるときだけ指せる
                var open = await db.NonconformanceReports
                    .Where(n => n.LotId == lotId && n.Status != NonconformanceStatus.Closed)
                    .Select(n => n.Id).ToListAsync(ct);
                if (open.Count != 1)
                {
                    reader.Fail(open.Count == 0
                        ? ApiText.T("ロット '{0}' に未完了の不適合はありません。", lotNumber)
                        : ApiText.T("ロット '{0}' に未完了の不適合が {1} 件あります。ReportNo で指定してください。", lotNumber, open.Count));
                    continue;
                }
                reportId = open[0];
            }

            // 失敗した操作の理由（業務サービスは失敗のときだけ Error を返す）
            string? error = null;
            if (action is not null)
            {
                error = (await nonconformances.InstructAsync(
                    reportId.Value, new NonconformanceActionRequest(action.Value, instruction), userId, ct)).Error;
            }
            if (error is null && actionRecord is not null)
            {
                error = (await nonconformances.RecordActionAsync(
                    reportId.Value, new NonconformanceActionRecordRequest(actionRecord), userId, ct)).Error;
            }
            if (error is null && approve)
            {
                error = (await nonconformances.ApproveAsync(reportId.Value, userId, ct)).Error;
            }
            if (error is not null)
            {
                FailRow(reader, error);
                continue;
            }
            created++;
        }
        return created;
    }

    /// <summary>
    /// サンプル品の保管（D-40-50-01）。1行＝1件のサンプルで、未登録のサンプル番号なら採取（在庫から抜く）、
    /// Close を書けば払出・廃棄で保管を終える。登録済みのサンプル番号の行は保管の終了だけを行う
    /// </summary>
    private async Task<int> ImportSampleStoragesAsync(
        CsvTable table, List<CsvImportError> errors, string? userId, CancellationToken ct)
    {
        var locationIds = await db.Locations.AsNoTracking()
            .ToDictionaryAsync(l => l.Code, l => l.Id, StringComparer.Ordinal, ct);

        var created = 0;
        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            var sampleNo = reader.RequiredText("SampleNo", 50);
            var lotNumber = reader.Text("LotNumber", null);
            var quantity = reader.NumberOrNull("Quantity", null, 0.000001m);
            var locationId = reader.Reference("StorageLocationCode", null, locationIds, "ロケーション");
            var retainUntil = reader.DateOrNull("RetainUntil", null);
            var note = reader.Text("Note", null, 500);
            var close = reader.Text("Close", null) is null
                ? (SampleStorageStatus?)null
                : reader.Enum("Close", SampleStorageStatus.Consumed, CsvEnumLabels.SampleCloseStatuses);
            if (reader.Failed)
            {
                continue;
            }

            var sampleId = await db.SampleStorages.Where(x => x.SampleNo == sampleNo)
                .Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
            if (sampleId is null)
            {
                if (lotNumber is null || quantity is null || locationId is null)
                {
                    reader.Fail(ApiText.T("サンプルを採取する行には LotNumber・Quantity・StorageLocationCode が必要です。"));
                    continue;
                }
                var lotId = await LotIdAsync(reader, lotNumber, "LotNumber", ct);
                if (reader.Failed)
                {
                    continue;
                }
                var collected = await samples.CollectAsync(
                    new SampleCollectRequest(lotId!.Value, locationId.Value, quantity.Value, retainUntil, null, note),
                    sampleNo, userId, ct);
                if (collected.Failed)
                {
                    FailRow(reader, collected.Error!);
                    continue;
                }
                sampleId = collected.Value!.Id;
            }
            else if (close is null)
            {
                reader.Fail(ApiText.T("サンプル番号 '{0}' は既に存在します。", sampleNo));
                continue;
            }

            if (close is not null)
            {
                var closed = await samples.CloseAsync(sampleId.Value, new SampleCloseRequest(close.Value, note), userId, ct);
                if (closed.Failed)
                {
                    FailRow(reader, closed.Error!);
                    continue;
                }
            }
            created++;
        }
        return created;
    }
}
