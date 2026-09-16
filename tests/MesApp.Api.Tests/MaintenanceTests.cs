using System.Net;
using System.Net.Http.Json;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Maintenance;
using MesApp.Core.Contracts.Common;
using MesApp.Core.Contracts.Execution;
using MesApp.Core.Contracts.Inventory;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Contracts.Production;
using MesApp.Core.Contracts.Quality;
using MesApp.Core.Entities;

namespace MesApp.Api.Tests;

public class MaintenanceTests
{
    private static async Task<EquipmentResponse> CreateEquipmentAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/equipments",
            new EquipmentRequest("EQ-01", "プレス機", "第1工場", EquipmentStatus.Available,
                MaintenanceType.Calendar, 90m, "金型・ベルト"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EquipmentResponse>())!;
    }

    private static async Task<ToolResponse> CreateToolAsync(
        HttpClient admin, int? lifeCount = 1000, decimal? lifeHours = null)
    {
        var response = await admin.PostAsJsonAsync("/api/tools",
            new ToolRequest("T-01", "金型A", "型", lifeCount, lifeHours, ToolStatus.Available));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ToolResponse>())!;
    }

    [Fact]
    public async Task 治工具を作業指示へ引き当てて払出し返却できる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var tool = await CreateToolAsync(admin, lifeCount: 100);
        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);
        var first = order.WorkOrders[0];
        var second = order.WorkOrders[1];

        var allocated = await admin.PostAsJsonAsync("/api/tool-issues",
            new ToolAllocateRequest(tool.Id, first.Id, "前段取りで使用"));
        Assert.Equal(HttpStatusCode.Created, allocated.StatusCode);
        var issue = (await allocated.Content.ReadFromJsonAsync<ToolIssueResponse>())!;
        Assert.Equal(ToolIssueStatus.Allocated, issue.Status);

        // 現物は1つしかないので、他の作業指示へは引き当てられない
        var conflict = await admin.PostAsJsonAsync("/api/tool-issues",
            new ToolAllocateRequest(tool.Id, second.Id, null));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        // 払出（受領確認）→ 返却で、治工具は再び引当可能になる
        var issued = await admin.PostAsJsonAsync($"/api/tool-issues/{issue.Id}/issue",
            new ToolIssueReceiveRequest(null));
        issued.EnsureSuccessStatusCode();
        var issuedBody = (await issued.Content.ReadFromJsonAsync<ToolIssueResponse>())!;
        Assert.Equal(ToolIssueStatus.Issued, issuedBody.Status);
        Assert.NotNull(issuedBody.IssuedAt);
        Assert.NotNull(issuedBody.IssuedToName);

        // 払出済みは取り消せない（返却で戻す）
        var cancelAfterIssue = await admin.PostAsJsonAsync($"/api/tool-issues/{issue.Id}/cancel",
            new ToolIssueCloseRequest(null));
        Assert.Equal(HttpStatusCode.Conflict, cancelAfterIssue.StatusCode);

        var returned = await admin.PostAsJsonAsync($"/api/tool-issues/{issue.Id}/return",
            new ToolIssueCloseRequest("摩耗なし"));
        returned.EnsureSuccessStatusCode();
        Assert.Equal(ToolIssueStatus.Returned,
            (await returned.Content.ReadFromJsonAsync<ToolIssueResponse>())!.Status);

        var afterReturn = await admin.GetFromJsonAsync<ToolResponse>($"/api/tools/{tool.Id}");
        Assert.Equal(ToolStatus.Available, afterReturn!.Status);

        var reallocated = await admin.PostAsJsonAsync("/api/tool-issues",
            new ToolAllocateRequest(tool.Id, second.Id, null));
        Assert.Equal(HttpStatusCode.Created, reallocated.StatusCode);
    }

    [Fact]
    public async Task 寿命に達した治工具は引き当てられない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var tool = await CreateToolAsync(admin, lifeCount: 100);
        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);

        // 閾値まで使い切る（E-60-20-02 の寿命到達と同じ条件で引当を止める）
        (await admin.PostAsJsonAsync("/api/tool-usages",
                new ToolUsageRequest(tool.Id, order.WorkOrders[0].Id, 100, null)))
            .EnsureSuccessStatusCode();

        var rejected = await admin.PostAsJsonAsync("/api/tool-issues",
            new ToolAllocateRequest(tool.Id, order.WorkOrders[0].Id, null));
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);

        // メンテナンス中も引き当てられない（治工具コードは一意なので別コードで登録する）
        var created = await admin.PostAsJsonAsync("/api/tools",
            new ToolRequest("T-02", "金型B", "型", null, null, ToolStatus.UnderMaintenance));
        created.EnsureSuccessStatusCode();
        var maintained = (await created.Content.ReadFromJsonAsync<ToolResponse>())!;
        var underMaintenance = await admin.PostAsJsonAsync("/api/tool-issues",
            new ToolAllocateRequest(maintained.Id, order.WorkOrders[0].Id, null));
        Assert.Equal(HttpStatusCode.Conflict, underMaintenance.StatusCode);
    }

    [Fact]
    public async Task 寿命分析で使用ペースから寿命到達の見込みを出せる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var tool = await CreateToolAsync(admin, lifeCount: 100);
        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);

        // 1日で20回使用 → 残り80回、1日平均20回なので4日後に到達する見込み
        (await admin.PostAsJsonAsync("/api/tool-usages",
                new ToolUsageRequest(tool.Id, order.WorkOrders[0].Id, 20, null)))
            .EnsureSuccessStatusCode();

        var rows = await admin.GetFromJsonAsync<List<ToolLifeAnalysisRow>>("/api/tool-usages/life-analysis");
        var row = Assert.Single(rows!, r => r.ToolCode == tool.Code);
        Assert.Equal(20, row.CumulativeCount);
        Assert.Equal(80, row.RemainingCount);
        Assert.Equal(20, row.PeriodUsageCount);
        Assert.Equal(1, row.UsageDays);
        Assert.Equal(20m, row.AveragePerDay);
        Assert.Equal(1, row.WorkOrderCount);
        Assert.Equal(20m, row.AveragePerWorkOrder);
        Assert.NotNull(row.EstimatedLifeReachedOn);

        // 期間外だけを指定すると使用実績が無いので見込みは出さない（0ではなくnull）
        var empty = await admin.GetFromJsonAsync<List<ToolLifeAnalysisRow>>(
            "/api/tool-usages/life-analysis?from=2020-01-01&to=2020-01-31");
        var emptyRow = Assert.Single(empty!, r => r.ToolCode == tool.Code);
        Assert.Equal(0, emptyRow.PeriodUsageCount);
        Assert.Null(emptyRow.AveragePerDay);
        Assert.Null(emptyRow.EstimatedLifeReachedOn);
        // 累計（寿命の消化）は期間に関係なく通算で見る
        Assert.Equal(20, emptyRow.CumulativeCount);
    }

    [Fact]
    public async Task 保全手順書のCRUDと版数管理ができる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var equipment = await CreateEquipmentAsync(admin);

        var created = await admin.PostAsJsonAsync("/api/maintenance-procedures",
            new MaintenanceProcedureRequest("PROC-01", "プレス機月次点検", equipment.Id, null,
                "1. 電源遮断\n2. ベルト張力確認\n3. 給油"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var procedure = await created.Content.ReadFromJsonAsync<MaintenanceProcedureResponse>();
        Assert.Equal(1, procedure!.Version);
        Assert.Equal(equipment.Name, procedure.TargetEquipmentName);

        // 見直し（E-20-30-06）で版数が上がる
        var updated = await admin.PutAsJsonAsync($"/api/maintenance-procedures/{procedure.Id}",
            new MaintenanceProcedureRequest("PROC-01", "プレス機月次点検", equipment.Id, null,
                "1. 電源遮断\n2. ベルト張力確認\n3. 給油\n4. 安全カバー確認"));
        var updatedBody = await updated.Content.ReadFromJsonAsync<MaintenanceProcedureResponse>();
        Assert.Equal(2, updatedBody!.Version);
    }

    /// <summary>消耗品を1品目登録した設備と、その在庫ロットを用意する</summary>
    private static async Task<(EquipmentResponse Equipment, ProductResponse Part, LotResponse Lot, int LocationId)>
        SetupConsumablePartAsync(HttpClient admin, decimal stockQuantity)
    {
        var equipment = await CreateEquipmentAsync(admin);
        var part = await MasterTests.CreateProductAsync(admin, "PT-02", "Oリング", ProductType.Material);
        var location = await admin.PostAsJsonAsync("/api/locations",
            new LocationRequest("LOC-M", LocationAreaType.MaterialWarehouse, null));
        location.EnsureSuccessStatusCode();
        var locationId = (await location.Content.ReadFromJsonAsync<LocationResponse>())!.Id;

        (await admin.PutAsJsonAsync($"/api/equipments/{equipment.Id}/parts",
            new List<EquipmentPartRequest> { new(part.Id, MaintenancePartCategory.Consumable, 2m, null) }))
            .EnsureSuccessStatusCode();

        var lot = await Phase3TestData.ReceiveAsync(admin, part.Id, stockQuantity, locationId);
        return (equipment, part, lot, locationId);
    }

    private static async Task<MaintenanceOrderResponse> CreateSpotOrderAsync(HttpClient admin, int equipmentId)
    {
        var response = await admin.PostAsJsonAsync("/api/maintenance-orders",
            new MaintenanceOrderCreateRequest(equipmentId, null, null, null, null,
                MaintenanceRequestType.Spot, "Oリング交換"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MaintenanceOrderResponse>())!;
    }

    [Fact]
    public async Task 保全実績の消費部材で在庫が引き落とされ消耗材モニタリングに集計される()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var (equipment, part, lot, locationId) = await SetupConsumablePartAsync(admin, 10m);
        var order = await CreateSpotOrderAsync(admin, equipment.Id);

        var recorded = await admin.PostAsJsonAsync($"/api/maintenance-orders/{order.Id}/record",
            new MaintenanceRecordRequest(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow,
                "目視で劣化を確認", "交換完了", null, false,
                [new MaintenanceRecordPartRequest(lot.Id, locationId, 3m, "2本のうち3本使用")]));
        Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);

        var saved = (await recorded.Content.ReadFromJsonAsync<MaintenanceOrderResponse>())!;
        var line = Assert.Single(Assert.Single(saved.Records).Parts);
        Assert.Equal("PT-02", line.ProductCode);
        Assert.Equal(lot.LotNumber, line.LotNumber);
        Assert.Equal(3m, line.Quantity);

        // 引落しは InventoryService 経由なので、在庫と在庫履歴の両方に出る
        Assert.Equal(7m, await Phase3TestData.GetStockQuantityAsync(admin, lot.Id));
        var transactions = await admin.GetFromJsonAsync<PagedResult<TransactionResponse>>(
            $"/api/inventory/transactions?productId={part.Id}");
        Assert.Contains(transactions!.Items,
            x => x.Type == InventoryTransactionType.MaintenanceIssue && x.Quantity == 3m);

        // 消耗材モニタリング（E-20-10-04）：消費数量と現在庫が並ぶ
        var consumption = await admin.GetFromJsonAsync<List<MaintenancePartConsumptionRow>>(
            "/api/maintenance-orders/parts-consumption");
        var row = Assert.Single(consumption!);
        Assert.Equal("PT-02", row.ProductCode);
        Assert.Equal(3m, row.Quantity);
        Assert.Equal(1, row.RecordCount);
        Assert.Equal(7m, row.StockOnHand);

        // 期間で絞れる（SQLiteはDateTimeOffsetの比較をSQLへ翻訳できないため、取り出してから絞る経路を通す）
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var inRange = await admin.GetFromJsonAsync<List<MaintenancePartConsumptionRow>>(
            $"/api/maintenance-orders/parts-consumption?from={today.AddDays(-1):yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        Assert.Single(inRange!);
        var outOfRange = await admin.GetFromJsonAsync<List<MaintenancePartConsumptionRow>>(
            $"/api/maintenance-orders/parts-consumption?from={today.AddDays(1):yyyy-MM-dd}");
        Assert.Empty(outOfRange!);
    }

    [Fact]
    public async Task 資産管理部品と在庫不足は保全実績の消費部材にできない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var (equipment, _, lot, locationId) = await SetupConsumablePartAsync(admin, 5m);

        // 在庫不足は400。実績ごと弾くので指示は未完了のまま
        var order = await CreateSpotOrderAsync(admin, equipment.Id);
        var shortage = await admin.PostAsJsonAsync($"/api/maintenance-orders/{order.Id}/record",
            new MaintenanceRecordRequest(DateTimeOffset.UtcNow, null, null, null, null, false,
                [new MaintenanceRecordPartRequest(lot.Id, locationId, 99m, null)]));
        Assert.Equal(HttpStatusCode.BadRequest, shortage.StatusCode);
        var stillOpen = await admin.GetFromJsonAsync<MaintenanceOrderResponse>(
            $"/api/maintenance-orders/{order.Id}");
        Assert.Equal(MaintenanceOrderStatus.Instructed, stillOpen!.Status);
        Assert.Equal(5m, await Phase3TestData.GetStockQuantityAsync(admin, lot.Id));

        // 資産管理部品（金型）は数量在庫の引落し対象外
        var mold = await MasterTests.CreateProductAsync(admin, "PT-01", "金型A", ProductType.Material);
        (await admin.PutAsJsonAsync($"/api/equipments/{equipment.Id}/parts",
            new List<EquipmentPartRequest> { new(mold.Id, MaintenancePartCategory.Asset, 1m, null) }))
            .EnsureSuccessStatusCode();
        var moldLot = await Phase3TestData.ReceiveAsync(admin, mold.Id, 2m, locationId, lotNumber: "MOLD-1");
        var asset = await admin.PostAsJsonAsync($"/api/maintenance-orders/{order.Id}/record",
            new MaintenanceRecordRequest(DateTimeOffset.UtcNow, null, null, null, null, false,
                [new MaintenanceRecordPartRequest(moldLot.Id, locationId, 1m, null)]));
        Assert.Equal(HttpStatusCode.BadRequest, asset.StatusCode);
        Assert.Equal(2m, await Phase3TestData.GetStockQuantityAsync(admin, moldLot.Id));
    }

    [Fact]
    public async Task 設備の保全部品を品目参照で管理でき資産管理部品と消耗品を区別できる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var equipment = await CreateEquipmentAsync(admin);
        var mold = await MasterTests.CreateProductAsync(admin, "PT-01", "金型A", ProductType.Material);
        var oring = await MasterTests.CreateProductAsync(admin, "PT-02", "Oリング", ProductType.Material);

        var saved = await admin.PutAsJsonAsync($"/api/equipments/{equipment.Id}/parts",
            new List<EquipmentPartRequest>
            {
                new(mold.Id, MaintenancePartCategory.Asset, 1m, "資産番号で個体管理"),
                new(oring.Id, MaintenancePartCategory.Consumable, 2m, null),
            });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var parts = await saved.Content.ReadFromJsonAsync<List<EquipmentPartResponse>>();
        Assert.Equal(["PT-01", "PT-02"], parts!.Select(p => p.ProductCode));
        Assert.Equal(MaintenancePartCategory.Asset, parts![0].Category);
        Assert.Equal(2m, parts[1].QuantityPer);

        // 同じ品目を2行は400
        var duplicated = await admin.PutAsJsonAsync($"/api/equipments/{equipment.Id}/parts",
            new List<EquipmentPartRequest>
            {
                new(oring.Id, MaintenancePartCategory.Consumable, 1m, null),
                new(oring.Id, MaintenancePartCategory.Consumable, 2m, null),
            });
        Assert.Equal(HttpStatusCode.BadRequest, duplicated.StatusCode);

        // 存在しない品目は400
        var missing = await admin.PutAsJsonAsync($"/api/equipments/{equipment.Id}/parts",
            new List<EquipmentPartRequest> { new(9999, MaintenancePartCategory.Consumable, 1m, null) });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        // 一括置換（工順と同じ方式）：残した行だけになる
        (await admin.PutAsJsonAsync($"/api/equipments/{equipment.Id}/parts",
            new List<EquipmentPartRequest> { new(oring.Id, MaintenancePartCategory.Consumable, 3m, null) }))
            .EnsureSuccessStatusCode();
        var after = await admin.GetFromJsonAsync<List<EquipmentPartResponse>>(
            $"/api/equipments/{equipment.Id}/parts");
        Assert.Equal("PT-02", Assert.Single(after!).ProductCode);
        Assert.Equal(3m, after![0].QuantityPer);
    }

    [Fact]
    public async Task 設備稼働ログを作業指示に紐付けるとロットの履歴から辿れる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var equipment = await CreateEquipmentAsync(admin);

        // 指図を展開して作業指示と産出ロットを作る
        var product = await MasterTests.CreateProductAsync(admin, "FG-01", "完成品", ProductType.Product);
        var process = await MasterTests.CreateProcessAsync(admin, "PR-01", "組立");
        (await admin.PutAsJsonAsync($"/api/products/{product.Id}/routing",
            new List<RoutingStepRequest> { new(1, process.Id, 30m, 10m, null, null, null, null, null) }))
            .EnsureSuccessStatusCode();
        var order = await admin.PostAsJsonAsync("/api/manufacturing-orders",
            new CreateManufacturingOrderRequest(product.Id, 10m, null,
                ManufacturingOrderType.Normal, null, null));
        var orderBody = await order.Content.ReadFromJsonAsync<ManufacturingOrderResponse>();
        await admin.PostAsync($"/api/manufacturing-orders/{orderBody!.Id}/approve", null);
        var expanded = await admin.PostAsJsonAsync(
            $"/api/manufacturing-orders/{orderBody.Id}/expand", new ExpandRequest(null));
        var detail = await expanded.Content.ReadFromJsonAsync<ManufacturingOrderDetailResponse>();
        var workOrder = detail!.WorkOrders.First();

        var start = DateTimeOffset.Now.AddHours(-4);

        // 存在しない作業指示は400
        var missing = await admin.PostAsJsonAsync("/api/equipment-logs",
            new EquipmentLogRequest(equipment.Id, EquipmentLogStatus.Running,
                start, start.AddHours(1), null, null, 9999));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        // 作業指示に紐付けた稼働（PQC×EQCの交差点）と、紐付かないアイドル
        var linked = await admin.PostAsJsonAsync("/api/equipment-logs",
            new EquipmentLogRequest(equipment.Id, EquipmentLogStatus.Running,
                start, start.AddHours(3), null, null, workOrder.Id));
        linked.EnsureSuccessStatusCode();
        var linkedBody = await linked.Content.ReadFromJsonAsync<EquipmentLogResponse>();
        Assert.Equal(workOrder.WorkOrderNo, linkedBody!.WorkOrderNo);

        (await admin.PostAsJsonAsync("/api/equipment-logs",
            new EquipmentLogRequest(equipment.Id, EquipmentLogStatus.Idle,
                start.AddHours(3), start.AddHours(4), null, null)))
            .EnsureSuccessStatusCode();

        // 作業指示で絞り込める
        var filtered = await admin.GetFromJsonAsync<PagedResult<EquipmentLogResponse>>(
            $"/api/equipment-logs?workOrderId={workOrder.Id}");
        Assert.Equal(1, filtered!.Total);

        // アイドルはサマリの時間区分に出て、稼働率の分母にも入る（3/4=75%）
        var summary = await admin.GetFromJsonAsync<List<EquipmentUtilizationRow>>("/api/equipment-logs/summary");
        var row = Assert.Single(summary!);
        Assert.Equal(1m, row.IdleHours);
        Assert.Equal(75m, row.UtilizationRate);

        // 産出ロットの履歴から設備稼働履歴を辿れる（H-30-10-04）
        var location = await admin.PostAsJsonAsync("/api/locations",
            new LocationRequest("LOC-01", LocationAreaType.ProductWarehouse, "A-1"));
        location.EnsureSuccessStatusCode();
        var locationBody = await location.Content.ReadFromJsonAsync<LocationResponse>();
        var record = await admin.PostAsJsonAsync($"/api/work-orders/{workOrder.Id}/production-records",
            new ProductionRecordRequest(10m, 0m, start, start.AddHours(3), locationBody!.Id, false));
        record.EnsureSuccessStatusCode();
        var recordBody = await record.Content.ReadFromJsonAsync<ProductionRecordResponse>();
        var lotId = recordBody!.OutputLotId;
        Assert.NotNull(lotId);
        var history = await admin.GetFromJsonAsync<LotHistoryResponse>($"/api/traceability/{lotId}/history");
        Assert.Contains(history!.EquipmentHistory, h => h.Contains(workOrder.WorkOrderNo));
        // 紐付けのない稼働は履歴に出ない（別のロットの設備状態を混ぜて見せないため）
        Assert.DoesNotContain(history.EquipmentHistory, h => h.Contains("アイドル"));
        // 状態は日本語で出す（履歴は人が読む前提。CLAUDE.md のUI文言の方針）
        Assert.Contains(history.EquipmentHistory, h => h.Contains("[稼働]"));
    }

    [Fact]
    public async Task OEEを三要素で算出し紐付けのない稼働は対象外にする()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var equipment = await CreateEquipmentAsync(admin);
        var other = await admin.PostAsJsonAsync("/api/equipments",
            new EquipmentRequest("EQ-02", "検査機", null, EquipmentStatus.Available,
                MaintenanceType.Calendar, null, null));
        other.EnsureSuccessStatusCode();
        var otherEquipment = (await other.Content.ReadFromJsonAsync<EquipmentResponse>())!;

        // 工順は1段目が作業30分/個、2段目が15分/個（Phase3TestData）
        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);
        var first = order.WorkOrders[0];
        var second = order.WorkOrders[1];

        // 実行時刻に依存しないよう固定日で組み立てる（製造日の境界をまたがない10時台）
        var day = new DateOnly(2026, 1, 20);
        DateTimeOffset At(int hour) =>
            new(day.ToDateTime(new TimeOnly(hour, 0)),
                TimeZoneInfo.Local.GetUtcOffset(day.ToDateTime(new TimeOnly(hour, 0))));

        // 同じ設備の同じ時間帯に2つの作業指示が紐づく（まとめ処理）。按分して二重に数えない
        foreach (var workOrderId in new[] { first.Id, second.Id })
        {
            (await admin.PostAsJsonAsync("/api/equipment-logs",
                    new EquipmentLogRequest(equipment.Id, EquipmentLogStatus.Running,
                        At(10), At(16), null, null, workOrderId)))
                .EnsureSuccessStatusCode();
        }
        (await admin.PostAsJsonAsync("/api/equipment-logs",
                new EquipmentLogRequest(equipment.Id, EquipmentLogStatus.Stopped,
                    At(16), At(18), "材料待ち", null)))
            .EnsureSuccessStatusCode();

        // 作業指示に紐づかない稼働（EQ-02）は性能・良品率を出せない
        (await admin.PostAsJsonAsync("/api/equipment-logs",
                new EquipmentLogRequest(otherEquipment.Id, EquipmentLogStatus.Running,
                    At(10), At(14), null, null)))
            .EnsureSuccessStatusCode();

        (await admin.PostAsJsonAsync($"/api/work-orders/{first.Id}/production-records",
                new ProductionRecordRequest(8m, 2m, At(10), At(16), ctx.ProductLocationId, false)))
            .EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/work-orders/{second.Id}/production-records",
                new ProductionRecordRequest(10m, 0m, At(10), At(16), ctx.ProductLocationId, false)))
            .EnsureSuccessStatusCode();

        var rows = await admin.GetFromJsonAsync<List<EquipmentOeeRow>>("/api/oee");
        var press = Assert.Single(rows!, r => r.AssetNo == "EQ-01");

        // 負荷14h（稼働6h×2＋停止2h）に対し稼働12h
        Assert.Equal(14m, press.LoadHours);
        Assert.Equal(12m, press.RunningHours);
        Assert.Equal(85.71m, press.AvailabilityRate);

        // 重なる2区間は按分され、紐づいた稼働は実時間の6hに収まる（12hにならない）
        Assert.Equal(6m, press.CoveredRunningHours);
        Assert.Equal(50m, press.CoverageRate);

        // 理論時間 30分×10個 + 15分×10個 = 450分 ÷ 紐づいた稼働360分
        Assert.Equal(20m, press.ProducedQuantity);
        Assert.Equal(18m, press.GoodQuantity);
        Assert.Equal(125m, press.PerformanceRate);
        Assert.Equal(90m, press.QualityRate);
        Assert.Equal(96.42m, press.Oee);

        // 紐付けのない稼働だけの設備は、0%ではなくnullで返す
        var inspector = Assert.Single(rows!, r => r.AssetNo == "EQ-02");
        Assert.Equal(100m, inspector.AvailabilityRate);
        Assert.Equal(0m, inspector.CoverageRate);
        Assert.Null(inspector.PerformanceRate);
        Assert.Null(inspector.QualityRate);
        Assert.Null(inspector.Oee);
    }

    [Fact]
    public async Task 稼働サマリを製造日の期間で絞り込める()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var equipment = await CreateEquipmentAsync(admin);

        // 製造日の境界（既定6時）を避けるため、どちらもローカル10時に始める。
        // 実行時刻に依存しない固定日で組み立てる（現在時刻から相対に取ると、
        // 境界前に走らせたときだけ前日へ寄って落ちる）
        var oldDay = new DateOnly(2026, 1, 10);
        var newDay = new DateOnly(2026, 1, 20);
        DateTimeOffset At(DateOnly day, int hour) =>
            new(day.ToDateTime(new TimeOnly(hour, 0)), TimeZoneInfo.Local.GetUtcOffset(
                day.ToDateTime(new TimeOnly(hour, 0))));

        (await admin.PostAsJsonAsync("/api/equipment-logs",
            new EquipmentLogRequest(equipment.Id, EquipmentLogStatus.Running,
                At(oldDay, 10), At(oldDay, 12), null, null)))
            .EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/equipment-logs",
            new EquipmentLogRequest(equipment.Id, EquipmentLogStatus.Running,
                At(newDay, 10), At(newDay, 16), null, null)))
            .EnsureSuccessStatusCode();

        // 省略時は従来どおり全期間（2+6時間）
        var all = await admin.GetFromJsonAsync<List<EquipmentUtilizationRow>>("/api/equipment-logs/summary");
        Assert.Equal(8m, Assert.Single(all!).RunningHours);

        // 期間を指定すると、その製造日に始まった稼働だけを集計する
        var narrowed = await admin.GetFromJsonAsync<List<EquipmentUtilizationRow>>(
            $"/api/equipment-logs/summary?from={newDay:yyyy-MM-dd}&to={newDay:yyyy-MM-dd}");
        Assert.Equal(6m, Assert.Single(narrowed!).RunningHours);

        // 稼働のない期間は行ごと出ない（0時間の設備を並べない）
        var empty = await admin.GetFromJsonAsync<List<EquipmentUtilizationRow>>(
            "/api/equipment-logs/summary?from=2026-02-01&to=2026-02-01");
        Assert.Empty(empty!);
    }

    [Fact]
    public async Task 設備稼働ログの記録と稼働サマリを取得できる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var equipment = await CreateEquipmentAsync(admin);
        var start = DateTimeOffset.Now.AddHours(-8);

        // 稼働6時間・故障2時間（停止原因必須）
        (await admin.PostAsJsonAsync("/api/equipment-logs",
            new EquipmentLogRequest(equipment.Id, EquipmentLogStatus.Running, start, start.AddHours(6), null, null)))
            .EnsureSuccessStatusCode();
        var noCause = await admin.PostAsJsonAsync("/api/equipment-logs",
            new EquipmentLogRequest(equipment.Id, EquipmentLogStatus.Failure,
                start.AddHours(6), start.AddHours(8), null, null));
        Assert.Equal(HttpStatusCode.BadRequest, noCause.StatusCode);
        (await admin.PostAsJsonAsync("/api/equipment-logs",
            new EquipmentLogRequest(equipment.Id, EquipmentLogStatus.Failure,
                start.AddHours(6), start.AddHours(8), "モーター過熱", null)))
            .EnsureSuccessStatusCode();

        // サマリ（E-20-10-03）：稼働率 = 6 / 8 = 75%
        var summary = await admin.GetFromJsonAsync<List<EquipmentUtilizationRow>>("/api/equipment-logs/summary");
        var row = Assert.Single(summary!);
        Assert.Equal(6m, row.RunningHours);
        Assert.Equal(2m, row.FailureHours);
        Assert.Equal(1, row.FailureCount);
        Assert.Equal(75m, row.UtilizationRate);
    }

    [Fact]
    public async Task 保全計画から指示発行実績登録で計画まで完了する()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var equipment = await CreateEquipmentAsync(admin);

        // 計画作成（E-30-10）
        var planCreated = await admin.PostAsJsonAsync("/api/maintenance-plans",
            new MaintenancePlanRequest(equipment.Id, MaintenanceCategory.Periodic, 2026,
                DateOnly.FromDateTime(DateTime.Today).AddDays(7), 90, "定期点検"));
        Assert.Equal(HttpStatusCode.Created, planCreated.StatusCode);
        var plan = await planCreated.Content.ReadFromJsonAsync<MaintenancePlanResponse>();

        // 指示作成（E-30-20-01）→ 計画は指示発行済みへ
        var orderCreated = await admin.PostAsJsonAsync("/api/maintenance-orders",
            new MaintenanceOrderCreateRequest(equipment.Id, null, plan!.Id, null,
                plan.ScheduledDate, MaintenanceRequestType.Planned, null));
        Assert.Equal(HttpStatusCode.Created, orderCreated.StatusCode);
        var order = await orderCreated.Content.ReadFromJsonAsync<MaintenanceOrderResponse>();
        Assert.StartsWith("MT", order!.OrderNo);
        var planAfterOrder = await admin.GetFromJsonAsync<MaintenancePlanResponse>(
            $"/api/maintenance-plans/{plan.Id}");
        Assert.Equal(MaintenancePlanStatus.Ordered, planAfterOrder!.Status);

        // 実績登録（E-40-30-01）→ 指示完了・計画完了
        var recorded = await admin.PostAsJsonAsync($"/api/maintenance-orders/{order.Id}/record",
            new MaintenanceRecordRequest(DateTimeOffset.Now.AddHours(-2), DateTimeOffset.Now,
                "ベルト×1", "ベルト交換・給油実施", null));
        Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);
        var completed = await recorded.Content.ReadFromJsonAsync<MaintenanceOrderResponse>();
        Assert.Equal(MaintenanceOrderStatus.Completed, completed!.Status);
        Assert.Single(completed.Records);
        var planAfterRecord = await admin.GetFromJsonAsync<MaintenancePlanResponse>(
            $"/api/maintenance-plans/{plan.Id}");
        Assert.Equal(MaintenancePlanStatus.Completed, planAfterRecord!.Status);
    }

    [Fact]
    public async Task 突発の保全依頼は作業者も起票できるが計画保全は保全ロールのみ()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var equipment = await CreateEquipmentAsync(admin);
        using var operator_ = await TestAuth.CreateUserClientAsync(
            factory, admin, "operator1", "Passw0rd123", MesRoles.Operator);

        // 突発依頼（E-30-30-01）は作業者も可
        var spot = await operator_.PostAsJsonAsync("/api/maintenance-orders",
            new MaintenanceOrderCreateRequest(equipment.Id, null, null, null, null,
                MaintenanceRequestType.Spot, "異音がする"));
        Assert.Equal(HttpStatusCode.Created, spot.StatusCode);

        // 計画保全は保全ロールのみ
        var planned = await operator_.PostAsJsonAsync("/api/maintenance-orders",
            new MaintenanceOrderCreateRequest(equipment.Id, null, null, null, null,
                MaintenanceRequestType.Planned, null));
        Assert.Equal(HttpStatusCode.Forbidden, planned.StatusCode);

        // 保全計画の作成も保全ロールのみ
        var plan = await operator_.PostAsJsonAsync("/api/maintenance-plans",
            new MaintenancePlanRequest(equipment.Id, MaintenanceCategory.Periodic, 2026, null, null, null));
        Assert.Equal(HttpStatusCode.Forbidden, plan.StatusCode);
    }

    [Fact]
    public async Task 治工具の利用実績から寿命警告と要交換を検出できる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var tool = await CreateToolAsync(admin, lifeCount: 1000);

        // 850回使用 → 85%で警告（E-60-20-02）
        (await admin.PostAsJsonAsync("/api/tool-usages",
            new ToolUsageRequest(tool.Id, null, 850, null))).EnsureSuccessStatusCode();
        var status1 = await admin.GetFromJsonAsync<List<ToolLifeStatusRow>>("/api/tool-usages/life-status");
        var row1 = Assert.Single(status1!);
        Assert.Equal(850, row1.CumulativeCount);
        Assert.Equal(85m, row1.LifeUsageRate);
        Assert.True(row1.IsWarning);
        Assert.False(row1.IsLifeReached);

        // さらに200回 → 105%で要交換
        (await admin.PostAsJsonAsync("/api/tool-usages",
            new ToolUsageRequest(tool.Id, null, 200, null))).EnsureSuccessStatusCode();
        var status2 = await admin.GetFromJsonAsync<List<ToolLifeStatusRow>>("/api/tool-usages/life-status?alertOnly=true");
        var row2 = Assert.Single(status2!);
        Assert.True(row2.IsLifeReached);
    }

    [Fact]
    public async Task 治工具メンテナンス完了で寿命カウンタがリセットされる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var tool = await CreateToolAsync(admin, lifeCount: 1000);
        (await admin.PostAsJsonAsync("/api/tool-usages",
            new ToolUsageRequest(tool.Id, null, 900, null))).EnsureSuccessStatusCode();

        // 治工具メンテ指示（E-60-30-01）→ 実績登録（寿命リセット指定）
        var orderCreated = await admin.PostAsJsonAsync("/api/maintenance-orders",
            new MaintenanceOrderCreateRequest(null, tool.Id, null, null, null,
                MaintenanceRequestType.Spot, "再研磨"));
        var order = await orderCreated.Content.ReadFromJsonAsync<MaintenanceOrderResponse>();
        var recorded = await admin.PostAsJsonAsync($"/api/maintenance-orders/{order!.Id}/record",
            new MaintenanceRecordRequest(DateTimeOffset.Now.AddHours(-1), DateTimeOffset.Now,
                null, "再研磨実施", null, ResetToolLife: true));
        Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);

        // 寿命カウンタは0に戻る
        var status = await admin.GetFromJsonAsync<List<ToolLifeStatusRow>>("/api/tool-usages/life-status");
        var row = Assert.Single(status!);
        Assert.Equal(0, row.CumulativeCount);
        Assert.NotNull(row.LifeResetAt);
        Assert.False(row.IsLifeReached);

        // リセット後の新しい利用実績のみ累計される
        (await admin.PostAsJsonAsync("/api/tool-usages",
            new ToolUsageRequest(tool.Id, null, 100, null))).EnsureSuccessStatusCode();
        var after = await admin.GetFromJsonAsync<List<ToolLifeStatusRow>>("/api/tool-usages/life-status");
        Assert.Equal(100, after!.Single().CumulativeCount);
    }

    [Fact]
    public async Task 設備と治工具の両方指定または未指定の保全指示は作成できない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var equipment = await CreateEquipmentAsync(admin);
        var tool = await CreateToolAsync(admin);

        var both = await admin.PostAsJsonAsync("/api/maintenance-orders",
            new MaintenanceOrderCreateRequest(equipment.Id, tool.Id, null, null, null,
                MaintenanceRequestType.Spot, null));
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);

        var neither = await admin.PostAsJsonAsync("/api/maintenance-orders",
            new MaintenanceOrderCreateRequest(null, null, null, null, null,
                MaintenanceRequestType.Spot, null));
        Assert.Equal(HttpStatusCode.BadRequest, neither.StatusCode);
    }
}
