using MesApp.Api.Localization;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Masters;
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
public sealed partial class ActualCsvService(
    MesAppDbContext db,
    ReceivingService receiving,
    ManufacturingOrderService orders,
    WorkOrderExecutionService execution,
    InspectionService inspections,
    ShopFloorReportService reports,
    ShippingService shipping,
    ShipmentJudgmentService judgments,
    MaintenanceOrderService maintenanceOrders,
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
            ActualCsvKinds.EquipmentLogs => await ImportEquipmentLogsAsync(table, errors, userId, ct),
            ActualCsvKinds.ShippingOrders => await ImportShippingOrdersAsync(table, errors, userId, ct),
            ActualCsvKinds.ShipmentJudgments => await ImportShipmentJudgmentsAsync(table, errors, userId!, ct),
            ActualCsvKinds.Shipments => await ImportShipmentsAsync(table, errors, userId, ct),
            ActualCsvKinds.MaintenanceOrders => await ImportMaintenanceOrdersAsync(table, errors, userId, ct),
            ActualCsvKinds.MaintenanceRecords => await ImportMaintenanceRecordsAsync(table, errors, userId!, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        if (errors.Count == 0)
        {
            await auditLogger.LogAsync("Actual", "CsvImport", kind.Info.Kind, null,
                detail: $"rows={table.Rows.Count}, created={created}", ct: ct);
        }
        return new CsvFileImportCount(created, 0);
    }

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
            reader.Fail(ApiText.T("Sequence（工程順序）は必須です。"));
        }
        if (reader.Failed)
        {
            return null;
        }
        if (workOrders.TryGetValue((orderNo, sequence!.Value), out var id))
        {
            return id;
        }
        reader.Fail(ApiText.T("指図 '{0}' の工程順序 {1} の作業指示はありません（展開済みか、工程順序を確認してください）。", orderNo, sequence));
        return null;
    }

    /// <summary>OrderNo を書いた行だけ作業指示を解決する（空欄なら紐づけない）</summary>
    private static int? ResolveOptionalWorkOrder(
        CsvRowReader reader, Dictionary<(string OrderNo, int Sequence), int> workOrders) =>
        reader.Text("OrderNo", null) is null ? null : ResolveWorkOrder(reader, workOrders);

    private DateTimeOffset? RequiredDateTime(CsvRowReader reader, string column)
    {
        var text = reader.RequiredText(column);
        return text.Length == 0 ? null : reader.DateTimeOrNull(column, FactoryOffset);
    }
}
