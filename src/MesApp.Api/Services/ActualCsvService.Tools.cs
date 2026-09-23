using MesApp.Core.Contracts.Maintenance;
using MesApp.Core.Contracts.Masters;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

// 実績CSVのうち治工具と検査機（E-60-20-01 利用実績、B-20-30 引当・払出・返却、C-20-50-03 校正）。
// 利用実績は ShopFloorReportService、引当は ToolIssueService、校正は InspectionDeviceService を通す
public sealed partial class ActualCsvService
{
    /// <summary>治工具の利用実績（E-60-20-01）。1行＝1記録。寿命の累計に入る</summary>
    private async Task<int> ImportToolUsagesAsync(
        CsvTable table, List<CsvImportError> errors, string? userId, CancellationToken ct)
    {
        var workOrders = await WorkOrderKeysAsync(ct);
        var toolIds = await db.Tools.AsNoTracking().Where(t => t.IsActive)
            .ToDictionaryAsync(t => t.Code, t => t.Id, StringComparer.Ordinal, ct);

        var created = 0;
        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            reader.RequiredText("ToolCode");
            var toolId = reader.Reference("ToolCode", null, toolIds, "有効な治工具");
            var workOrderId = ResolveOptionalWorkOrder(reader, workOrders);
            var usageCount = reader.IntOrNull("UsageCount", null, 0) ?? 0;
            var usageHours = reader.NumberOrNull("UsageHours", null, 0m);
            // 空欄なら取り込んだ時刻（寿命の累計はメンテでリセットした時刻より後の記録だけを数える）
            var recordedAt = reader.Text("RecordedAt", null) is null ? null : reader.DateTimeOrNull("RecordedAt", FactoryOffset);
            if (reader.Failed)
            {
                continue;
            }

            var outcome = await reports.AddToolUsageAsync(
                new ToolUsageRequest(toolId!.Value, workOrderId, usageCount, usageHours), recordedAt, userId, ct);
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
    /// 治工具の引当（B-20-30-01）。1行＝1件の引当で、Issue=true なら払出・受領確認（受領者は取り込んだユーザー）、
    /// Return=true なら返却まで進める。引当には番号が無いので、後のファイルからは指さない
    /// </summary>
    private async Task<int> ImportToolIssuesAsync(
        CsvTable table, List<CsvImportError> errors, string? userId, CancellationToken ct)
    {
        var workOrders = await WorkOrderKeysAsync(ct);
        var toolIds = await db.Tools.AsNoTracking()
            .ToDictionaryAsync(t => t.Code, t => t.Id, StringComparer.Ordinal, ct);

        var created = 0;
        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            reader.RequiredText("ToolCode");
            var toolId = reader.Reference("ToolCode", null, toolIds, "治工具");
            var workOrderId = ResolveWorkOrder(reader, workOrders);
            var issue = reader.Bool("Issue", false);
            var returned = reader.Bool("Return", false);
            var note = reader.Text("Note", null, 500);
            if (reader.Failed)
            {
                continue;
            }

            // 引当・払出のどちらでも ToolIssuePolicy が使えるかを判定する（単票と同じ）
            var outcome = await toolIssues.AllocateAsync(new ToolAllocateRequest(toolId!.Value, workOrderId!.Value, note), userId, ct);
            if (!outcome.Failed && issue)
            {
                outcome = await toolIssues.IssueAsync(outcome.Value!.Id, new ToolIssueReceiveRequest(null), userId, ct);
            }
            if (!outcome.Failed && returned)
            {
                outcome = await toolIssues.ReturnAsync(outcome.Value!.Id, new ToolIssueCloseRequest(null), userId, ct);
            }
            if (outcome.Failed)
            {
                FailRow(reader, outcome.Error!);
                continue;
            }
            created++;
        }
        return created;
    }

    /// <summary>検査機・測定器の校正（C-20-50-03）。1行＝1回の校正。検査機の最終校正日と次回期限が更新される</summary>
    private async Task<int> ImportCalibrationsAsync(
        CsvTable table, List<CsvImportError> errors, string? userId, CancellationToken ct)
    {
        var deviceIds = await db.InspectionDevices.AsNoTracking()
            .ToDictionaryAsync(d => d.Code, d => d.Id, StringComparer.Ordinal, ct);

        var created = 0;
        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            reader.RequiredText("DeviceCode");
            var deviceId = reader.Reference("DeviceCode", null, deviceIds, "検査機");
            reader.RequiredText("CalibratedOn");
            var calibratedOn = reader.DateOrNull("CalibratedOn", null);
            var nextDueOn = reader.DateOrNull("NextDueOn", null);
            var result = reader.Text("Result", null, 500);
            if (reader.Failed)
            {
                continue;
            }

            var outcome = await devices.AddCalibrationAsync(deviceId!.Value,
                new InspectionDeviceCalibrationRequest(calibratedOn!.Value, nextDueOn, result), userId, ct);
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
