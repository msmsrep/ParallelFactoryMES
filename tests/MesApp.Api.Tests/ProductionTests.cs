using System.Net;
using System.Net.Http.Json;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Audit;
using MesApp.Core.Contracts.Common;
using MesApp.Core.Contracts.Dashboard;
using MesApp.Core.Contracts.Execution;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Contracts.Planning;
using MesApp.Core.Contracts.Maintenance;
using MesApp.Core.Contracts.Production;
using MesApp.Core.Contracts.Quality;
using MesApp.Core.Contracts.Users;
using MesApp.Core.Entities;

namespace MesApp.Api.Tests;

public class ProductionTests
{
    /// <summary>品目＋工程＋工順（必要スキル付き可）を登録して品目IDを返す</summary>
    private static async Task<(int ProductId, int ProcessId)> SetupMastersAsync(
        HttpClient admin, int? requiredSkillId = null)
    {
        var product = await MasterTests.CreateProductAsync(admin, "FG-01", "完成品", ProductType.Product);
        var process = await MasterTests.CreateProcessAsync(admin, "PR-01", "組立");
        var routing = await admin.PutAsJsonAsync($"/api/products/{product.Id}/routing",
            new List<RoutingStepRequest>
            {
                new(1, process.Id, 30m, 10m, requiredSkillId, null, null, null, null),
                new(2, process.Id, 15m, 5m, null, null, null, null, null),
            });
        routing.EnsureSuccessStatusCode();
        return (product.Id, process.Id);
    }

    [Fact]
    public async Task 製造指図をCSVで登録し承認と工程展開まで進められる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        await Phase3TestData.SetupAsync(admin);
        const string header = "OrderNo,ProductCode,Quantity,DueDate,OrderType,SourceOrderNo,Note,Approve,Expand,OutputLotNumber\n";

        var result = await Phase3TestData.ImportActualCsvAsync(admin, "manufacturing-orders",
            header
            + "CSV-MO-1,FG-01,10,2026-10-31,通常,,初回,true,true,FG-LOT-1\n"
            + "CSV-MO-2,FG-01,5,,Spot,,,true,false,\n"
            + "CSV-MO-3,FG-01,2,,Rework,CSV-MO-1,同じファイルの前の行を元指図にする,false,false,\n"
            + ",FG-01,1,,,,,,,\n");
        Assert.True(result.Succeeded, string.Join(" / ", result.Errors.Select(e => $"{e.Line}行目 {e.Message}")));
        Assert.Equal(4, result.Created);

        var orders = (await admin.GetFromJsonAsync<PagedResult<ManufacturingOrderResponse>>(
            "/api/manufacturing-orders"))!.Items;
        Assert.Equal(4, orders.Count);
        var expanded = orders.Single(o => o.OrderNo == "CSV-MO-1");
        Assert.Equal(ManufacturingOrderStatus.Released, expanded.Status);
        Assert.Equal("FG-LOT-1", expanded.OutputLotNumber);
        Assert.Equal(new DateOnly(2026, 10, 31), expanded.DueDate);
        var detail = await admin.GetFromJsonAsync<ManufacturingOrderDetailResponse>(
            $"/api/manufacturing-orders/{expanded.Id}");
        // 後続の実績CSVは「指図番号＋工程順序」で作業指示を指す
        Assert.Equal(["CSV-MO-1-01", "CSV-MO-1-02"], detail!.WorkOrders.Select(w => w.WorkOrderNo));
        Assert.Equal(ManufacturingOrderStatus.Approved, orders.Single(o => o.OrderNo == "CSV-MO-2").Status);
        var rework = orders.Single(o => o.OrderNo == "CSV-MO-3");
        Assert.Equal(ManufacturingOrderStatus.Draft, rework.Status);
        Assert.Equal(expanded.Id, rework.SourceOrderId);
        Assert.StartsWith("MO", Assert.Single(orders, o => !o.OrderNo.StartsWith("CSV-")).OrderNo);

        // 不正な行は行番号付きで返り、正しい行も含めて1件も登録されない
        var invalid = await Phase3TestData.ImportActualCsvAsync(admin, "manufacturing-orders",
            header
            + "CSV-MO-4,FG-01,1,,,,,true,true,\n"
            + "CSV-MO-1,FG-01,1,,,,,,,\n"
            + "MO-MANUAL,FG-01,1,,,,,,,\n"
            + "CSV-MO-5,FG-01,1,,,,,false,true,\n"
            + "CSV-MO-6,FG-01,1,,Rework,NO-SUCH,,,,\n"
            + "CSV-MO-7,RM-01,1,,,,,true,true,\n"
            + "CSV-MO-8,FG-01,1,,,,,true,true,FG-LOT-1\n");
        Assert.False(invalid.Succeeded);
        Assert.Contains(invalid.Errors, e => e.Line == 3 && e.Message.Contains("既に存在"));
        Assert.Contains(invalid.Errors, e => e.Line == 4 && e.Message.Contains("自動採番"));
        Assert.Contains(invalid.Errors, e => e.Line == 5 && e.Message.Contains("Approve"));
        Assert.Contains(invalid.Errors, e => e.Line == 6 && e.Message.Contains("NO-SUCH"));
        Assert.Contains(invalid.Errors, e => e.Line == 7 && e.Message.Contains("工順"));
        Assert.Contains(invalid.Errors, e => e.Line == 8 && e.Message.Contains("FG-LOT-1"));
        Assert.Equal(4, (await admin.GetFromJsonAsync<PagedResult<ManufacturingOrderResponse>>(
            "/api/manufacturing-orders"))!.Total);

        // 取込の権限は単票の指図APIと同じ（作業者は指図を発行できない）
        using var operator_ = await TestAuth.CreateUserClientAsync(
            factory, admin, "operator1", "Passw0rd123", MesRoles.Operator);
        Assert.Equal(HttpStatusCode.Forbidden, (await Phase3TestData.PostActualCsvAsync(
            operator_, "manufacturing-orders", header + ",FG-01,1,,,,,,,\n")).StatusCode);
    }

    private static async Task<ManufacturingOrderResponse> CreateOrderAsync(
        HttpClient admin, int productId, decimal quantity = 100m, DateOnly? dueDate = null)
    {
        var response = await admin.PostAsJsonAsync("/api/manufacturing-orders",
            new CreateManufacturingOrderRequest(productId, quantity, dueDate,
                ManufacturingOrderType.Normal, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ManufacturingOrderResponse>())!;
    }

    [Fact]
    public async Task 工程管理項目は工順の工程ごとの紐付けどおりに展開され以降のマスタ改訂で指示が変わらない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);

        var product = await MasterTests.CreateProductAsync(admin, "FG-01", "完成品", ProductType.Product);
        var heating = await MasterTests.CreateProcessAsync(admin, "PR-01", "加熱");
        var inspection = await MasterTests.CreateProcessAsync(admin, "PR-02", "検査");
        async Task<int> CreateItemAsync(ControlItemRequest request)
        {
            var created = await admin.PostAsJsonAsync("/api/control-items", request);
            created.EnsureSuccessStatusCode();
            return (await created.Content.ReadFromJsonAsync<ControlItemResponse>())!.Id;
        }
        var weightId = await CreateItemAsync(new("CI-01", "投入重量", "kg", 10m, 9.5m, 10.5m));
        var temperatureId = await CreateItemAsync(new("CI-02", "加熱温度", "℃", 180m, 175m, 185m));
        var reheatId = await CreateItemAsync(new("CI-03", "再加熱温度", "℃", 150m, 145m, 155m));

        // 同じ加熱工程が2回ある工順。工程ごとに別の条件を持たせ、検査工程には何も紐付けない
        (await admin.PutAsJsonAsync($"/api/products/{product.Id}/routing",
            new List<RoutingStepRequest>
            {
                new(1, heating.Id, 30m, 10m, null, null, null, null, null, ControlItemIds: [weightId, temperatureId]),
                new(2, inspection.Id, 10m, 0m, null, null, null, null, null),
                new(3, heating.Id, 20m, 0m, null, null, null, null, null, ControlItemIds: [reheatId]),
            })).EnsureSuccessStatusCode();

        var order = await CreateOrderAsync(admin, product.Id);
        await admin.PostAsync($"/api/manufacturing-orders/{order.Id}/approve", null);
        var expanded = await admin.PostAsJsonAsync(
            $"/api/manufacturing-orders/{order.Id}/expand", new ExpandRequest(null));
        expanded.EnsureSuccessStatusCode();
        var detail = await expanded.Content.ReadFromJsonAsync<ManufacturingOrderDetailResponse>();
        async Task<List<WorkOrderControlItemResponse>> ItemsOfAsync(ManufacturingOrderDetailResponse d, int sequence) =>
            (await admin.GetFromJsonAsync<List<WorkOrderControlItemResponse>>(
                $"/api/work-orders/{d.WorkOrders.Single(w => w.RoutingSequence == sequence).Id}/control-items"))!;

        var items = await ItemsOfAsync(detail!, 1);
        Assert.Equal(["CI-01", "CI-02"], items.Select(i => i.ItemCode));
        Assert.Empty(await ItemsOfAsync(detail!, 2));
        Assert.Equal(["CI-03"], (await ItemsOfAsync(detail!, 3)).Select(i => i.ItemCode));
        var workOrder = detail!.WorkOrders.Single(w => w.RoutingSequence == 1);
        var temperature = items!.Single(i => i.ItemCode == "CI-02");
        Assert.Equal(180m, temperature.TargetValue);
        Assert.Equal(175m, temperature.LowerLimit);
        Assert.Equal(1, temperature.ItemVersion);
        Assert.Equal("℃", temperature.Unit);

        // マスタを改訂しても展開済みの指示は変わらない（Spec.md 5.7）
        (await admin.PutAsJsonAsync($"/api/control-items/{temperatureId}",
            new ControlItemRequest("CI-02", "加熱温度", "℃", 200m, 195m, 205m)))
            .EnsureSuccessStatusCode();
        var afterRevision = await admin.GetFromJsonAsync<List<WorkOrderControlItemResponse>>(
            $"/api/work-orders/{workOrder.Id}/control-items");
        var kept = afterRevision!.Single(i => i.ItemCode == "CI-02");
        Assert.Equal(180m, kept.TargetValue);
        Assert.Equal(1, kept.ItemVersion);

        // 改訂後に展開した指図には新しい条件が写る
        var next = await CreateOrderAsync(admin, product.Id);
        await admin.PostAsync($"/api/manufacturing-orders/{next.Id}/approve", null);
        var nextExpanded = await admin.PostAsJsonAsync(
            $"/api/manufacturing-orders/{next.Id}/expand", new ExpandRequest(null));
        var nextDetail = await nextExpanded.Content.ReadFromJsonAsync<ManufacturingOrderDetailResponse>();
        var nextItems = await ItemsOfAsync(nextDetail!, 1);
        Assert.Equal(200m, nextItems.Single(i => i.ItemCode == "CI-02").TargetValue);
        Assert.Equal(2, nextItems.Single(i => i.ItemCode == "CI-02").ItemVersion);
    }

    [Fact]
    public async Task 作業手順書は改訂が仕掛中の作業指示にも届き計画時の版数と食い違えば改訂ありになる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var product = await MasterTests.CreateProductAsync(admin, "FG-01", "完成品", ProductType.Product);
        var process = await MasterTests.CreateProcessAsync(admin, "PR-01", "組立");

        var created = await admin.PostAsJsonAsync("/api/work-procedures",
            new WorkProcedureRequest("SOP-01", "組立作業手順", "1. 部材を並べる", null));
        created.EnsureSuccessStatusCode();
        var procedure = (await created.Content.ReadFromJsonAsync<WorkProcedureResponse>())!;

        // 1工程目だけ手順書を紐付ける
        var routing = await admin.PutAsJsonAsync($"/api/products/{product.Id}/routing",
            new List<RoutingStepRequest>
            {
                new(1, process.Id, 30m, 10m, null, null, null, null, null, null, null, procedure.Id),
                new(2, process.Id, 15m, 5m, null, null, null, null, null),
            });
        routing.EnsureSuccessStatusCode();

        var order = await CreateOrderAsync(admin, product.Id);
        await admin.PostAsync($"/api/manufacturing-orders/{order.Id}/approve", null);
        var expanded = await admin.PostAsJsonAsync(
            $"/api/manufacturing-orders/{order.Id}/expand", new ExpandRequest(null));
        expanded.EnsureSuccessStatusCode();
        var detail = (await expanded.Content.ReadFromJsonAsync<ManufacturingOrderDetailResponse>())!;
        var first = detail.WorkOrders.Single(w => w.RoutingSequence == 1);
        var second = detail.WorkOrders.Single(w => w.RoutingSequence == 2);

        var shown = await admin.GetFromJsonAsync<WorkOrderProcedureResponse>(
            $"/api/work-orders/{first.Id}/procedure");
        Assert.Equal("SOP-01", shown!.ProcedureNo);
        Assert.Equal("1. 部材を並べる", shown.Steps);
        Assert.Equal(1, shown.CurrentVersion);
        Assert.Equal(1, shown.PlannedVersion);
        Assert.False(shown.IsRevised);

        // 手順書が紐付いていない工程は404（工順に登録が無い運用でも画面は開ける）
        var none = await admin.GetAsync($"/api/work-orders/{second.Id}/procedure");
        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);

        // 改訂は仕掛中の作業指示にも届く（本文はマスタの現在値。製造条件の固定とは前提が違う）
        var revised = await admin.PutAsJsonAsync($"/api/work-procedures/{procedure.Id}",
            new WorkProcedureRequest("SOP-01", "組立作業手順", "1. 部材を並べる／2. 規定トルクで締結する", null));
        revised.EnsureSuccessStatusCode();

        var afterRevision = await admin.GetFromJsonAsync<WorkOrderProcedureResponse>(
            $"/api/work-orders/{first.Id}/procedure");
        Assert.Equal("1. 部材を並べる／2. 規定トルクで締結する", afterRevision!.Steps);
        Assert.Equal(2, afterRevision.CurrentVersion);
        // 計画時の版数は展開時のまま。食い違いを「改訂あり」として示す
        Assert.Equal(1, afterRevision.PlannedVersion);
        Assert.True(afterRevision.IsRevised);

        // 工順から手順書を外しても、展開済みの作業指示の紐付けは変わらない（Spec.md 5.7）
        var unlinked = await admin.PutAsJsonAsync($"/api/products/{product.Id}/routing",
            new List<RoutingStepRequest> { new(1, process.Id, 30m, 10m, null, null, null, null, null) });
        unlinked.EnsureSuccessStatusCode();
        var stillLinked = await admin.GetFromJsonAsync<WorkOrderProcedureResponse>(
            $"/api/work-orders/{first.Id}/procedure");
        Assert.Equal("SOP-01", stillLinked!.ProcedureNo);

        // 外した後なら手順書を無効化でき、作業指示側では無効と分かる
        (await admin.DeleteAsync($"/api/work-procedures/{procedure.Id}")).EnsureSuccessStatusCode();
        var deactivated = await admin.GetFromJsonAsync<WorkOrderProcedureResponse>(
            $"/api/work-orders/{first.Id}/procedure");
        Assert.False(deactivated!.IsActive);
    }

    [Fact]
    public async Task 工順の作業区が展開時に固定され進捗を上位の段でまとめて集計できる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);

        var plant = await MasterTests.CreateWorkCenterAsync(admin, "P1", "第一工場", WorkCenterLevel.Plant, null);
        var line = await MasterTests.CreateWorkCenterAsync(admin, "L1", "組立1ライン", WorkCenterLevel.Line, plant.Id);
        var area = await MasterTests.CreateWorkCenterAsync(admin, "A1", "前工程エリア", WorkCenterLevel.Area, line.Id);
        var wc = await MasterTests.CreateWorkCenterAsync(admin, "WC01", "溶接作業区", WorkCenterLevel.WorkCenter, area.Id);

        var product = await MasterTests.CreateProductAsync(admin, "FG-01", "完成品", ProductType.Product);
        var process = await MasterTests.CreateProcessAsync(admin, "PR-01", "組立");
        // 1工程目だけ作業区を指定する
        var routing = await admin.PutAsJsonAsync($"/api/products/{product.Id}/routing",
            new List<RoutingStepRequest>
            {
                new(1, process.Id, 30m, 10m, null, null, null, null, null, wc.Id),
                new(2, process.Id, 15m, 5m, null, null, null, null, null),
            });
        routing.EnsureSuccessStatusCode();

        // 工順の作業区は最下段のみ（設備と同じ条件）
        var wrongLevel = await admin.PutAsJsonAsync($"/api/products/{product.Id}/routing",
            new List<RoutingStepRequest> { new(1, process.Id, 30m, 10m, null, null, null, null, null, line.Id) });
        Assert.Equal(HttpStatusCode.BadRequest, wrongLevel.StatusCode);

        var order = await CreateOrderAsync(admin, product.Id);
        await admin.PostAsync($"/api/manufacturing-orders/{order.Id}/approve", null);
        var expanded = await admin.PostAsJsonAsync(
            $"/api/manufacturing-orders/{order.Id}/expand", new ExpandRequest(null));
        expanded.EnsureSuccessStatusCode();

        // 作業区を指定した工程だけが集計に乗る
        var byWorkCenter = await admin.GetFromJsonAsync<List<ProcessProgressRow>>(
            $"/api/work-orders/process-summary?workCenterId={wc.Id}");
        Assert.Equal(1, byWorkCenter!.Sum(r => r.Created));

        // 上位の段を指定しても配下へ展開されるので同じ件数になる（展開しないと常に0件になる）
        foreach (var ancestor in new[] { area.Id, line.Id, plant.Id })
        {
            var rows = await admin.GetFromJsonAsync<List<ProcessProgressRow>>(
                $"/api/work-orders/process-summary?workCenterId={ancestor}");
            Assert.Equal(1, rows!.Sum(r => r.Created));
        }

        // 絞り込みなしは全件（作業区未設定の工程も含む）
        var all = await admin.GetFromJsonAsync<List<ProcessProgressRow>>("/api/work-orders/process-summary");
        Assert.Equal(2, all!.Sum(r => r.Created));

        // 存在しない作業区は400
        var missing = await admin.GetAsync("/api/work-orders/process-summary?workCenterId=9999");
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        // 工順を改訂しても展開済みの作業指示の作業区は変わらない（Spec.md 5.7）
        var revised = await admin.PutAsJsonAsync($"/api/products/{product.Id}/routing",
            new List<RoutingStepRequest> { new(1, process.Id, 30m, 10m, null, null, null, null, null) });
        revised.EnsureSuccessStatusCode();
        var afterRevision = await admin.GetFromJsonAsync<List<ProcessProgressRow>>(
            $"/api/work-orders/process-summary?workCenterId={wc.Id}");
        Assert.Equal(1, afterRevision!.Sum(r => r.Created));
    }

    [Fact]
    public async Task 指図の作成から承認展開まで通しで動作しロットと作業指示が生成される()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var (productId, _) = await SetupMastersAsync(admin);

        var order = await CreateOrderAsync(admin, productId);
        Assert.Equal(ManufacturingOrderStatus.Draft, order.Status);
        Assert.StartsWith("MO", order.OrderNo);

        // 承認
        var approved = await admin.PostAsync($"/api/manufacturing-orders/{order.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        // 展開：工順2ステップ → 作業指示2件、産出ロット自動採番（品目コード-日付-連番）
        var expanded = await admin.PostAsJsonAsync(
            $"/api/manufacturing-orders/{order.Id}/expand", new ExpandRequest(null));
        Assert.Equal(HttpStatusCode.OK, expanded.StatusCode);
        var detail = await expanded.Content.ReadFromJsonAsync<ManufacturingOrderDetailResponse>();
        Assert.Equal(ManufacturingOrderStatus.Released, detail!.Order.Status);
        Assert.Equal(2, detail.WorkOrders.Count);
        Assert.All(detail.WorkOrders, w => Assert.Equal(WorkOrderStatus.Created, w.Status));
        Assert.Equal(100m, detail.WorkOrders[0].PlannedQuantity);
        Assert.StartsWith("FG-01-", detail.Order.OutputLotNumber);
        Assert.Equal($"{order.OrderNo}-01", detail.WorkOrders[0].WorkOrderNo);
    }

    [Fact]
    public async Task 展開時に工順とMBOMが固定されマスタ改訂の影響を受けない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var (productId, processId) = await SetupMastersAsync(admin); // 工順1: 標準30分/段取り10分
        var material = await MasterTests.CreateProductAsync(admin, "RM-01", "部材", ProductType.Material);
        (await admin.PutAsJsonAsync($"/api/products/{productId}/bom",
            new List<BomItemRequest> { new(material.Id, 2m, MakeOrBuy.InHouse, null) })).EnsureSuccessStatusCode();

        var order = await CreateOrderAsync(admin, productId, 10m);
        (await admin.PostAsync($"/api/manufacturing-orders/{order.Id}/approve", null)).EnsureSuccessStatusCode();
        var expanded = await admin.PostAsJsonAsync(
            $"/api/manufacturing-orders/{order.Id}/expand", new ExpandRequest(null));
        var detail = (await expanded.Content.ReadFromJsonAsync<ManufacturingOrderDetailResponse>())!;

        // 工順スナップショット
        Assert.Equal(30m, detail.WorkOrders[0].StandardWorkMinutes);
        Assert.Equal(10m, detail.WorkOrders[0].StandardSetupMinutes);
        // 予定材料（MBOM × 指図数量）
        var planned = Assert.Single(detail.Materials!);
        Assert.Equal("RM-01", planned.ProductCode);
        Assert.Equal(2m, planned.QuantityPer);
        Assert.Equal(20m, planned.PlannedQuantity);

        // 展開後に工順とMBOMを改訂する
        (await admin.PutAsJsonAsync($"/api/products/{productId}/routing",
            new List<RoutingStepRequest>
            {
                new(1, processId, 99m, 88m, null, null, null, null, null),
                new(2, processId, 15m, 5m, null, null, null, null, null),
            })).EnsureSuccessStatusCode();
        var other = await MasterTests.CreateProductAsync(admin, "RM-02", "別部材", ProductType.Material);
        (await admin.PutAsJsonAsync($"/api/products/{productId}/bom",
            new List<BomItemRequest> { new(other.Id, 3m, MakeOrBuy.InHouse, null) })).EnsureSuccessStatusCode();

        // 既に展開済みの指図は改訂前の条件のまま
        var reloaded = await admin.GetFromJsonAsync<ManufacturingOrderDetailResponse>(
            $"/api/manufacturing-orders/{order.Id}");
        Assert.Equal(30m, reloaded!.WorkOrders[0].StandardWorkMinutes);
        Assert.Equal(10m, reloaded.WorkOrders[0].StandardSetupMinutes);
        var stillPlanned = Assert.Single(reloaded.Materials!);
        Assert.Equal("RM-01", stillPlanned.ProductCode);
        Assert.Equal(20m, stillPlanned.PlannedQuantity);

        // 改訂後に展開した指図は新しい条件になる
        var next = await CreateOrderAsync(admin, productId, 10m);
        (await admin.PostAsync($"/api/manufacturing-orders/{next.Id}/approve", null)).EnsureSuccessStatusCode();
        var nextExpanded = await admin.PostAsJsonAsync(
            $"/api/manufacturing-orders/{next.Id}/expand", new ExpandRequest(null));
        var nextDetail = (await nextExpanded.Content.ReadFromJsonAsync<ManufacturingOrderDetailResponse>())!;
        Assert.Equal(99m, nextDetail.WorkOrders[0].StandardWorkMinutes);
        Assert.Equal("RM-02", Assert.Single(nextDetail.Materials!).ProductCode);
    }

    [Fact]
    public async Task 未承認の指図は展開できない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var (productId, _) = await SetupMastersAsync(admin);
        var order = await CreateOrderAsync(admin, productId);

        var expanded = await admin.PostAsJsonAsync(
            $"/api/manufacturing-orders/{order.Id}/expand", new ExpandRequest(null));
        Assert.Equal(HttpStatusCode.Conflict, expanded.StatusCode);
    }

    [Fact]
    public async Task 工順未登録の品目の指図は展開できない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var product = await MasterTests.CreateProductAsync(admin, "FG-99", "工順なし品", ProductType.Product);
        var order = await CreateOrderAsync(admin, product.Id);
        await admin.PostAsync($"/api/manufacturing-orders/{order.Id}/approve", null);

        var expanded = await admin.PostAsJsonAsync(
            $"/api/manufacturing-orders/{order.Id}/expand", new ExpandRequest(null));
        Assert.Equal(HttpStatusCode.BadRequest, expanded.StatusCode);
    }

    [Fact]
    public async Task 予定材料は親品目の標準不良率ぶん割り増される()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);

        // 不良品も部材を使うので、良品10を得るには 2×10÷(1－0.2)=25 の部材が要る（A-40-10-04）
        (await admin.PutAsJsonAsync($"/api/products/{ctx.ProductId}",
            new ProductRequest("FG-01", "完成品", "個", null, ProductType.Product, 20m))).EnsureSuccessStatusCode();
        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);
        Assert.Equal(25m, order.Materials!.Single().PlannedQuantity);

        // 割り切れないときは小数6桁で切り上げる（2×10÷0.7＝28.5714285…）
        (await admin.PutAsJsonAsync($"/api/products/{ctx.ProductId}",
            new ProductRequest("FG-01", "完成品", "個", null, ProductType.Product, 30m))).EnsureSuccessStatusCode();
        var rounded = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);
        Assert.Equal(28.571429m, rounded.Materials!.Single().PlannedQuantity);

        // ÷(1－率) なので100%は受け付けない
        var invalid = await admin.PutAsJsonAsync($"/api/products/{ctx.ProductId}",
            new ProductRequest("FG-01", "完成品", "個", null, ProductType.Product, 100m));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task MBOMの消費工程が工順に無い品目の指図は展開できない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        // 工順は工程順序 1・2。MBOMと工順は別々に改訂できるため、ずれは展開時に止める
        (await admin.PutAsJsonAsync($"/api/products/{ctx.ProductId}/bom", new List<BomItemRequest>
        {
            new(ctx.MaterialId, Phase3TestData.BomQuantityPer, MakeOrBuy.InHouse, null, RoutingSequence: 3),
        })).EnsureSuccessStatusCode();
        var order = await CreateOrderAsync(admin, ctx.ProductId);
        await admin.PostAsync($"/api/manufacturing-orders/{order.Id}/approve", null);

        var expanded = await admin.PostAsJsonAsync(
            $"/api/manufacturing-orders/{order.Id}/expand", new ExpandRequest(null));
        Assert.Equal(HttpStatusCode.BadRequest, expanded.StatusCode);
        Assert.Contains("RM-01", await expanded.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task 承認済みの指図を変更すると未承認に戻る()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var (productId, _) = await SetupMastersAsync(admin);
        var order = await CreateOrderAsync(admin, productId);
        await admin.PostAsync($"/api/manufacturing-orders/{order.Id}/approve", null);

        var updated = await admin.PutAsJsonAsync($"/api/manufacturing-orders/{order.Id}",
            new UpdateManufacturingOrderRequest(200m, null, "数量変更"));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var body = await updated.Content.ReadFromJsonAsync<ManufacturingOrderResponse>();
        Assert.Equal(ManufacturingOrderStatus.Draft, body!.Status);
        Assert.Equal(200m, body.Quantity);
        Assert.Null(body.ApprovedByUserId);
    }

    [Fact]
    public async Task 差立はスキル照合で拒否されスキル付与後に成功する()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);

        // 必要スキル付きの工順
        var skillCreated = await admin.PostAsJsonAsync("/api/skills",
            new SkillRequest("SK-01", "組立資格", SkillType.Certification, true));
        var skill = await skillCreated.Content.ReadFromJsonAsync<SkillResponse>();
        var (productId, _) = await SetupMastersAsync(admin, requiredSkillId: skill!.Id);

        // 作業者（スキル未保有）
        var userCreated = await admin.PostAsJsonAsync("/api/users",
            new CreateUserRequest("worker1", "Passw0rd123", "作業者1", [MesRoles.Operator]));
        var worker = await userCreated.Content.ReadFromJsonAsync<UserSummaryResponse>();

        var order = await CreateOrderAsync(admin, productId);
        await admin.PostAsync($"/api/manufacturing-orders/{order.Id}/approve", null);
        var expanded = await admin.PostAsJsonAsync(
            $"/api/manufacturing-orders/{order.Id}/expand", new ExpandRequest(null));
        var detail = await expanded.Content.ReadFromJsonAsync<ManufacturingOrderDetailResponse>();
        var workOrderId = detail!.WorkOrders[0].Id; // 工順1（必要スキルあり）

        // スキル未保有 → 400
        var rejected = await admin.PutAsJsonAsync($"/api/work-orders/{workOrderId}/dispatch",
            new DispatchRequest(worker!.Id, null, 1));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        // スキル付与（有効期限内）→ 差立成功
        var future = DateOnly.FromDateTime(DateTime.Today).AddYears(1);
        await admin.PutAsJsonAsync($"/api/users/{worker.Id}/skills",
            new List<UserSkillRequest> { new(skill.Id, null, future) });
        var dispatched = await admin.PutAsJsonAsync($"/api/work-orders/{workOrderId}/dispatch",
            new DispatchRequest(worker.Id, null, 1));
        Assert.Equal(HttpStatusCode.OK, dispatched.StatusCode);
        var workOrder = await dispatched.Content.ReadFromJsonAsync<WorkOrderResponse>();
        Assert.Equal(WorkOrderStatus.Dispatched, workOrder!.Status);
        Assert.Equal(worker.Id, workOrder.AssignedUserId);

        // 必要スキルのない工順2は照合なしで差立できる
        var second = await admin.PutAsJsonAsync($"/api/work-orders/{detail.WorkOrders[1].Id}/dispatch",
            new DispatchRequest(worker.Id, null, 2));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task 着手は着手する者のスキルを照合し差立を省略しても通さない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var skillCreated = await admin.PostAsJsonAsync("/api/skills",
            new SkillRequest("SK-01", "組立資格", SkillType.Certification, true));
        var skill = await skillCreated.Content.ReadFromJsonAsync<SkillResponse>();
        var (productId, _) = await SetupMastersAsync(admin, requiredSkillId: skill!.Id);

        using var qualified = await TestAuth.CreateUserClientAsync(factory, admin, "worker1", "Passw0rd123", MesRoles.Operator);
        using var unqualified = await TestAuth.CreateUserClientAsync(factory, admin, "worker2", "Passw0rd123", MesRoles.Operator);
        var users = await admin.GetFromJsonAsync<List<UserSummaryResponse>>("/api/users");
        var worker1 = users!.Single(u => u.UserName == "worker1");
        var worker2 = users!.Single(u => u.UserName == "worker2");
        var today = DateOnly.FromDateTime(DateTime.Today);
        await admin.PutAsJsonAsync($"/api/users/{worker1.Id}/skills",
            new List<UserSkillRequest> { new(skill.Id, null, today.AddYears(1)) });
        // worker2 は期限切れの資格だけを持つ
        await admin.PutAsJsonAsync($"/api/users/{worker2.Id}/skills",
            new List<UserSkillRequest> { new(skill.Id, null, today.AddDays(-10)) });

        var order = await CreateOrderAsync(admin, productId);
        await admin.PostAsync($"/api/manufacturing-orders/{order.Id}/approve", null);
        var expanded = await admin.PostAsJsonAsync(
            $"/api/manufacturing-orders/{order.Id}/expand", new ExpandRequest(null));
        var detail = await expanded.Content.ReadFromJsonAsync<ManufacturingOrderDetailResponse>();
        var skilledStep = detail!.WorkOrders[0].Id; // 工順1（必要スキルあり）

        // 差立を省略して、スキルを持たない管理者が着手 → 400
        var byAdmin = await admin.PostAsync($"/api/work-orders/{skilledStep}/start", null);
        Assert.Equal(HttpStatusCode.BadRequest, byAdmin.StatusCode);

        // worker1 に差立しても、期限切れの worker2 が着手すれば 400
        (await admin.PutAsJsonAsync($"/api/work-orders/{skilledStep}/dispatch",
            new DispatchRequest(worker1.Id, null, 1))).EnsureSuccessStatusCode();
        var byExpired = await unqualified.PostAsync($"/api/work-orders/{skilledStep}/start", null);
        Assert.Equal(HttpStatusCode.BadRequest, byExpired.StatusCode);
        var problem = await byExpired.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Contains("有効期限切れ", problem!.Title);

        // 資格のある worker1 は着手できる
        var byQualified = await qualified.PostAsync($"/api/work-orders/{skilledStep}/start", null);
        Assert.Equal(HttpStatusCode.NoContent, byQualified.StatusCode);

        // 必要スキルのない工順2は誰でも着手できる
        var free = await admin.PostAsync($"/api/work-orders/{detail.WorkOrders[1].Id}/start", null);
        Assert.Equal(HttpStatusCode.NoContent, free.StatusCode);
    }

    [Fact]
    public async Task 進捗一覧で納期遅延を検出できる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var (productId, _) = await SetupMastersAsync(admin);

        var overdue = await CreateOrderAsync(admin, productId,
            dueDate: DateOnly.FromDateTime(DateTime.Today).AddDays(-3));
        var onTime = await CreateOrderAsync(admin, productId,
            dueDate: DateOnly.FromDateTime(DateTime.Today).AddDays(30));

        var progress = await admin.GetFromJsonAsync<List<OrderProgressResponse>>(
            "/api/manufacturing-orders/progress");
        Assert.Contains(progress!, p => p.Id == overdue.Id && p.IsOverdue);
        Assert.Contains(progress!, p => p.Id == onTime.Id && !p.IsOverdue);

        var overdueOnly = await admin.GetFromJsonAsync<List<OrderProgressResponse>>(
            "/api/manufacturing-orders/progress?overdueOnly=true");
        Assert.Single(overdueOnly!);
        Assert.Equal(overdue.Id, overdueOnly![0].Id);
    }

    [Fact]
    public async Task 指図取消で未完了の作業指示も取消される()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var (productId, _) = await SetupMastersAsync(admin);
        var order = await CreateOrderAsync(admin, productId);
        await admin.PostAsync($"/api/manufacturing-orders/{order.Id}/approve", null);
        await admin.PostAsJsonAsync($"/api/manufacturing-orders/{order.Id}/expand", new ExpandRequest(null));

        var canceled = await admin.PostAsync($"/api/manufacturing-orders/{order.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, canceled.StatusCode);

        var detail = await admin.GetFromJsonAsync<ManufacturingOrderDetailResponse>(
            $"/api/manufacturing-orders/{order.Id}");
        Assert.Equal(ManufacturingOrderStatus.Canceled, detail!.Order.Status);
        Assert.All(detail.WorkOrders, w => Assert.Equal(WorkOrderStatus.Canceled, w.Status));
    }

    [Fact]
    public async Task 作業者ロールは指図を作成できない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var (productId, _) = await SetupMastersAsync(admin);
        using var operator_ = await TestAuth.CreateUserClientAsync(
            factory, admin, "operator1", "Passw0rd123", MesRoles.Operator);

        var response = await operator_.PostAsJsonAsync("/api/manufacturing-orders",
            new CreateManufacturingOrderRequest(productId, 10m, null, ManufacturingOrderType.Normal, null, null));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // 参照は可能（B-10-30-03 指示内容の閲覧）
        var read = await operator_.GetAsync("/api/work-orders");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task 作業指示の選択肢は検索で絞り込める()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);

        var all = await admin.GetFromJsonAsync<OptionsResult<WorkOrderResponse>>("/api/work-orders/options");
        Assert.Equal(order.WorkOrders.Count, all!.Items.Count);
        Assert.False(all.Truncated);

        // 指示番号の部分一致
        var byNo = await admin.GetFromJsonAsync<OptionsResult<WorkOrderResponse>>(
            $"/api/work-orders/options?q={order.WorkOrders[0].WorkOrderNo}");
        Assert.Equal(order.WorkOrders[0].Id, Assert.Single(byNo!.Items).Id);

        // 工程コードでも引ける
        var byProcess = await admin.GetFromJsonAsync<OptionsResult<WorkOrderResponse>>(
            "/api/work-orders/options?q=PR-01");
        Assert.Equal(order.WorkOrders.Count, byProcess!.Items.Count);

        var limited = await admin.GetFromJsonAsync<OptionsResult<WorkOrderResponse>>(
            "/api/work-orders/options?limit=1");
        Assert.Single(limited!.Items);
        Assert.True(limited.Truncated);
    }

    [Fact]
    public async Task 工程別サマリは状態ごとの件数をDB側で数える()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);

        var summary = await admin.GetFromJsonAsync<List<ProcessProgressRow>>("/api/work-orders/process-summary");
        var row = Assert.Single(summary!);
        Assert.Equal("PR-01", row.ProcessCode);
        Assert.Equal(order.WorkOrders.Count, row.Created + row.Dispatched + row.Started + row.Completed + row.Approved);

        // 1件着手すると内訳が動く
        (await admin.PostAsync($"/api/work-orders/{order.WorkOrders[0].Id}/start", null)).EnsureSuccessStatusCode();
        var after = await admin.GetFromJsonAsync<List<ProcessProgressRow>>("/api/work-orders/process-summary");
        Assert.Equal(1, Assert.Single(after!).Started);
    }

    [Fact]
    public async Task 生産性モニタリングで歩留まり直行率と標準時間予実を集計できる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);

        // 通常指図10個：良品8・不良2（工順1段目は 作業30分/個・段取り10分）
        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);
        var workOrder = order.WorkOrders.First();
        var started = DateTimeOffset.Now.AddHours(-2);
        (await admin.PostAsJsonAsync($"/api/work-orders/{workOrder.Id}/production-records",
                new ProductionRecordRequest(8m, 2m, started, started.AddHours(1), ctx.ProductLocationId, false)))
            .EnsureSuccessStatusCode();

        // 直接作業時間60分（予定は 段取り10 + 作業30×10 = 310分）
        (await admin.PostAsJsonAsync("/api/work-time-records",
                new WorkTimeRequest(WorkTimeType.Direct, null, workOrder.Id, started, started.AddMinutes(60), null)))
            .EnsureSuccessStatusCode();

        // リワーク指図で不良2個を救済する（別指図なので分母には入らない）
        var reworkCreated = await admin.PostAsJsonAsync("/api/manufacturing-orders",
            new CreateManufacturingOrderRequest(ctx.ProductId, 2m, null,
                ManufacturingOrderType.Rework, order.Order.Id, null));
        reworkCreated.EnsureSuccessStatusCode();
        var rework = (await reworkCreated.Content.ReadFromJsonAsync<ManufacturingOrderResponse>())!;
        (await admin.PostAsync($"/api/manufacturing-orders/{rework.Id}/approve", null)).EnsureSuccessStatusCode();
        var reworkExpanded = await admin.PostAsJsonAsync(
            $"/api/manufacturing-orders/{rework.Id}/expand", new ExpandRequest(null));
        reworkExpanded.EnsureSuccessStatusCode();
        var reworkDetail = (await reworkExpanded.Content.ReadFromJsonAsync<ManufacturingOrderDetailResponse>())!;
        (await admin.PostAsJsonAsync(
                $"/api/work-orders/{reworkDetail.WorkOrders.First().Id}/production-records",
                new ProductionRecordRequest(2m, 0m, started, started.AddHours(1), ctx.ProductLocationId, false)))
            .EnsureSuccessStatusCode();

        var summary = await admin.GetFromJsonAsync<ProductivitySummaryResponse>("/api/productivity");

        // 直行率は手直しを経ずに通った割合＝8/10、歩留まりは救済を含めて(8+2)/10
        Assert.Equal(8m, summary!.Total.GoodQuantity);
        Assert.Equal(2m, summary.Total.DefectQuantity);
        Assert.Equal(2m, summary.Total.ReworkGoodQuantity);
        Assert.Equal(80m, summary.Total.FirstPassRate);
        Assert.Equal(100m, summary.Total.YieldRate);

        var product = Assert.Single(summary.ByProduct);
        Assert.Equal("FG-01", product.Key);
        Assert.Equal(80m, product.FirstPassRate);

        // 標準時間の予実：予定310分に対し実績60分
        var variance = Assert.Single(summary.TimeVariances, v => v.WorkOrderNo == workOrder.WorkOrderNo);
        Assert.Equal(310m, variance.PlannedMinutes);
        Assert.Equal(60m, variance.ActualMinutes);
        Assert.Equal(-80.65m, variance.VarianceRate);

        // 期間外を指定すれば空になる（期間は製造日基準）
        var empty = await admin.GetFromJsonAsync<ProductivitySummaryResponse>(
            "/api/productivity?from=2020-01-01&to=2020-01-01");
        Assert.Equal(0m, empty!.Total.GoodQuantity);
        Assert.Empty(empty.ByProduct);
        Assert.Empty(empty.TimeVariances);
    }

    [Fact]
    public async Task 製造リードタイムは作業の記録の時刻から製造日で数え完了した指図だけを分布にする()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        // 製造日の境界に掛からないよう正午で組む（工場のタイムゾーンは既定でOSのローカル）
        static DateTimeOffset Noon(int day) => new(new DateTime(2026, 6, day, 12, 0, 0, DateTimeKind.Local));

        async Task<ManufacturingOrderDetailResponse> OrderAsync(DateOnly? due)
        {
            var created = await admin.PostAsJsonAsync("/api/manufacturing-orders",
                new CreateManufacturingOrderRequest(ctx.ProductId, 10m, due, ManufacturingOrderType.Normal, null, null));
            var order = (await created.Content.ReadFromJsonAsync<ManufacturingOrderResponse>())!;
            (await admin.PostAsync($"/api/manufacturing-orders/{order.Id}/approve", null)).EnsureSuccessStatusCode();
            var expanded = await admin.PostAsJsonAsync($"/api/manufacturing-orders/{order.Id}/expand", new ExpandRequest(null));
            return (await expanded.Content.ReadFromJsonAsync<ManufacturingOrderDetailResponse>())!;
        }
        async Task RecordAsync(int workOrderId, DateTimeOffset start, int? locationId = null) =>
            (await admin.PostAsJsonAsync($"/api/work-orders/{workOrderId}/production-records",
                new ProductionRecordRequest(10m, 0m, start, start.AddHours(1), locationId, false)))
            .EnsureSuccessStatusCode();

        // A：6/1着手 → 6/3完了（2日）。納期6/2なので1日遅れ
        var a = await OrderAsync(new DateOnly(2026, 6, 2));
        await RecordAsync(a.WorkOrders[0].Id, Noon(1));
        await RecordAsync(a.WorkOrders[1].Id, Noon(3), ctx.ProductLocationId);
        // B：前段取りを6/4に始め、実績は6/5（段取りの開始から数えて1日）。納期なし
        var b = await OrderAsync(null);
        (await admin.PostAsJsonAsync($"/api/work-orders/{b.WorkOrders[0].Id}/setup-records",
            new SetupRecordRequest(SetupType.Pre, Noon(4), Noon(4).AddHours(1), null))).EnsureSuccessStatusCode();
        await RecordAsync(b.WorkOrders[0].Id, Noon(5));
        await RecordAsync(b.WorkOrders[1].Id, Noon(5), ctx.ProductLocationId);
        // C：2工程目が未完了なので対象外
        var c = await OrderAsync(null);
        await RecordAsync(c.WorkOrders[0].Id, Noon(1));

        var result = await admin.GetFromJsonAsync<LeadTimeResponse>("/api/productivity/lead-time");

        Assert.Equal(2, result!.OrderCount);
        Assert.DoesNotContain(result.Orders, o => o.OrderNo == c.Order.OrderNo);
        var rowA = Assert.Single(result.Orders, o => o.OrderNo == a.Order.OrderNo);
        Assert.Equal(2, rowA.LeadTimeDays);
        Assert.Equal(1, rowA.DelayDays);
        Assert.Equal(new DateOnly(2026, 6, 3), rowA.CompletedOn);
        var rowB = Assert.Single(result.Orders, o => o.OrderNo == b.Order.OrderNo);
        Assert.Equal(1, rowB.LeadTimeDays);
        Assert.Null(rowB.DelayDays);
        Assert.Equal(a.Order.OrderNo, result.Orders[0].OrderNo); // 長い順
        Assert.Equal(1, result.LateCount);
        Assert.Equal(1, result.NoDueDateCount);
        Assert.Equal(1.5m, result.MedianDays);
        Assert.Null(result.OutlierThresholdDays); // 4件未満では異常値を判定しない
        // 0日〜最大2日を件数0の日も含めて並べる
        Assert.Equal([0, 1, 2], result.Distribution.Select(d => d.Days));
        Assert.Equal([0, 1, 1], result.Distribution.Select(d => d.Count));
        Assert.Equal(1, result.Distribution[2].LateCount);

        // 期間は完了の製造日で絞る（Aは6/3完了、Bは6/5完了）
        var onlyA = await admin.GetFromJsonAsync<LeadTimeResponse>("/api/productivity/lead-time?from=2026-06-01&to=2026-06-04");
        Assert.Equal(a.Order.OrderNo, Assert.Single(onlyA!.Orders).OrderNo);

        // 0日を3件と10日を1件足すと [0,0,0,1,2,10]。Q1=0・Q3=2 からしきい値 2+1.5×2=5 日を超える10日が異常値
        for (var i = 0; i < 3; i++)
        {
            var same = await OrderAsync(null);
            await RecordAsync(same.WorkOrders[0].Id, Noon(10));
            await RecordAsync(same.WorkOrders[1].Id, Noon(10), ctx.ProductLocationId);
        }
        var slow = await OrderAsync(null);
        await RecordAsync(slow.WorkOrders[0].Id, Noon(11));
        await RecordAsync(slow.WorkOrders[1].Id, Noon(21), ctx.ProductLocationId);

        var withOutlier = await admin.GetFromJsonAsync<LeadTimeResponse>("/api/productivity/lead-time");
        Assert.Equal(5m, withOutlier!.OutlierThresholdDays);
        var outlier = Assert.Single(withOutlier.Orders, o => o.IsOutlier);
        Assert.Equal(slow.Order.OrderNo, outlier.OrderNo);
        Assert.Equal(10, withOutlier.MaxDays);
    }

    [Fact]
    public async Task 標準時間の見直し候補は実績の中央値が工順マスタから外れた工程を件数が足りるときだけ挙げる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var started = DateTimeOffset.Now.AddHours(-12);

        // 工順1段目（標準 作業30分/個）を3指図で、1個あたり60分・60分・66分で行う → 中央値60分（+100%）
        // 工順2段目は1指図だけ時間を記録する（件数不足で候補にしない）。時間を記録しない作業指示は件数に入れない
        var minutes = new[] { 600, 600, 660 };
        for (var i = 0; i < 3; i++)
        {
            var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);
            var first = order.WorkOrders[0].Id;
            (await admin.PostAsJsonAsync($"/api/work-orders/{first}/production-records",
                new ProductionRecordRequest(10m, 0m, started, started.AddHours(1), null, false))).EnsureSuccessStatusCode();
            (await admin.PostAsJsonAsync("/api/work-time-records",
                new WorkTimeRequest(WorkTimeType.Direct, null, first, started, started.AddMinutes(minutes[i]), null)))
                .EnsureSuccessStatusCode();
            var second = order.WorkOrders[1].Id;
            (await admin.PostAsJsonAsync($"/api/work-orders/{second}/production-records",
                new ProductionRecordRequest(10m, 0m, started, started.AddHours(1), ctx.ProductLocationId, false))).EnsureSuccessStatusCode();
            if (i == 0)
            {
                (await admin.PostAsJsonAsync("/api/work-time-records",
                    new WorkTimeRequest(WorkTimeType.Direct, null, second, started, started.AddMinutes(500), null)))
                    .EnsureSuccessStatusCode();
            }
        }

        var review = await admin.GetFromJsonAsync<StandardTimeReviewResponse>("/api/productivity/standard-time-review");

        var step1 = Assert.Single(review!.Rows, r => r.Sequence == 1);
        Assert.True(step1.IsCandidate);
        Assert.Equal(3, step1.WorkSampleCount);
        Assert.Equal(60m, step1.MedianWorkMinutes);
        Assert.Equal(100m, step1.WorkDeviationRate);
        var step2 = Assert.Single(review.Rows, r => r.Sequence == 2);
        Assert.False(step2.IsCandidate);
        Assert.Equal(1, step2.WorkSampleCount);
        Assert.Equal(step1.RoutingId, review.Rows[0].RoutingId); // 候補が先

        // しきい値を上げれば候補から外れる。マスタは書き換えない
        var loose = await admin.GetFromJsonAsync<StandardTimeReviewResponse>(
            "/api/productivity/standard-time-review?threshold=150");
        Assert.DoesNotContain(loose!.Rows, r => r.IsCandidate);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync("/api/productivity/standard-time-review?threshold=0")).StatusCode);
    }

    [Fact]
    public async Task 納期超過と標準時間超過の作業指示を遅延として拾える()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);

        // 納期が過去の指図（工順1段目は 作業30分/個・段取り10分）。
        // 超過日数はAPIが製造日で数えるので、今日も製造日で取る（暦日だと境界時刻前の実行で1日ずれる）
        var today = (await admin.GetFromJsonAsync<BusinessDateResponse>("/api/business-date"))!.Today;
        var overdue = await admin.PostAsJsonAsync("/api/manufacturing-orders",
            new CreateManufacturingOrderRequest(ctx.ProductId, 10m,
                today.AddDays(-3),
                ManufacturingOrderType.Normal, null, null));
        overdue.EnsureSuccessStatusCode();
        var overdueOrder = (await overdue.Content.ReadFromJsonAsync<ManufacturingOrderResponse>())!;
        (await admin.PostAsync($"/api/manufacturing-orders/{overdueOrder.Id}/approve", null))
            .EnsureSuccessStatusCode();
        var overdueExpanded = await admin.PostAsJsonAsync(
            $"/api/manufacturing-orders/{overdueOrder.Id}/expand", new ExpandRequest(null));
        overdueExpanded.EnsureSuccessStatusCode();
        var overdueDetail = (await overdueExpanded.Content
            .ReadFromJsonAsync<ManufacturingOrderDetailResponse>())!;

        var delays = await admin.GetFromJsonAsync<List<WorkOrderDelayRow>>("/api/work-orders/delays");
        var overdueRow = Assert.Single(delays!,
            d => d.WorkOrderNo == overdueDetail.WorkOrders[0].WorkOrderNo);
        Assert.Equal(WorkOrderDelayKind.OverdueDueDate, overdueRow.Kind);
        Assert.Equal(3, overdueRow.OverdueDays);

        // 納期内の指図を着手すると、経過時間が予定（310分）を超えるまでは遅延に出ない
        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);
        var workOrder = order.WorkOrders[0];
        (await admin.PostAsync($"/api/work-orders/{workOrder.Id}/start", null)).EnsureSuccessStatusCode();
        var justStarted = await admin.GetFromJsonAsync<List<WorkOrderDelayRow>>("/api/work-orders/delays");
        Assert.DoesNotContain(justStarted!, d => d.WorkOrderNo == workOrder.WorkOrderNo);

        // しきい値を下げれば着手直後でも拾える（超過率は負なので-100%より上を対象にする）
        var sensitive = await admin.GetFromJsonAsync<List<WorkOrderDelayRow>>(
            "/api/work-orders/delays?overrunPercent=-100");
        var overrunRow = Assert.Single(sensitive!, d => d.WorkOrderNo == workOrder.WorkOrderNo);
        Assert.Equal(WorkOrderDelayKind.OverrunStandardTime, overrunRow.Kind);
        Assert.Equal(310m, overrunRow.PlannedMinutes);
        Assert.NotNull(overrunRow.StartedAt);

        // 実績を報告して完了した作業指示は遅延に出ない
        (await admin.PostAsJsonAsync($"/api/work-orders/{workOrder.Id}/production-records",
                new ProductionRecordRequest(10m, 0m, DateTimeOffset.Now.AddHours(-1), DateTimeOffset.Now,
                    ctx.ProductLocationId, false)))
            .EnsureSuccessStatusCode();
        var afterComplete = await admin.GetFromJsonAsync<List<WorkOrderDelayRow>>(
            "/api/work-orders/delays?overrunPercent=-100");
        Assert.DoesNotContain(afterComplete!, d => d.WorkOrderNo == workOrder.WorkOrderNo);
    }

    [Fact]
    public async Task ダッシュボードは開始時刻の製造日で日週月に区切り軸別に内訳を出す()
    {
        // +09:00 の時刻で製造日の境目を検証するので工場のタイムゾーンを日本に固定する
        using var factory = new ApiFactory(new Dictionary<string, string>
        {
            ["BusinessDay:TimeZone"] = "Asia/Tokyo",
        });
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 1000m, ctx.MaterialLocationId);

        var plant = await MasterTests.CreateWorkCenterAsync(admin, "P1", "第一工場", WorkCenterLevel.Plant, null);
        var line = await MasterTests.CreateWorkCenterAsync(admin, "L1", "組立1ライン", WorkCenterLevel.Line, plant.Id);
        var area = await MasterTests.CreateWorkCenterAsync(admin, "A1", "前工程エリア", WorkCenterLevel.Area, line.Id);
        var wc = await MasterTests.CreateWorkCenterAsync(admin, "WC01", "組立作業区", WorkCenterLevel.WorkCenter, area.Id);
        (await admin.PutAsJsonAsync($"/api/products/{ctx.ProductId}/routing",
            new List<RoutingStepRequest>
            {
                new(1, ctx.ProcessId, 30m, 10m, null, null, null, null, null, wc.Id),
                new(2, ctx.ProcessId, 15m, 5m, null, null, null, null, null, wc.Id),
            })).EnsureSuccessStatusCode();

        var jst = TimeSpan.FromHours(9);
        async Task RecordAsync(DateTime startedAt, decimal good, decimal defect)
        {
            var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);
            (await admin.PostAsJsonAsync($"/api/work-orders/{order.WorkOrders[1].Id}/production-records",
                new ProductionRecordRequest(good, defect, new DateTimeOffset(startedAt, jst), null,
                    ctx.ProductLocationId, false))).EnsureSuccessStatusCode();
        }
        await RecordAsync(new DateTime(2026, 8, 31, 10, 0, 0), 9m, 1m);   // 月曜・8月
        await RecordAsync(new DateTime(2026, 9, 1, 22, 0, 0), 10m, 0m);   // 夜勤
        await RecordAsync(new DateTime(2026, 9, 7, 5, 0, 0), 8m, 2m);     // 6時前なので製造日は 9/6（日曜）
        await RecordAsync(new DateTime(2026, 9, 7, 10, 0, 0), 10m, 0m);   // 翌週の月曜

        var equipment = await admin.PostAsJsonAsync("/api/equipments",
            new EquipmentRequest("EQ-01", "プレス1号", null, EquipmentStatus.Available, MaintenanceType.None,
                null, null, wc.Id));
        equipment.EnsureSuccessStatusCode();
        var equipmentId = (await equipment.Content.ReadFromJsonAsync<EquipmentResponse>())!.Id;
        var logStart = new DateTimeOffset(new DateTime(2026, 9, 1, 8, 0, 0), jst);
        (await admin.PostAsJsonAsync("/api/equipment-logs", new EquipmentLogRequest(equipmentId,
            EquipmentLogStatus.Running, logStart, logStart.AddHours(6), null, null, null))).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/equipment-logs", new EquipmentLogRequest(equipmentId,
            EquipmentLogStatus.Stopped, logStart.AddHours(6), logStart.AddHours(8), "材料待ち", null, null))).EnsureSuccessStatusCode();

        // 日次：夜明け前の実績は前日の製造日に入る
        var daily = await admin.GetFromJsonAsync<DashboardSummaryResponse>(
            "/api/dashboard/trend?from=2026-09-06&to=2026-09-07&unit=Day");
        Assert.Equal([8m, 10m], daily!.Rows.Select(r => r.GoodQuantity!.Value));

        // 週次（月曜始まり）：9/6 の実績は 8/31 の週に入る。データの無い区切りも行として返す
        var weekly = await admin.GetFromJsonAsync<DashboardSummaryResponse>(
            "/api/dashboard/trend?from=2026-08-31&to=2026-09-20&unit=Week");
        Assert.Equal(["2026-08-31", "2026-09-07", "2026-09-14"], weekly!.Rows.Select(r => r.Key));
        Assert.Equal(27m, weekly.Rows[0].GoodQuantity);
        Assert.Equal(10m, weekly.Rows[0].DefectRate);
        Assert.Equal(75m, weekly.Rows[0].UtilizationRate);
        Assert.Equal(0m, weekly.Rows[2].GoodQuantity);
        Assert.Null(weekly.Rows[2].DefectRate);
        Assert.Equal(37m, weekly.Total.GoodQuantity);

        // 月次：期間の端で切り詰める
        var monthly = await admin.GetFromJsonAsync<DashboardSummaryResponse>(
            "/api/dashboard/trend?from=2026-08-31&to=2026-09-07&unit=Month");
        Assert.Equal(2, monthly!.Rows.Count);
        Assert.Equal(new DateOnly(2026, 8, 31), monthly.Rows[0].PeriodStart);
        Assert.Equal(9m, monthly.Rows[0].GoodQuantity);
        Assert.Equal(new DateOnly(2026, 9, 7), monthly.Rows[1].PeriodEnd);
        Assert.Equal(28m, monthly.Rows[1].GoodQuantity);

        // 工程で絞ると稼働は求めない（稼働ログと作業指示の紐付けは任意のため）
        var byProcessFilter = await admin.GetFromJsonAsync<DashboardSummaryResponse>(
            $"/api/dashboard/trend?from=2026-08-31&to=2026-09-07&unit=Week&processId={ctx.ProcessId}");
        Assert.False(byProcessFilter!.UtilizationAvailable);
        Assert.All(byProcessFilter.Rows, r => Assert.Null(r.UtilizationRate));

        // ライン軸：最下段の作業区をラインへまとめ、生産と稼働の両方を出す。工場で絞っても配下が入る
        var byLine = await admin.GetFromJsonAsync<DashboardSummaryResponse>(
            $"/api/dashboard/breakdown?from=2026-08-31&to=2026-09-07&axis=Line&workCenterId={plant.Id}");
        var lineRow = Assert.Single(byLine!.Rows);
        Assert.Equal("L1", lineRow.Key);
        Assert.Equal(37m, lineRow.GoodQuantity);
        Assert.Equal(75m, lineRow.UtilizationRate);

        // 設備軸は稼働だけ、工程軸は生産だけ
        var byEquipment = await admin.GetFromJsonAsync<DashboardSummaryResponse>(
            "/api/dashboard/breakdown?from=2026-08-31&to=2026-09-07&axis=Equipment");
        Assert.False(byEquipment!.ProductionAvailable);
        Assert.Null(Assert.Single(byEquipment.Rows).GoodQuantity);
        var byProcess = await admin.GetFromJsonAsync<DashboardSummaryResponse>(
            "/api/dashboard/breakdown?from=2026-08-31&to=2026-09-07&axis=Process");
        Assert.False(byProcess!.UtilizationAvailable);
        Assert.Equal(37m, Assert.Single(byProcess.Rows).GoodQuantity);

        // 品質分析も登録時刻ではなく開始時刻の製造日で数える（ダッシュボードと食い違わない）
        var quality = await admin.GetFromJsonAsync<QualitySummaryResponse>(
            "/api/quality/summary?from=2026-09-06&to=2026-09-06");
        Assert.Equal(8m, Assert.Single(quality!.ByProduct).GoodQuantity);

        // 期間の逆転・区切りの上限超えは400
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await admin.GetAsync(
            "/api/dashboard/trend?from=2026-09-07&to=2026-09-01")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await admin.GetAsync(
            "/api/dashboard/trend?from=2024-01-01&to=2026-09-01&unit=Day")).StatusCode);

        // 区切りの表示名（曜日・合計・直なし）は表示言語で返す。直なしのキーは並び順に使うので訳さない
        Assert.Equal(["09/06(日)", "09/07(月)"], daily.Rows.Select(r => r.Label));
        admin.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        var dailyEn = await admin.GetFromJsonAsync<DashboardSummaryResponse>(
            "/api/dashboard/trend?from=2026-09-06&to=2026-09-07&unit=Day");
        Assert.Equal(["09/06(Sun)", "09/07(Mon)"], dailyEn!.Rows.Select(r => r.Label));
        Assert.Equal("Total", dailyEn.Total.Label);
        var byShiftEn = await admin.GetFromJsonAsync<DashboardSummaryResponse>(
            "/api/dashboard/breakdown?from=2026-08-31&to=2026-09-07&axis=Shift");
        var noShift = Assert.Single(byShiftEn!.Rows);
        Assert.Equal((ShiftLabels.NoShift, "(No shift)"), (noShift.Key, noShift.Label));
    }

    // ---- 生産計画（A-30-10-01 の「予」。PLAN-01）----

    [Fact]
    public async Task 生産計画を登録し期間と作業区で絞り込める()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var (productId, processId) = await SetupMastersAsync(admin);
        var plant = await MasterTests.CreateWorkCenterAsync(admin, "P1", "第一工場", WorkCenterLevel.Plant, null);
        var line = await MasterTests.CreateWorkCenterAsync(admin, "L1", "組立1ライン", WorkCenterLevel.Line, plant.Id);
        var area = await MasterTests.CreateWorkCenterAsync(admin, "A1", "組立エリア", WorkCenterLevel.Area, line.Id);
        var wc1 = await MasterTests.CreateWorkCenterAsync(admin, "WC01", "組立作業区1", WorkCenterLevel.WorkCenter, area.Id);
        var wc2 = await MasterTests.CreateWorkCenterAsync(admin, "WC02", "組立作業区2", WorkCenterLevel.WorkCenter, area.Id);
        var otherLine = await MasterTests.CreateWorkCenterAsync(admin, "L2", "組立2ライン", WorkCenterLevel.Line, plant.Id);

        var d1 = new DateOnly(2026, 10, 1);
        var d2 = new DateOnly(2026, 10, 2);
        await CreatePlanAsync(admin, new(d2, productId, processId, wc2.Id, 20m, null));
        await CreatePlanAsync(admin, new(d2, productId, processId, wc1.Id, 10m, "初回"));
        await CreatePlanAsync(admin, new(d1, productId, processId, null, 5m, null));
        await CreatePlanAsync(admin, new(new DateOnly(2026, 10, 3), productId, processId, otherLine.Id, 0m, "休止"));

        // 期間は製造日の両端を含み、製造日→品目→工程→作業区の順に並ぶ
        var byPeriod = (await admin.GetFromJsonAsync<List<ProductionPlanResponse>>(
            "/api/production-plans?from=2026-10-01&to=2026-10-02"))!;
        Assert.Equal([(d1, (string?)null), (d2, "WC01"), (d2, "WC02")],
            byPeriod.Select(p => (p.BusinessDate, p.WorkCenterCode)));
        Assert.Equal(("FG-01", "PR-01", "初回"), (byPeriod[1].ProductCode, byPeriod[1].ProcessCode, byPeriod[1].Note));

        // ラインを指定したら配下の作業区の計画も返す（作業区なしの計画と別ラインの計画は入らない）
        var byLine = (await admin.GetFromJsonAsync<List<ProductionPlanResponse>>(
            $"/api/production-plans?workCenterId={line.Id}"))!;
        Assert.Equal(["WC01", "WC02"], byLine.Select(p => p.WorkCenterCode));
        var byPlant = (await admin.GetFromJsonAsync<List<ProductionPlanResponse>>(
            $"/api/production-plans?workCenterId={plant.Id}&processId={processId}&productId={productId}"))!;
        Assert.Equal(3, byPlant.Count);

        // 期間の逆転・存在しない作業区での絞り込みは400
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync("/api/production-plans?from=2026-10-02&to=2026-10-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync("/api/production-plans?workCenterId=9999")).StatusCode);
    }

    [Fact]
    public async Task 生産計画は同じキーの二重登録と存在しない参照と負の数量を拒否する()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var (productId, processId) = await SetupMastersAsync(admin);
        var wc = await MasterTests.CreateWorkCenterAsync(admin, "P1", "第一工場", WorkCenterLevel.Plant, null);
        var date = new DateOnly(2026, 10, 1);

        var withoutWc = await CreatePlanAsync(admin, new(date, productId, processId, null, 10m, null));
        var date2 = date.AddDays(2);
        var withWc = await CreatePlanAsync(admin, new(date2, productId, processId, wc.Id, 10m, null));

        // 作業区なしどうし・同じ作業区どうしはどちらも同じキー（NULL もDBに任せずAPIで止める）
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/production-plans",
            new ProductionPlanRequest(date, productId, processId, null, 3m, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/production-plans",
            new ProductionPlanRequest(date2, productId, processId, wc.Id, 3m, null))).StatusCode);
        // 更新でほかの計画と同じキーへ付け替えるのも409。自分自身のキーのままの更新は通る
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/production-plans/{withWc.Id}",
            new ProductionPlanRequest(date, productId, processId, null, 3m, null))).StatusCode);
        var updated = await admin.PutAsJsonAsync($"/api/production-plans/{withWc.Id}",
            new ProductionPlanRequest(date2, productId, processId, wc.Id, 12m, "改訂"));
        updated.EnsureSuccessStatusCode();
        Assert.Equal(12m, (await updated.Content.ReadFromJsonAsync<ProductionPlanResponse>())!.PlannedQuantity);

        // 存在しない品目・工程・作業区、負の数量は400。0 は計画上の休止として受け付ける
        foreach (var bad in new ProductionPlanRequest[]
                 {
                     new(date, 9999, processId, null, 1m, null),
                     new(date, productId, 9999, null, 1m, null),
                     new(date, productId, processId, 9999, 1m, null),
                     new(date.AddDays(1), productId, processId, null, -1m, null),
                 })
        {
            Assert.Equal(HttpStatusCode.BadRequest,
                (await admin.PostAsJsonAsync("/api/production-plans", bad)).StatusCode);
        }
        var zero = await CreatePlanAsync(admin, new(date.AddDays(1), productId, processId, null, 0m, "休止"));
        Assert.Equal(0m, zero.PlannedQuantity);

        // 削除すると同じキーで登録し直せる
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"/api/production-plans/{withoutWc.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/production-plans/{withoutWc.Id}")).StatusCode);
        await CreatePlanAsync(admin, new(date, productId, processId, null, 8m, null));
    }

    [Fact]
    public async Task 生産計画は作業区の範囲が重なる計画を同じ製造日品目工程に併存させない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var (productId, processId) = await SetupMastersAsync(admin);
        var plant = await MasterTests.CreateWorkCenterAsync(admin, "P1", "第一工場", WorkCenterLevel.Plant, null);
        var line = await MasterTests.CreateWorkCenterAsync(admin, "L1", "組立1ライン", WorkCenterLevel.Line, plant.Id);
        var area = await MasterTests.CreateWorkCenterAsync(admin, "A1", "組立エリア", WorkCenterLevel.Area, line.Id);
        var wc1 = await MasterTests.CreateWorkCenterAsync(admin, "WC01", "組立作業区1", WorkCenterLevel.WorkCenter, area.Id);
        var wc2 = await MasterTests.CreateWorkCenterAsync(admin, "WC02", "組立作業区2", WorkCenterLevel.WorkCenter, area.Id);
        var date = new DateOnly(2026, 10, 1);
        var date2 = date.AddDays(1);

        async Task<HttpStatusCode> PostAsync(DateOnly d, int? workCenterId) =>
            (await admin.PostAsJsonAsync("/api/production-plans",
                new ProductionPlanRequest(d, productId, processId, workCenterId, 1m, null))).StatusCode;

        // 兄弟の作業区どうしは重ならないので併存できる
        var plan1 = await CreatePlanAsync(admin, new(date, productId, processId, wc1.Id, 10m, null));
        await CreatePlanAsync(admin, new(date, productId, processId, wc2.Id, 10m, null));
        // 作業区なし（全体）・上位の段は配下の計画と重なるので409（予実で二重に数えるため）
        Assert.Equal(HttpStatusCode.Conflict, await PostAsync(date, null));
        Assert.Equal(HttpStatusCode.Conflict, await PostAsync(date, line.Id));
        Assert.Equal(HttpStatusCode.Conflict, await PostAsync(date, area.Id));
        // 逆向き（全体の計画が先にある日に作業区ありを足す）も409
        await CreatePlanAsync(admin, new(date2, productId, processId, null, 20m, null));
        Assert.Equal(HttpStatusCode.Conflict, await PostAsync(date2, wc1.Id));
        // 更新で重なるキーへ付け替えるのも409。理由には重なる相手の作業区が出る
        var moved = await admin.PutAsJsonAsync($"/api/production-plans/{plan1.Id}",
            new ProductionPlanRequest(date2, productId, processId, wc1.Id, 10m, null));
        Assert.Equal(HttpStatusCode.Conflict, moved.StatusCode);
        Assert.Contains("WC01", await moved.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task 生産計画の参照は誰でもでき書き込みにはロールが要り監査ログが残る()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var (productId, processId) = await SetupMastersAsync(admin);
        using var operator_ = await TestAuth.CreateUserClientAsync(
            factory, admin, "operator1", "Passw0rd123", MesRoles.Operator);
        using var manager = await TestAuth.CreateUserClientAsync(
            factory, admin, "manager1", "Passw0rd123", MesRoles.ProductionManager);
        var date = new DateOnly(2026, 10, 1);

        var plan = await CreatePlanAsync(manager, new(date, productId, processId, null, 10m, null));
        Assert.Equal(HttpStatusCode.OK, (await operator_.GetAsync("/api/production-plans")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await operator_.GetAsync($"/api/production-plans/{plan.Id}")).StatusCode);

        var request = new ProductionPlanRequest(date.AddDays(1), productId, processId, null, 1m, null);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await operator_.PostAsJsonAsync("/api/production-plans", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await operator_.PutAsJsonAsync($"/api/production-plans/{plan.Id}", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await operator_.DeleteAsync($"/api/production-plans/{plan.Id}")).StatusCode);

        (await manager.PutAsJsonAsync($"/api/production-plans/{plan.Id}",
            new ProductionPlanRequest(date, productId, processId, null, 15m, null))).EnsureSuccessStatusCode();
        (await manager.DeleteAsync($"/api/production-plans/{plan.Id}")).EnsureSuccessStatusCode();

        var logs = (await admin.GetFromJsonAsync<PagedResult<AuditLogResponse>>(
            $"/api/audit-logs?targetType={nameof(ProductionPlan)}&targetId={plan.Id}"))!;
        Assert.Equal(["Delete", "Update", "Create"], logs.Items.Select(a => a.Action));
        Assert.All(logs.Items, a => Assert.Equal("Planning", a.Category));
    }

    [Fact]
    public async Task 予実は開始時刻の製造日で実績を振り分けリワーク指図の産出を数えない()
    {
        // +09:00 の時刻で製造日の境目を検証するので工場のタイムゾーンを日本に固定する
        using var factory = new ApiFactory(new Dictionary<string, string>
        {
            ["BusinessDay:TimeZone"] = "Asia/Tokyo",
        });
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 1000m, ctx.MaterialLocationId);
        var plant1 = await MasterTests.CreateWorkCenterAsync(admin, "P1", "第一工場", WorkCenterLevel.Plant, null);
        var plant2 = await MasterTests.CreateWorkCenterAsync(admin, "P2", "第二工場", WorkCenterLevel.Plant, null);

        var d1 = new DateOnly(2026, 9, 1);
        var d2 = new DateOnly(2026, 9, 2);
        var d3 = new DateOnly(2026, 9, 3);
        // 作業区違い（重ならない作業区どうし）の計画は1行に合算する。計画0（休止）の日は実績が無くても行になる
        await CreatePlanAsync(admin, new(d1, ctx.ProductId, ctx.ProcessId, plant1.Id, 20m, null));
        await CreatePlanAsync(admin, new(d1, ctx.ProductId, ctx.ProcessId, plant2.Id, 5m, null));
        await CreatePlanAsync(admin, new(d2, ctx.ProductId, ctx.ProcessId, null, 0m, "休止"));

        var jst = TimeSpan.FromHours(9);
        async Task RecordAsync(DateTime startedAt, decimal good)
        {
            var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);
            (await admin.PostAsJsonAsync($"/api/work-orders/{order.WorkOrders[1].Id}/production-records",
                new ProductionRecordRequest(good, 0m, new DateTimeOffset(startedAt, jst), null,
                    ctx.ProductLocationId, false))).EnsureSuccessStatusCode();
        }
        await RecordAsync(new DateTime(2026, 9, 1, 5, 0, 0), 9m);    // 6時前なので製造日は 8/31（期間外）
        await RecordAsync(new DateTime(2026, 9, 1, 10, 0, 0), 8m);
        await RecordAsync(new DateTime(2026, 9, 2, 5, 0, 0), 4m);    // 製造日は 9/1
        await RecordAsync(new DateTime(2026, 9, 3, 10, 0, 0), 6m);   // 計画の無い日
        await RecordAsync(new DateTime(2026, 9, 4, 5, 0, 0), 1m);    // 製造日は 9/3
        await RecordAsync(new DateTime(2026, 9, 4, 7, 0, 0), 10m);   // 製造日は 9/4（期間外）

        // リワーク指図の産出は出来高に数えない（生産性と同じ規則）
        var parent = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);
        var reworkCreated = await admin.PostAsJsonAsync("/api/manufacturing-orders",
            new CreateManufacturingOrderRequest(ctx.ProductId, 2m, null,
                ManufacturingOrderType.Rework, parent.Order.Id, null));
        reworkCreated.EnsureSuccessStatusCode();
        var rework = (await reworkCreated.Content.ReadFromJsonAsync<ManufacturingOrderResponse>())!;
        (await admin.PostAsync($"/api/manufacturing-orders/{rework.Id}/approve", null)).EnsureSuccessStatusCode();
        var reworkExpanded = await admin.PostAsJsonAsync(
            $"/api/manufacturing-orders/{rework.Id}/expand", new ExpandRequest(null));
        reworkExpanded.EnsureSuccessStatusCode();
        var reworkDetail = (await reworkExpanded.Content.ReadFromJsonAsync<ManufacturingOrderDetailResponse>())!;
        (await admin.PostAsJsonAsync(
                $"/api/work-orders/{reworkDetail.WorkOrders.First().Id}/production-records",
                new ProductionRecordRequest(2m, 0m, new DateTimeOffset(new DateTime(2026, 9, 1, 12, 0, 0), jst), null,
                    ctx.ProductLocationId, false)))
            .EnsureSuccessStatusCode();

        var rows = (await admin.GetFromJsonAsync<List<ProductionPlanActualRow>>(
            "/api/production-plans/plan-actual?from=2026-09-01&to=2026-09-03"))!;
        Assert.Equal(
            [(d1, 25m, 12m, (decimal?)48m), (d2, 0m, 0m, null), (d3, 0m, 7m, null)],
            rows.Select(r => (r.BusinessDate, r.PlannedQuantity, r.ActualQuantity, r.AchievementRate)));
        Assert.All(rows, r => Assert.Equal(("FG-01", "PR-01"), (r.ProductCode, r.ProcessCode)));

        // 品目で絞ると該当しない行は出ない。期間の逆転は400
        Assert.Empty((await admin.GetFromJsonAsync<List<ProductionPlanActualRow>>(
            $"/api/production-plans/plan-actual?from=2026-09-01&to=2026-09-03&productId={ctx.MaterialId}"))!);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync("/api/production-plans/plan-actual?from=2026-09-03&to=2026-09-01")).StatusCode);
    }

    [Fact]
    public async Task 予実を作業区で絞ると計画も実績も配下の作業区のものだけに揃う()
    {
        using var factory = new ApiFactory(new Dictionary<string, string>
        {
            ["BusinessDay:TimeZone"] = "Asia/Tokyo",
        });
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 1000m, ctx.MaterialLocationId);
        var plant = await MasterTests.CreateWorkCenterAsync(admin, "P1", "第一工場", WorkCenterLevel.Plant, null);
        var line = await MasterTests.CreateWorkCenterAsync(admin, "L1", "組立1ライン", WorkCenterLevel.Line, plant.Id);
        var area = await MasterTests.CreateWorkCenterAsync(admin, "A1", "組立エリア", WorkCenterLevel.Area, line.Id);
        var wc = await MasterTests.CreateWorkCenterAsync(admin, "WC01", "組立作業区", WorkCenterLevel.WorkCenter, area.Id);
        var otherLine = await MasterTests.CreateWorkCenterAsync(admin, "L2", "組立2ライン", WorkCenterLevel.Line, plant.Id);

        var date = new DateOnly(2026, 9, 1);
        await CreatePlanAsync(admin, new(date, ctx.ProductId, ctx.ProcessId, wc.Id, 10m, null));
        await CreatePlanAsync(admin, new(date, ctx.ProductId, ctx.ProcessId, otherLine.Id, 7m, null));

        var startedAt = new DateTimeOffset(new DateTime(2026, 9, 1, 10, 0, 0), TimeSpan.FromHours(9));
        async Task RecordAsync(decimal good)
        {
            var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);
            (await admin.PostAsJsonAsync($"/api/work-orders/{order.WorkOrders[1].Id}/production-records",
                new ProductionRecordRequest(good, 0m, startedAt, null, ctx.ProductLocationId, false)))
                .EnsureSuccessStatusCode();
        }
        await RecordAsync(5m);   // 工順に作業区が無いので、作業指示も作業区なし
        (await admin.PutAsJsonAsync($"/api/products/{ctx.ProductId}/routing",
            new List<RoutingStepRequest>
            {
                new(1, ctx.ProcessId, 30m, 10m, null, null, null, null, null, wc.Id),
                new(2, ctx.ProcessId, 15m, 5m, null, null, null, null, null, wc.Id),
            })).EnsureSuccessStatusCode();
        await RecordAsync(3m);   // 展開時点の作業区（WC01）が作業指示に固定される

        async Task<ProductionPlanActualRow> GetAsync(string query) =>
            Assert.Single((await admin.GetFromJsonAsync<List<ProductionPlanActualRow>>(
                $"/api/production-plans/plan-actual?from=2026-09-01&to=2026-09-01{query}"))!);

        var all = await GetAsync(string.Empty);
        Assert.Equal((17m, 8m), (all.PlannedQuantity, all.ActualQuantity));

        // ラインを指定したら配下の作業区だけ。作業区なしの実績は外れる
        var byLine = await GetAsync($"&workCenterId={line.Id}");
        Assert.Equal((10m, 3m, (decimal?)30m), (byLine.PlannedQuantity, byLine.ActualQuantity, byLine.AchievementRate));
        var byPlant = await GetAsync($"&workCenterId={plant.Id}");
        Assert.Equal((17m, 3m), (byPlant.PlannedQuantity, byPlant.ActualQuantity));

        // 作業区なしの計画も、作業区で絞ると外れる（作業区ありと同じ日には置けないので翌日に置く）
        await CreatePlanAsync(admin, new(date.AddDays(1), ctx.ProductId, ctx.ProcessId, null, 20m, null));
        var twoDays = (await admin.GetFromJsonAsync<List<ProductionPlanActualRow>>(
            $"/api/production-plans/plan-actual?from=2026-09-01&to=2026-09-02&workCenterId={plant.Id}"))!;
        Assert.Equal([date], twoDays.Select(r => r.BusinessDate));

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync("/api/production-plans/plan-actual?workCenterId=9999")).StatusCode);
    }

    private static async Task<ProductionPlanResponse> CreatePlanAsync(HttpClient client, ProductionPlanRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/production-plans", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductionPlanResponse>())!;
    }
}
