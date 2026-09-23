using System.Net;
using System.Net.Http.Json;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Common;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Contracts.Production;
using MesApp.Core.Contracts.Quality;
using MesApp.Core.Contracts.Users;
using MesApp.Core.Entities;

namespace MesApp.Api.Tests;

public class QualityTests
{
    [Fact]
    public async Task 検査と作業時間とトラブル報告をCSVで取り込める()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        await Phase3TestData.SetupAsync(admin);
        async Task ImportAsync(string path, string csv)
        {
            var result = await Phase3TestData.ImportCsvAsync(admin, path, csv);
            Assert.True(result.Succeeded, $"{path}: " + string.Join(" / ", result.Errors.Select(e => $"{e.Line}行目 {e.Message}")));
        }
        await ImportAsync("masters/csv/inspection-items",
            "Code,Name,TargetProductCode,Type,LowerLimit,UpperLimit\nINS-R,受入寸法,RM-01,Receiving,9.5,10.5\nINS-P,工程内外観,FG-01,InProcess,,\n");
        await ImportAsync("masters/csv/inspection-devices",
            "Code,Name,CalibrationDueOn\nDV-OK,ノギス,2099-12-31\nDV-NG,期限切れノギス,2020-01-31\n");
        await ImportAsync("actuals/csv/receiving", "ProductCode,Quantity,LocationCode,LotNumber\nRM-01,100,LOC-M,RM-LOT-1\n");
        await ImportAsync("actuals/csv/manufacturing-orders", "OrderNo,ProductCode,Quantity,Approve,Expand\nORD-1,FG-01,10,true,true\n");

        const string inspectionHeader =
            "InspectionKey,Type,LotNumber,OrderNo,Sequence,ItemCode,SampleNo,MeasuredValue,TextValue,Judgment,DeviceCode,Judge,Grade,Note\n";
        await ImportAsync("actuals/csv/inspections", inspectionHeader
            + "K1,受入検査,RM-LOT-1,,,INS-R,1,10.0,,,DV-OK,true,A,初回受入\n"
            + "K1,,,,,INS-R,2,10.5,,,DV-OK,,,\n"
            + "K2,InProcess,,ORD-1,1,INS-P,1,,外観良好,合格,,false,,\n");
        var orders = (await admin.GetFromJsonAsync<PagedResult<InspectionOrderResponse>>("/api/inspection-orders"))!.Items;
        var receiving = orders.Single(o => o.Type == InspectionOrderType.Receiving);
        Assert.Equal(InspectionOrderStatus.Judged, receiving.Status);
        Assert.Equal(InspectionJudgment.Pass, receiving.OverallJudgment);
        Assert.Equal(2, receiving.Results.Count);
        Assert.Equal("初回受入", receiving.Note);
        var inProcess = orders.Single(o => o.Type == InspectionOrderType.InProcess);
        Assert.Equal("ORD-1-01", inProcess.TargetWorkOrderNo);
        Assert.Equal(InspectionOrderStatus.InProgress, inProcess.Status); // Judge=false は実績の登録まで

        // 校正期限切れの検査機・未登録ロット・作業指示の無い工程内検査・未登録の検査項目は行番号付きで返り、1件も登録されない
        var invalid = await Phase3TestData.ImportCsvAsync(admin, "actuals/csv/inspections", inspectionHeader
            + "E1,Receiving,RM-LOT-1,,,INS-R,1,10,,,DV-NG,true,,\n"
            + "E2,Receiving,NO-LOT,,,INS-R,1,10,,,,,,\n"
            + "E3,InProcess,,,,INS-P,1,,,合格,,,,\n"
            + "E4,Receiving,RM-LOT-1,,,INS-X,1,10,,,,,,\n");
        Assert.False(invalid.Succeeded);
        Assert.Contains(invalid.Errors, e => e.Line == 2 && e.Message.Contains("校正"));
        Assert.Contains(invalid.Errors, e => e.Line == 3 && e.Message.Contains("NO-LOT"));
        Assert.Contains(invalid.Errors, e => e.Line == 4 && e.Message.Contains("OrderNo"));
        Assert.Contains(invalid.Errors, e => e.Line == 5 && e.Message.Contains("INS-X"));
        Assert.Equal(2, (await admin.GetFromJsonAsync<PagedResult<InspectionOrderResponse>>("/api/inspection-orders"))!.Total);

        await ImportAsync("actuals/csv/work-time-records",
            "Type,IndirectCategory,OrderNo,Sequence,StartedAt,EndedAt,Note\n"
            + "Direct,,ORD-1,1,2026-09-17 08:00,2026-09-17 09:00,\n"
            + "間接作業,部材準備,,,2026-09-17 09:00,2026-09-17 09:30,\n");
        var workTimes = (await admin.GetFromJsonAsync<List<Core.Contracts.Execution.WorkTimeResponse>>("/api/work-time-records"))!;
        Assert.Equal(2, workTimes.Count);
        Assert.Contains(workTimes, w => w.WorkOrderNo == "ORD-1-01" && w.Type == WorkTimeType.Direct);
        var workTimeError = await Phase3TestData.ImportCsvAsync(admin, "actuals/csv/work-time-records",
            "Type,StartedAt\nDirect,2026-09-17 10:00\n");
        Assert.Contains("直接作業", Assert.Single(workTimeError.Errors).Message);

        // トラブル報告は単票APIと同じく誰でも取り込める（検査は品質管理の権限が要る）
        using var operator_ = await TestAuth.CreateUserClientAsync(
            factory, admin, "operator1", "Passw0rd123", MesRoles.Operator);
        var trouble = await Phase3TestData.ImportActualCsvAsync(operator_, "trouble-reports",
            "OccurredAt,Category,OrderNo,Sequence,EquipmentAssetNo,Content\n2026-09-17 10:15,品質,ORD-1,1,,バリの発生\n");
        Assert.True(trouble.Succeeded);
        var reports = (await admin.GetFromJsonAsync<PagedResult<Core.Contracts.Execution.TroubleReportResponse>>(
            "/api/trouble-reports"))!.Items;
        Assert.Equal("ORD-1-01", Assert.Single(reports).WorkOrderNo);
        var troubleError = await Phase3TestData.ImportActualCsvAsync(operator_, "trouble-reports",
            "OccurredAt,Category,EquipmentAssetNo,Content\n2026-09-17 10:15,Safety,EQ-X,転倒\n");
        Assert.Contains("EQ-X", Assert.Single(troubleError.Errors).Message);
        Assert.Equal(HttpStatusCode.Forbidden, (await Phase3TestData.PostActualCsvAsync(
            operator_, "inspections", inspectionHeader + "K9,Receiving,RM-LOT-1,,,INS-R,1,10,,,,,,\n")).StatusCode);
    }

    /// <summary>完成品FG-01の完成品検査基準（外径9.5〜10.5）を登録する</summary>
    private static async Task<InspectionItemResponse> CreateFinalInspectionItemAsync(
        HttpClient admin, int productId, decimal lower = 9.5m, decimal upper = 10.5m)
    {
        var response = await admin.PostAsJsonAsync("/api/inspection-items",
            new InspectionItemRequest("INS-01", "外径測定", productId, null, InspectionType.FinalProduct,
                lower, upper, 10m, "ノギス", 1));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<InspectionItemResponse>())!;
    }

    [Fact]
    public async Task 工程内検査の基準は対象品目と対象工程の両方が合うものだけが選ばれる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var other = await MasterTests.CreateProductAsync(admin, "FG-99", "別の完成品", ProductType.Product);
        var otherProcess = await MasterTests.CreateProcessAsync(admin, "PR-99", "別工程");
        async Task<int> CreateItemAsync(string code, int? productId, int? processId)
        {
            var response = await admin.PostAsJsonAsync("/api/inspection-items",
                new InspectionItemRequest(code, code, productId, processId, InspectionType.InProcess,
                    null, null, null, null, null));
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<InspectionItemResponse>())!.Id;
        }
        await CreateItemAsync("INS-A", ctx.ProductId, ctx.ProcessId);   // この品目のこの工程
        await CreateItemAsync("INS-B", ctx.ProductId, null);            // この品目のどの工程でも
        await CreateItemAsync("INS-C", null, ctx.ProcessId);            // この工程ならどの品目でも
        var otherProductItem = await CreateItemAsync("INS-D", other.Id, ctx.ProcessId);   // 別品目の同じ工程
        await CreateItemAsync("INS-E", ctx.ProductId, otherProcess.Id); // 同じ品目の別工程

        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);
        var workOrderId = order.WorkOrders[0].Id;
        var created = await admin.PostAsJsonAsync("/api/inspection-orders",
            new InspectionOrderCreateRequest(InspectionOrderType.InProcess, null, workOrderId, null, null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var inspection = await created.Content.ReadFromJsonAsync<InspectionOrderResponse>();
        Assert.Equal(["INS-A", "INS-B", "INS-C"], inspection!.Items.Select(i => i.Code).Order());

        // 指定した基準も同じ条件で確かめる（別品目の基準では検査させない）
        var manual = await admin.PostAsJsonAsync("/api/inspection-orders",
            new InspectionOrderCreateRequest(InspectionOrderType.InProcess, null, workOrderId, [otherProductItem], null));
        Assert.Equal(HttpStatusCode.BadRequest, manual.StatusCode);
        Assert.Contains("INS-D", await manual.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task 保留ロットや判定前の指示があるロットには発行できず再検査の取消で不良に戻る()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var item = await CreateFinalInspectionItemAsync(admin, ctx.ProductId);
        var lot = await Phase3TestData.ReceiveAsync(admin, ctx.ProductId, 100m, ctx.ProductLocationId);
        async Task<HttpResponseMessage> IssueAsync(InspectionOrderType type) =>
            await admin.PostAsJsonAsync("/api/inspection-orders",
                new InspectionOrderCreateRequest(type, lot.Id, null, null, null));
        async Task<LotStockStatus> LotStatusAsync() =>
            (await admin.GetFromJsonAsync<Core.Contracts.Inventory.LotResponse>($"/api/inventory/lots/{lot.Id}"))!.StockStatus;

        // 保留中のロットには発行できない（発行→取消で保留が外れてしまう）
        (await admin.PostAsJsonAsync("/api/inventory/status",
            new Core.Contracts.Inventory.LotStatusRequest(lot.Id, LotStockStatus.OnHold, "調査中"))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await IssueAsync(InspectionOrderType.FinalProduct)).StatusCode);
        Assert.Equal(LotStockStatus.OnHold, await LotStatusAsync());
        (await admin.PostAsJsonAsync("/api/inventory/status",
            new Core.Contracts.Inventory.LotStatusRequest(lot.Id, LotStockStatus.Normal, "調査完了"))).EnsureSuccessStatusCode();

        // 判定前の指示があるロットには重ねて発行できない（先に合格した方でロットが正常になってしまう）
        var first = await (await IssueAsync(InspectionOrderType.FinalProduct)).Content.ReadFromJsonAsync<InspectionOrderResponse>();
        var duplicated = await IssueAsync(InspectionOrderType.FinalProduct);
        Assert.Equal(HttpStatusCode.Conflict, duplicated.StatusCode);
        Assert.Contains(first!.OrderNo, await duplicated.Content.ReadAsStringAsync());
        // サンプル検査はロットを拘束しないので重ねてよい
        (await admin.PostAsJsonAsync("/api/inspection-items",
            new InspectionItemRequest("INS-S", "保管サンプル外観", ctx.ProductId, null, InspectionType.Sample,
                null, null, null, "目視", 1))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Created, (await IssueAsync(InspectionOrderType.Sample)).StatusCode);

        // 不合格で不良になったロットは再検査だけ発行でき、再検査を取り消すと不良に戻る（正常にしない）
        await admin.PostAsJsonAsync($"/api/inspection-orders/{first.Id}/results",
            new List<InspectionResultRequest> { new(item.Id, 1, 12.0m, null, null) });
        (await admin.PostAsJsonAsync($"/api/inspection-orders/{first.Id}/judge", new InspectionJudgeRequest(null)))
            .EnsureSuccessStatusCode();
        Assert.Equal(LotStockStatus.Defective, await LotStatusAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await IssueAsync(InspectionOrderType.FinalProduct)).StatusCode);
        var reinspection = await (await IssueAsync(InspectionOrderType.Reinspection)).Content.ReadFromJsonAsync<InspectionOrderResponse>();
        Assert.Equal(LotStockStatus.AwaitingInspection, await LotStatusAsync());
        (await admin.PostAsync($"/api/inspection-orders/{reinspection!.Id}/cancel", null)).EnsureSuccessStatusCode();
        Assert.Equal(LotStockStatus.Defective, await LotStatusAsync());
    }

    [Fact]
    public async Task 規格値で決まる合否は手で変えられずサンプリング数がそろうまで判定できない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var response = await admin.PostAsJsonAsync("/api/inspection-items",
            new InspectionItemRequest("INS-01", "外径測定", ctx.ProductId, null, InspectionType.FinalProduct,
                9.5m, 10.5m, 10m, "ノギス", 2));
        var item = (await response.Content.ReadFromJsonAsync<InspectionItemResponse>())!;
        var lot = await Phase3TestData.ReceiveAsync(admin, ctx.ProductId, 100m, ctx.ProductLocationId);
        var order = (await (await admin.PostAsJsonAsync("/api/inspection-orders",
            new InspectionOrderCreateRequest(InspectionOrderType.FinalProduct, lot.Id, null, null, null)))
            .Content.ReadFromJsonAsync<InspectionOrderResponse>())!;

        // 規格外の測定値を「合格」と指定しても通さない（規格外品を使うなら不適合の特採で処置する）
        var overridden = await admin.PostAsJsonAsync($"/api/inspection-orders/{order.Id}/results",
            new List<InspectionResultRequest> { new(item.Id, 1, 12.0m, null, InspectionJudgment.Pass) });
        Assert.Equal(HttpStatusCode.BadRequest, overridden.StatusCode);
        // 自動判定と同じ合否なら指定してもよい
        var recorded = await (await admin.PostAsJsonAsync($"/api/inspection-orders/{order.Id}/results",
            new List<InspectionResultRequest> { new(item.Id, 1, 10.0m, null, InspectionJudgment.Pass) }))
            .Content.ReadFromJsonAsync<InspectionOrderResponse>();

        // サンプリング数2に対して1サンプルでは判定できない
        var early = await admin.PostAsJsonAsync($"/api/inspection-orders/{order.Id}/judge", new InspectionJudgeRequest(null));
        Assert.Equal(HttpStatusCode.BadRequest, early.StatusCode);
        Assert.Contains("サンプリング数", await early.Content.ReadAsStringAsync());

        // 訂正も同じ条件：規格外の値へ直しながら合格にはできない
        var resultId = recorded!.Results.Single().Id;
        var badCorrection = await admin.PutAsJsonAsync($"/api/inspection-orders/{order.Id}/results/{resultId}",
            new InspectionResultCorrectionRequest(11.0m, null, InspectionJudgment.Pass, "転記ミス"));
        Assert.Equal(HttpStatusCode.BadRequest, badCorrection.StatusCode);

        (await admin.PostAsJsonAsync($"/api/inspection-orders/{order.Id}/results",
            new List<InspectionResultRequest> { new(item.Id, 2, 10.1m, null, null) })).EnsureSuccessStatusCode();
        var judged = await admin.PostAsJsonAsync($"/api/inspection-orders/{order.Id}/judge", new InspectionJudgeRequest(null));
        Assert.Equal(HttpStatusCode.OK, judged.StatusCode);
    }

    [Fact]
    public async Task 再検査は直近の判定済みの検査の基準を引き継ぎ取消済みの検査は訂正できない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var receivingItem = (await (await admin.PostAsJsonAsync("/api/inspection-items",
            new InspectionItemRequest("INS-R", "受入寸法", ctx.MaterialId, null, InspectionType.Receiving,
                9.5m, 10.5m, 10m, "ノギス", 1))).Content.ReadFromJsonAsync<InspectionItemResponse>())!;
        async Task<HttpResponseMessage> IssueAsync(InspectionOrderType type, int lotId) =>
            await admin.PostAsJsonAsync("/api/inspection-orders", new InspectionOrderCreateRequest(type, lotId, null, null, null));

        // 判定済みの検査が無いロットは再検査できない
        var fresh = await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 10m, ctx.MaterialLocationId);
        Assert.Equal(HttpStatusCode.BadRequest, (await IssueAsync(InspectionOrderType.Reinspection, fresh.Id)).StatusCode);

        // 受入検査で不合格になった部材ロットの再検査は、受入の基準を引き継ぐ（完成品の基準を探さない）
        var lot = await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 100m, ctx.MaterialLocationId);
        var first = (await (await IssueAsync(InspectionOrderType.Receiving, lot.Id)).Content.ReadFromJsonAsync<InspectionOrderResponse>())!;
        await admin.PostAsJsonAsync($"/api/inspection-orders/{first.Id}/results",
            new List<InspectionResultRequest> { new(receivingItem.Id, 1, 12.0m, null, null) });
        (await admin.PostAsJsonAsync($"/api/inspection-orders/{first.Id}/judge", new InspectionJudgeRequest(null)))
            .EnsureSuccessStatusCode();
        var reinspection = await IssueAsync(InspectionOrderType.Reinspection, lot.Id);
        Assert.Equal(HttpStatusCode.Created, reinspection.StatusCode);
        var reinspectionOrder = (await reinspection.Content.ReadFromJsonAsync<InspectionOrderResponse>())!;
        Assert.Equal(["INS-R"], reinspectionOrder.Items.Select(i => i.Code));

        // 取り消した検査の実績は訂正できない
        var recorded = (await (await admin.PostAsJsonAsync($"/api/inspection-orders/{reinspectionOrder.Id}/results",
            new List<InspectionResultRequest> { new(receivingItem.Id, 1, 10.0m, null, null) }))
            .Content.ReadFromJsonAsync<InspectionOrderResponse>())!;
        (await admin.PostAsync($"/api/inspection-orders/{reinspectionOrder.Id}/cancel", null)).EnsureSuccessStatusCode();
        var correction = await admin.PutAsJsonAsync(
            $"/api/inspection-orders/{reinspectionOrder.Id}/results/{recorded.Results.Single().Id}",
            new InspectionResultCorrectionRequest(10.1m, null, InspectionJudgment.Pass, "転記ミス"));
        Assert.Equal(HttpStatusCode.Conflict, correction.StatusCode);
    }

    [Fact]
    public async Task 検査指示を取消すと検査待ちのロットが解放され承認済みは取消せない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var item = await CreateFinalInspectionItemAsync(admin, ctx.ProductId);
        var lot = await Phase3TestData.ReceiveAsync(admin, ctx.ProductId, 100m, ctx.ProductLocationId);

        var created = await admin.PostAsJsonAsync("/api/inspection-orders",
            new InspectionOrderCreateRequest(InspectionOrderType.FinalProduct, lot.Id, null, null, null));
        var order = await created.Content.ReadFromJsonAsync<InspectionOrderResponse>();

        // 取消すと指示は取消状態になり、検査待ちで拘束していたロットは正常へ戻る
        var canceled = await admin.PostAsync($"/api/inspection-orders/{order!.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, canceled.StatusCode);
        var afterCancel = await canceled.Content.ReadFromJsonAsync<InspectionOrderResponse>();
        Assert.Equal(InspectionOrderStatus.Canceled, afterCancel!.Status);
        var lotAfterCancel = await admin.GetFromJsonAsync<Core.Contracts.Inventory.LotResponse>(
            $"/api/inventory/lots/{lot.Id}");
        Assert.Equal(LotStockStatus.Normal, lotAfterCancel!.StockStatus);

        // 取消済みは再度取消せない
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsync($"/api/inspection-orders/{order.Id}/cancel", null)).StatusCode);

        // 承認済みも取消せない
        var second = await admin.PostAsJsonAsync("/api/inspection-orders",
            new InspectionOrderCreateRequest(InspectionOrderType.FinalProduct, lot.Id, null, null, null));
        var order2 = await second.Content.ReadFromJsonAsync<InspectionOrderResponse>();
        await admin.PostAsJsonAsync($"/api/inspection-orders/{order2!.Id}/results",
            new List<InspectionResultRequest> { new(item.Id, 1, 10.0m, null, null) });
        await admin.PostAsJsonAsync($"/api/inspection-orders/{order2.Id}/judge", new InspectionJudgeRequest(null));
        (await admin.PostAsync($"/api/inspection-orders/{order2.Id}/approve", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsync($"/api/inspection-orders/{order2.Id}/cancel", null)).StatusCode);
    }

    [Fact]
    public async Task 校正期限切れの検査機では検査実績を登録できない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var item = await CreateFinalInspectionItemAsync(admin, ctx.ProductId);
        var lot = await Phase3TestData.ReceiveAsync(admin, ctx.ProductId, 100m, ctx.ProductLocationId);

        var expired = await admin.PostAsJsonAsync("/api/inspection-devices",
            new InspectionDeviceRequest("MD-OLD", "期限切れノギス", null, null,
                new DateOnly(2025, 1, 10), new DateOnly(2026, 1, 10), 365, null));
        expired.EnsureSuccessStatusCode();
        var expiredDevice = (await expired.Content.ReadFromJsonAsync<InspectionDeviceResponse>())!;

        var valid = await admin.PostAsJsonAsync("/api/inspection-devices",
            new InspectionDeviceRequest("MD-OK", "校正済みノギス", null, null,
                DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today).AddYears(1),
                365, null));
        valid.EnsureSuccessStatusCode();
        var validDevice = (await valid.Content.ReadFromJsonAsync<InspectionDeviceResponse>())!;

        var created = await admin.PostAsJsonAsync("/api/inspection-orders",
            new InspectionOrderCreateRequest(InspectionOrderType.FinalProduct, lot.Id, null, null, null));
        created.EnsureSuccessStatusCode();
        var order = (await created.Content.ReadFromJsonAsync<InspectionOrderResponse>())!;

        // 校正期限を過ぎた機器で測った結果は品質保証の根拠にならないため拒否する（C-20-50-03）
        var rejected = await admin.PostAsJsonAsync($"/api/inspection-orders/{order.Id}/results",
            new List<InspectionResultRequest> { new(item.Id, 1, 10.0m, null, null, expiredDevice.Id) });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);

        // 存在しない検査機は400
        var missing = await admin.PostAsJsonAsync($"/api/inspection-orders/{order.Id}/results",
            new List<InspectionResultRequest> { new(item.Id, 1, 10.0m, null, null, 9999) });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        // 校正済みの機器なら登録でき、どの機器で測ったかが実績に残る（成績書に出す）
        var accepted = await admin.PostAsJsonAsync($"/api/inspection-orders/{order.Id}/results",
            new List<InspectionResultRequest> { new(item.Id, 1, 10.0m, null, null, validDevice.Id) });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var body = (await accepted.Content.ReadFromJsonAsync<InspectionOrderResponse>())!;
        Assert.Equal("MD-OK", body.Results[0].InspectionDeviceCode);

        // 検査機を指定しない従来どおりの登録も引き続きできる
        var withoutDevice = await admin.PostAsJsonAsync($"/api/inspection-orders/{order.Id}/results",
            new List<InspectionResultRequest> { new(item.Id, 2, 10.0m, null, null) });
        Assert.Equal(HttpStatusCode.OK, withoutDevice.StatusCode);
    }

    [Fact]
    public async Task 検査指示から実績登録判定承認まで通しで動作しロットステータスへ反映される()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var item = await CreateFinalInspectionItemAsync(admin, ctx.ProductId);
        var lot = await Phase3TestData.ReceiveAsync(admin, ctx.ProductId, 100m, ctx.ProductLocationId);

        // 検査指示発行（項目は品目の完成品基準を自動選択）→ ロットは検査待ちへ
        var created = await admin.PostAsJsonAsync("/api/inspection-orders",
            new InspectionOrderCreateRequest(InspectionOrderType.FinalProduct, lot.Id, null, null, null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var order = await created.Content.ReadFromJsonAsync<InspectionOrderResponse>();
        Assert.StartsWith("IN", order!.OrderNo);
        Assert.Single(order.Items);
        var lotAfterCreate = await admin.GetFromJsonAsync<Core.Contracts.Inventory.LotResponse>(
            $"/api/inventory/lots/{lot.Id}");
        Assert.Equal(LotStockStatus.AwaitingInspection, lotAfterCreate!.StockStatus);

        // 実績未登録では判定できない
        var premature = await admin.PostAsJsonAsync(
            $"/api/inspection-orders/{order.Id}/judge", new InspectionJudgeRequest(null));
        Assert.Equal(HttpStatusCode.BadRequest, premature.StatusCode);

        // 実績登録（規格内 → 自動で合格判定）
        var results = await admin.PostAsJsonAsync($"/api/inspection-orders/{order.Id}/results",
            new List<InspectionResultRequest> { new(item.Id, 1, 10.0m, null, null) });
        Assert.Equal(HttpStatusCode.OK, results.StatusCode);
        var withResults = await results.Content.ReadFromJsonAsync<InspectionOrderResponse>();
        Assert.Equal(InspectionJudgment.Pass, withResults!.Results[0].Judgment);

        // 総合判定（合格）→ ロットは正常へ、グレードも設定（C-60-10-01）
        var judged = await admin.PostAsJsonAsync(
            $"/api/inspection-orders/{order.Id}/judge", new InspectionJudgeRequest("A"));
        Assert.Equal(HttpStatusCode.OK, judged.StatusCode);
        var judgedBody = await judged.Content.ReadFromJsonAsync<InspectionOrderResponse>();
        Assert.Equal(InspectionJudgment.Pass, judgedBody!.OverallJudgment);
        var lotAfterJudge = await admin.GetFromJsonAsync<Core.Contracts.Inventory.LotResponse>(
            $"/api/inventory/lots/{lot.Id}");
        Assert.Equal(LotStockStatus.Normal, lotAfterJudge!.StockStatus);
        Assert.Equal("A", lotAfterJudge.Grade);

        // 承認（C-20-10-06）
        var approved = await admin.PostAsync($"/api/inspection-orders/{order.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
    }

    [Fact]
    public async Task 検査項目の必要スキルは発行時に写り検査実績の登録者と照合される()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var skill = (await (await admin.PostAsJsonAsync("/api/skills",
            new SkillRequest("SK-QC", "検査員認定", SkillType.Certification, true)))
            .Content.ReadFromJsonAsync<SkillResponse>())!;
        var created = await admin.PostAsJsonAsync("/api/inspection-items",
            new InspectionItemRequest("INS-01", "外径測定", ctx.ProductId, null, InspectionType.FinalProduct,
                9.5m, 10.5m, 10m, "ノギス", 1, skill.Id));
        var item = (await created.Content.ReadFromJsonAsync<InspectionItemResponse>())!;
        Assert.Equal("検査員認定", item.RequiredSkillName);

        var lot = await Phase3TestData.ReceiveAsync(admin, ctx.ProductId, 10m, ctx.ProductLocationId);
        var order = (await (await admin.PostAsJsonAsync("/api/inspection-orders",
            new InspectionOrderCreateRequest(InspectionOrderType.FinalProduct, lot.Id, null, null, null)))
            .Content.ReadFromJsonAsync<InspectionOrderResponse>())!;

        // 発行後にマスタから必要スキルを外しても、発行済みの検査の照合条件は変わらない（スナップショット）
        (await admin.PutAsJsonAsync($"/api/inspection-items/{item.Id}",
            new InspectionItemRequest("INS-01", "外径測定", ctx.ProductId, null, InspectionType.FinalProduct,
                9.5m, 10.5m, 10m, "ノギス", 1))).EnsureSuccessStatusCode();

        List<InspectionResultRequest> results = [new(item.Id, 1, 10.0m, null, null)];
        var rejected = await admin.PostAsJsonAsync($"/api/inspection-orders/{order.Id}/results", results);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var problem = await rejected.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Contains("検査員認定", problem!.Title);

        // 有効期限内の資格を付与すると登録できる
        var me = (await admin.GetFromJsonAsync<List<UserSummaryResponse>>("/api/users"))!
            .Single(u => u.UserName == TestAuth.AdminUser);
        (await admin.PutAsJsonAsync($"/api/users/{me.Id}/skills",
            new List<UserSkillRequest> { new(skill.Id, null, DateOnly.FromDateTime(DateTime.Today).AddYears(1)) }))
            .EnsureSuccessStatusCode();
        var accepted = await admin.PostAsJsonAsync($"/api/inspection-orders/{order.Id}/results", results);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task 管理図は検査指示ごとに群を作り取消した検査の測定値を使わない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var item = await CreateFinalInspectionItemAsync(admin, ctx.ProductId);

        async Task<InspectionOrderResponse> InspectAsync(params decimal[] values)
        {
            var lot = await Phase3TestData.ReceiveAsync(admin, ctx.ProductId, 10m, ctx.ProductLocationId);
            var created = await admin.PostAsJsonAsync("/api/inspection-orders",
                new InspectionOrderCreateRequest(InspectionOrderType.FinalProduct, lot.Id, null, null, null));
            var order = (await created.Content.ReadFromJsonAsync<InspectionOrderResponse>())!;
            (await admin.PostAsJsonAsync($"/api/inspection-orders/{order.Id}/results",
                    values.Select((v, i) => new InspectionResultRequest(item.Id, i + 1, v, null, null)).ToList()))
                .EnsureSuccessStatusCode();
            return order;
        }

        await InspectAsync(10.0m, 10.2m);
        await InspectAsync(9.9m, 10.1m);
        var canceled = await InspectAsync(12.0m, 12.4m);
        (await admin.PostAsync($"/api/inspection-orders/{canceled.Id}/cancel", null)).EnsureSuccessStatusCode();

        var chart = await admin.GetFromJsonAsync<ControlChartResponse>(
            $"/api/quality/control-chart?inspectionItemId={item.Id}");

        Assert.Equal(2, chart!.Points.Count);
        Assert.DoesNotContain(chart.Points, p => p.OrderNo == canceled.OrderNo);
        Assert.False(chart.IsIndividuals);
        Assert.Equal(2, chart.SubgroupSize);
        Assert.Equal(10.05m, chart.CenterLine); // (10.1 + 10.0) / 2
        Assert.Equal(9.5m, chart.LowerSpecLimit); // 規格は検査指示のスナップショット
        Assert.Equal(10.5m, chart.UpperSpecLimit);
        Assert.NotNull(chart.Cpk);

        // 期間外を指定すると点は無く、管理限界も出さない
        var empty = await admin.GetFromJsonAsync<ControlChartResponse>(
            $"/api/quality/control-chart?inspectionItemId={item.Id}&from=2020-01-01&to=2020-01-31");
        Assert.Empty(empty!.Points);
        Assert.Null(empty.CenterLine);

        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync("/api/quality/control-chart?inspectionItemId=99999")).StatusCode);
    }

    [Fact]
    public async Task 検査基準を改訂しても発行済みの検査は当時の規格値で判定され成績書も変わらない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var item = await CreateFinalInspectionItemAsync(admin, ctx.ProductId); // 外径 9.5〜10.5（第1版）
        var lot = await Phase3TestData.ReceiveAsync(admin, ctx.ProductId, 100m, ctx.ProductLocationId);

        var created = await admin.PostAsJsonAsync("/api/inspection-orders",
            new InspectionOrderCreateRequest(InspectionOrderType.FinalProduct, lot.Id, null, null, null));
        var order = (await created.Content.ReadFromJsonAsync<InspectionOrderResponse>())!;
        Assert.Equal(9.5m, order.Items[0].LowerLimit);
        Assert.Equal(1, order.Items[0].ItemVersion);

        // 発行後に基準を改訂（規格を厳しくし、項目名も変える）
        var revised = await admin.PutAsJsonAsync($"/api/inspection-items/{item.Id}",
            new InspectionItemRequest("INS-01", "外径測定（改訂）", ctx.ProductId, null,
                InspectionType.FinalProduct, 9.9m, 10.1m, 10m, "マイクロメータ", 1));
        revised.EnsureSuccessStatusCode();
        Assert.Equal(2, (await revised.Content.ReadFromJsonAsync<InspectionItemResponse>())!.Version);

        // 測定値10.3は旧規格（9.5〜10.5）では合格、新規格（9.9〜10.1）では不合格。
        // 発行済みの検査は当時の規格で判定されなければならない
        var results = await admin.PostAsJsonAsync($"/api/inspection-orders/{order.Id}/results",
            new List<InspectionResultRequest> { new(item.Id, 1, 10.3m, null, null) });
        results.EnsureSuccessStatusCode();
        var withResults = (await results.Content.ReadFromJsonAsync<InspectionOrderResponse>())!;
        Assert.Equal(InspectionJudgment.Pass, withResults.Results[0].Judgment);

        // 成績書の表示元（詳細取得）も当時の規格値・項目名・版数を返す
        var reloaded = await admin.GetFromJsonAsync<InspectionOrderResponse>(
            $"/api/inspection-orders/{order.Id}");
        Assert.Equal(9.5m, reloaded!.Items[0].LowerLimit);
        Assert.Equal(10.5m, reloaded.Items[0].UpperLimit);
        Assert.Equal("ノギス", reloaded.Items[0].Method);
        Assert.Equal("外径測定", reloaded.Items[0].Name);
        Assert.Equal(1, reloaded.Items[0].ItemVersion);
        Assert.Equal("外径測定", reloaded.Results[0].ItemName);

        // 改訂後に発行した検査は新しい基準で判定される
        var lot2 = await Phase3TestData.ReceiveAsync(admin, ctx.ProductId, 100m, ctx.ProductLocationId);
        var created2 = await admin.PostAsJsonAsync("/api/inspection-orders",
            new InspectionOrderCreateRequest(InspectionOrderType.FinalProduct, lot2.Id, null, null, null));
        var order2 = (await created2.Content.ReadFromJsonAsync<InspectionOrderResponse>())!;
        Assert.Equal(9.9m, order2.Items[0].LowerLimit);
        Assert.Equal(2, order2.Items[0].ItemVersion);
        Assert.Equal("外径測定（改訂）", order2.Items[0].Name);

        var results2 = await admin.PostAsJsonAsync($"/api/inspection-orders/{order2.Id}/results",
            new List<InspectionResultRequest> { new(item.Id, 1, 10.3m, null, null) });
        results2.EnsureSuccessStatusCode();
        var withResults2 = (await results2.Content.ReadFromJsonAsync<InspectionOrderResponse>())!;
        Assert.Equal(InspectionJudgment.Fail, withResults2.Results[0].Judgment);
    }

    [Fact]
    public async Task 不合格判定でロットが不良になり不適合が自動起票される()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var item = await CreateFinalInspectionItemAsync(admin, ctx.ProductId);
        var lot = await Phase3TestData.ReceiveAsync(admin, ctx.ProductId, 100m, ctx.ProductLocationId);

        var created = await admin.PostAsJsonAsync("/api/inspection-orders",
            new InspectionOrderCreateRequest(InspectionOrderType.FinalProduct, lot.Id, null, null, null));
        var order = await created.Content.ReadFromJsonAsync<InspectionOrderResponse>();

        // 規格外の測定値 → 自動で不合格
        await admin.PostAsJsonAsync($"/api/inspection-orders/{order!.Id}/results",
            new List<InspectionResultRequest> { new(item.Id, 1, 12.0m, null, null) });
        var judged = await admin.PostAsJsonAsync(
            $"/api/inspection-orders/{order.Id}/judge", new InspectionJudgeRequest(null));
        var judgedBody = await judged.Content.ReadFromJsonAsync<InspectionOrderResponse>();
        Assert.Equal(InspectionJudgment.Fail, judgedBody!.OverallJudgment);

        // ロットは不良へ
        var lotInfo = await admin.GetFromJsonAsync<Core.Contracts.Inventory.LotResponse>(
            $"/api/inventory/lots/{lot.Id}");
        Assert.Equal(LotStockStatus.Defective, lotInfo!.StockStatus);

        // 不適合が自動起票される（発生元＝検査）
        var nonconformances = await admin.GetFromJsonAsync<PagedResult<NonconformanceResponse>>(
            "/api/nonconformances");
        var nonconformance = Assert.Single(nonconformances!.Items);
        Assert.Equal(NonconformanceSource.Inspection, nonconformance.Source);
        Assert.Equal(lot.Id, nonconformance.LotId);
    }

    [Fact]
    public async Task 検査実績の訂正で判定済み指示は再判定が必要になる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var item = await CreateFinalInspectionItemAsync(admin, ctx.ProductId);
        var lot = await Phase3TestData.ReceiveAsync(admin, ctx.ProductId, 100m, ctx.ProductLocationId);

        var created = await admin.PostAsJsonAsync("/api/inspection-orders",
            new InspectionOrderCreateRequest(InspectionOrderType.FinalProduct, lot.Id, null, null, null));
        var order = await created.Content.ReadFromJsonAsync<InspectionOrderResponse>();
        var results = await admin.PostAsJsonAsync($"/api/inspection-orders/{order!.Id}/results",
            new List<InspectionResultRequest> { new(item.Id, 1, 10.0m, null, null) });
        var resultId = (await results.Content.ReadFromJsonAsync<InspectionOrderResponse>())!.Results[0].Id;
        await admin.PostAsJsonAsync($"/api/inspection-orders/{order.Id}/judge", new InspectionJudgeRequest(null));

        // 訂正（C-20-50-07）→ 実施中へ戻り、ロットも検査待ちへ戻る
        var corrected = await admin.PutAsJsonAsync(
            $"/api/inspection-orders/{order.Id}/results/{resultId}",
            new InspectionResultCorrectionRequest(12.0m, null, InspectionJudgment.Fail, "測定器の読み間違い"));
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        var correctedBody = await corrected.Content.ReadFromJsonAsync<InspectionOrderResponse>();
        Assert.Equal(InspectionOrderStatus.InProgress, correctedBody!.Status);
        Assert.Null(correctedBody.OverallJudgment);
        var lotInfo = await admin.GetFromJsonAsync<Core.Contracts.Inventory.LotResponse>(
            $"/api/inventory/lots/{lot.Id}");
        Assert.Equal(LotStockStatus.AwaitingInspection, lotInfo!.StockStatus);
    }

    [Fact]
    public async Task 不適合の対応指示で保留はロットへ反映されリワークは指図が起票される()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 100m, ctx.MaterialLocationId);

        // 生産で産出ロットを作る（リワーク指図の由来特定のため）
        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);
        var posted = await admin.PostAsJsonAsync(
            $"/api/work-orders/{order.WorkOrders[1].Id}/production-records",
            new Core.Contracts.Execution.ProductionRecordRequest(
                10m, 0m, DateTimeOffset.Now, null, ctx.ProductLocationId, false));
        var record = await posted.Content.ReadFromJsonAsync<Core.Contracts.Execution.ProductionRecordResponse>();
        var outputLotId = record!.OutputLotId!.Value;

        // 不適合起票（現場から）
        var reported = await admin.PostAsJsonAsync("/api/nonconformances",
            new NonconformanceCreateRequest(NonconformanceSource.Production, outputLotId,
                order.WorkOrders[1].Id, null, "外観キズ", "作業ミス", null));
        var nc = await reported.Content.ReadFromJsonAsync<NonconformanceResponse>();

        // 対応指示：リワーク → リワーク指図が自動起票される
        var instructed = await admin.PostAsJsonAsync($"/api/nonconformances/{nc!.Id}/instruct",
            new NonconformanceActionRequest(NonconformanceAction.Rework, "再研磨すること"));
        Assert.Equal(HttpStatusCode.OK, instructed.StatusCode);
        var withRework = await instructed.Content.ReadFromJsonAsync<NonconformanceResponse>();
        Assert.NotNull(withRework!.ReworkOrderId);
        var reworkOrder = await admin.GetFromJsonAsync<ManufacturingOrderDetailResponse>(
            $"/api/manufacturing-orders/{withRework.ReworkOrderId}");
        Assert.Equal(ManufacturingOrderType.Rework, reworkOrder!.Order.OrderType);
        Assert.Equal(order.Order.Id, reworkOrder.Order.SourceOrderId);

        // 対応実行記録 → 承認でクローズ
        var recorded = await admin.PostAsJsonAsync($"/api/nonconformances/{nc.Id}/record-action",
            new NonconformanceActionRecordRequest("再研磨を実施し外観OK"));
        Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);
        var approved = await admin.PostAsync($"/api/nonconformances/{nc.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var closed = await approved.Content.ReadFromJsonAsync<NonconformanceResponse>();
        Assert.Equal(NonconformanceStatus.Closed, closed!.Status);
    }

    [Fact]
    public async Task 特採承認でロットが正常へ戻る()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var lot = await Phase3TestData.ReceiveAsync(admin, ctx.ProductId, 100m, ctx.ProductLocationId);

        var reported = await admin.PostAsJsonAsync("/api/nonconformances",
            new NonconformanceCreateRequest(NonconformanceSource.Receiving, lot.Id, null, null,
                "軽微な外観不良", null, null));
        var nc = await reported.Content.ReadFromJsonAsync<NonconformanceResponse>();

        // 保留指示 → ロット保留
        await admin.PostAsJsonAsync($"/api/nonconformances/{nc!.Id}/instruct",
            new NonconformanceActionRequest(NonconformanceAction.Hold, null));
        var held = await admin.GetFromJsonAsync<Core.Contracts.Inventory.LotResponse>(
            $"/api/inventory/lots/{lot.Id}");
        Assert.Equal(LotStockStatus.OnHold, held!.StockStatus);

        // 特採へ変更 → 対応記録 → 承認でロット正常化（C-30-20-03）
        await admin.PostAsJsonAsync($"/api/nonconformances/{nc.Id}/instruct",
            new NonconformanceActionRequest(NonconformanceAction.SpecialAcceptance, "顧客承認済みのため特採"));
        await admin.PostAsJsonAsync($"/api/nonconformances/{nc.Id}/record-action",
            new NonconformanceActionRecordRequest("特採処理を実施"));
        (await admin.PostAsync($"/api/nonconformances/{nc.Id}/approve", null)).EnsureSuccessStatusCode();
        var released = await admin.GetFromJsonAsync<Core.Contracts.Inventory.LotResponse>(
            $"/api/inventory/lots/{lot.Id}");
        Assert.Equal(LotStockStatus.Normal, released!.StockStatus);
    }

    [Fact]
    public async Task トレースバックとトレースフォワードで部材と製品の連鎖を辿れる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var materialLot = await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 100m, ctx.MaterialLocationId);
        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);

        // 部材投入（トレーサビリティの連鎖を作る）＋最終工程で在庫計上
        (await admin.PostAsJsonAsync($"/api/work-orders/{order.WorkOrders[0].Id}/consumptions",
            new Core.Contracts.Execution.ConsumptionRequest(materialLot.Id, ctx.MaterialLocationId, 20m)))
            .EnsureSuccessStatusCode();
        var posted = await admin.PostAsJsonAsync(
            $"/api/work-orders/{order.WorkOrders[1].Id}/production-records",
            new Core.Contracts.Execution.ProductionRecordRequest(
                10m, 0m, DateTimeOffset.Now, null, ctx.ProductLocationId, false));
        var record = await posted.Content.ReadFromJsonAsync<Core.Contracts.Execution.ProductionRecordResponse>();
        var outputLotId = record!.OutputLotId!.Value;

        // トレースバック：産出ロット → 投入部材ロット（H-30-10-01）
        var back = await admin.GetFromJsonAsync<TraceResponse>($"/api/traceability/{outputLotId}/back");
        Assert.Contains(back!.Nodes, n => n.LotId == materialLot.Id && n.Quantity == 20m);

        // トレースフォワード：部材ロット → 産出ロット（H-30-10-02）
        var forward = await admin.GetFromJsonAsync<TraceResponse>($"/api/traceability/{materialLot.Id}/forward");
        Assert.Contains(forward!.Nodes, n => n.LotId == outputLotId);

        // 履歴閲覧（H-30-10-03〜05）
        var history = await admin.GetFromJsonAsync<LotHistoryResponse>($"/api/traceability/{outputLotId}/history");
        Assert.NotEmpty(history!.ProductionHistory);
        Assert.NotEmpty(history.InventoryHistory);
    }

    [Fact]
    public async Task 統合したロットも系譜として前方追跡できる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);

        // 同一品目の部材ロット2つ。sourceをtargetへ統合し、targetだけを工程へ投入する
        var source = await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 40m, ctx.MaterialLocationId);
        var target = await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 60m, ctx.MaterialLocationId);
        (await admin.PostAsJsonAsync("/api/inventory/merge",
            new Core.Contracts.Inventory.MergeRequest(source.Id, target.Id, ctx.MaterialLocationId)))
            .EnsureSuccessStatusCode();

        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);
        (await admin.PostAsJsonAsync($"/api/work-orders/{order.WorkOrders[0].Id}/consumptions",
            new Core.Contracts.Execution.ConsumptionRequest(target.Id, ctx.MaterialLocationId, 20m)))
            .EnsureSuccessStatusCode();
        var posted = await admin.PostAsJsonAsync(
            $"/api/work-orders/{order.WorkOrders[1].Id}/production-records",
            new Core.Contracts.Execution.ProductionRecordRequest(
                10m, 0m, DateTimeOffset.Now, null, ctx.ProductLocationId, false));
        var record = await posted.Content.ReadFromJsonAsync<Core.Contracts.Execution.ProductionRecordResponse>();
        var outputLotId = record!.OutputLotId!.Value;

        // 前方追跡：統合元ロット → 統合先ロット → 産出ロット（統合の系譜がないとここで切れる）
        var forward = await admin.GetFromJsonAsync<TraceResponse>($"/api/traceability/{source.Id}/forward");
        var merged = Assert.Single(forward!.Nodes, n => n.LotId == target.Id);
        Assert.Equal(LotRelationType.Merge, merged.Relation);
        Assert.Equal(40m, merged.Quantity);
        Assert.Contains(merged.Children, n => n.LotId == outputLotId);

        // 後方追跡：産出ロット → 投入した統合先ロット → 統合元ロット
        var back = await admin.GetFromJsonAsync<TraceResponse>($"/api/traceability/{outputLotId}/back");
        var consumed = Assert.Single(back!.Nodes, n => n.LotId == target.Id);
        Assert.Contains(consumed.Children, n => n.LotId == source.Id && n.Relation == LotRelationType.Merge);
    }

    [Fact]
    public async Task ロットの保留と解除が状態履歴に残る()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var lot = await Phase3TestData.ReceiveAsync(admin, ctx.ProductId, 100m, ctx.ProductLocationId);

        (await admin.PostAsJsonAsync("/api/inventory/status",
            new Core.Contracts.Inventory.LotStatusRequest(lot.Id, LotStockStatus.OnHold, "異臭の調査のため")))
            .EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/inventory/status",
            new Core.Contracts.Inventory.LotStatusRequest(lot.Id, LotStockStatus.Normal, "調査完了・問題なし")))
            .EnsureSuccessStatusCode();
        // 現在値と同じ変更は履歴を作らない
        (await admin.PostAsJsonAsync("/api/inventory/status",
            new Core.Contracts.Inventory.LotStatusRequest(lot.Id, LotStockStatus.Normal, "重複操作")))
            .EnsureSuccessStatusCode();

        var history = await admin.GetFromJsonAsync<LotHistoryResponse>($"/api/traceability/{lot.Id}/history");
        Assert.Equal(2, history!.StatusHistory.Count);

        var hold = history.StatusHistory[0];
        Assert.Equal(LotStockStatus.Normal, hold.FromStatus);
        Assert.Equal(LotStockStatus.OnHold, hold.ToStatus);
        Assert.Equal(LotStatusChangeSource.Manual, hold.Source);
        Assert.Equal("異臭の調査のため", hold.Reason);
        Assert.NotNull(hold.ChangedByName);

        var release = history.StatusHistory[1];
        Assert.Equal(LotStockStatus.OnHold, release.FromStatus);
        Assert.Equal(LotStockStatus.Normal, release.ToStatus);
        Assert.Equal("調査完了・問題なし", release.Reason);
    }

    [Fact]
    public async Task 検査実績を訂正すると訂正前の記録が訂正履歴として残る()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var item = await CreateFinalInspectionItemAsync(admin, ctx.ProductId); // 外径 9.5〜10.5
        var lot = await Phase3TestData.ReceiveAsync(admin, ctx.ProductId, 100m, ctx.ProductLocationId);

        var created = await admin.PostAsJsonAsync("/api/inspection-orders",
            new InspectionOrderCreateRequest(InspectionOrderType.FinalProduct, lot.Id, null, null, null));
        var order = (await created.Content.ReadFromJsonAsync<InspectionOrderResponse>())!;
        var results = await admin.PostAsJsonAsync($"/api/inspection-orders/{order.Id}/results",
            new List<InspectionResultRequest> { new(item.Id, 1, 10.0m, null, null) });
        var resultId = (await results.Content.ReadFromJsonAsync<InspectionOrderResponse>())!.Results[0].Id;

        // 1回目の訂正：10.0 → 11.0（規格外なので不合格へ）
        var corrected = await admin.PutAsJsonAsync(
            $"/api/inspection-orders/{order.Id}/results/{resultId}",
            new InspectionResultCorrectionRequest(11.0m, null, InspectionJudgment.Fail, "測定器の読み違い"));
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        var afterFirst = (await corrected.Content.ReadFromJsonAsync<InspectionOrderResponse>())!;

        var first = Assert.Single(afterFirst.Corrections);
        Assert.Equal(10.0m, first.BeforeMeasuredValue);
        Assert.Equal(InspectionJudgment.Pass, first.BeforeJudgment);
        Assert.Equal(11.0m, first.AfterMeasuredValue);
        Assert.Equal(InspectionJudgment.Fail, first.AfterJudgment);
        Assert.Equal("測定器の読み違い", first.Reason);
        Assert.Equal("INS-01", first.ItemCode);
        Assert.NotNull(first.CorrectedByName);

        // 2回目の訂正：訂正履歴は積み上がり、1回目の記録も残る
        (await admin.PutAsJsonAsync($"/api/inspection-orders/{order.Id}/results/{resultId}",
            new InspectionResultCorrectionRequest(10.2m, null, InspectionJudgment.Pass, "再測定")))
            .EnsureSuccessStatusCode();

        var reloaded = await admin.GetFromJsonAsync<InspectionOrderResponse>(
            $"/api/inspection-orders/{order.Id}");
        Assert.Equal(2, reloaded!.Corrections.Count);
        Assert.Equal(10.0m, reloaded.Corrections[0].BeforeMeasuredValue);
        Assert.Equal(11.0m, reloaded.Corrections[1].BeforeMeasuredValue);
        Assert.Equal(10.2m, reloaded.Corrections[1].AfterMeasuredValue);
        Assert.Equal("再測定", reloaded.Corrections[1].Reason);
        // 実績自体は最新の値になっている
        Assert.Equal(10.2m, reloaded.Results[0].MeasuredValue);
    }

    [Fact]
    public async Task 品質分析サマリで不良集計と不適合状況を取得できる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 100m, ctx.MaterialLocationId);
        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);
        var reason = await admin.PostAsJsonAsync("/api/defect-reasons",
            new Core.Contracts.Masters.DefectReasonRequest("DF-01", "寸法外れ", DefectReasonCategory.Process));
        var reasonId = (await reason.Content.ReadFromJsonAsync<Core.Contracts.Masters.DefectReasonResponse>())!.Id;
        await admin.PostAsJsonAsync($"/api/work-orders/{order.WorkOrders[1].Id}/production-records",
            new Core.Contracts.Execution.ProductionRecordRequest(
                8m, 2m, DateTimeOffset.Now, null, ctx.ProductLocationId, false,
                ScrapQuantity: 1m, ReworkQuantity: 1m,
                Defects: [new(reasonId, 2m)]));
        await admin.PostAsJsonAsync("/api/nonconformances",
            new NonconformanceCreateRequest(NonconformanceSource.Production, null,
                order.WorkOrders[1].Id, null, "不良2個", "設備不調", null));

        var summary = await admin.GetFromJsonAsync<QualitySummaryResponse>("/api/quality/summary");
        var productRow = summary!.ByProduct.Single(r => r.Key == "FG-01");
        Assert.Equal(8m, productRow.GoodQuantity);
        Assert.Equal(2m, productRow.DefectQuantity);
        Assert.Equal(20m, productRow.DefectRate);
        // 廃棄・再作業待ちは不良数の内訳として集計される（不良率の算出には影響しない）
        Assert.Equal(1m, productRow.ScrapQuantity);
        Assert.Equal(1m, productRow.ReworkQuantity);
        // 不良理由別の集計（C-40-10-01）。1理由のみなので構成比は100%
        var reasonRow = Assert.Single(summary.ByDefectReason);
        Assert.Equal("DF-01", reasonRow.Code);
        Assert.Equal(2m, reasonRow.Quantity);
        Assert.Equal(100m, reasonRow.Share);
        Assert.Equal(1, summary.OpenNonconformanceCount);
        Assert.True(summary.NonconformanceByCause.ContainsKey("設備不調"));
        // 直を登録していない運用では「（直なし）」にまとまる
        Assert.Equal(ShiftLabels.NoShift, Assert.Single(summary.ByShift).Key);
    }

    [Fact]
    public async Task 生産実績は記録時の直で固定され直別に不良率を比べられる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 100m, ctx.MaterialLocationId);

        (await admin.PostAsJsonAsync("/api/shifts",
            new Core.Contracts.Masters.ShiftRequest("D", "昼勤", new TimeOnly(6, 0), new TimeOnly(18, 0))))
            .EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/shifts",
            new Core.Contracts.Masters.ShiftRequest("N", "夜勤", new TimeOnly(18, 0), new TimeOnly(6, 0))))
            .EnsureSuccessStatusCode();

        // 昼勤の時間帯の実績（開始時刻から直を引く）
        var dayOrder = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);
        (await admin.PostAsJsonAsync($"/api/work-orders/{dayOrder.WorkOrders[1].Id}/production-records",
            new Core.Contracts.Execution.ProductionRecordRequest(
                10m, 0m, AtLocalTime(10, 0), null, ctx.ProductLocationId, false)))
            .EnsureSuccessStatusCode();

        // 夜勤の時間帯（日跨ぎの手前側）の実績
        var nightOrder = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);
        var nightPost = await admin.PostAsJsonAsync(
            $"/api/work-orders/{nightOrder.WorkOrders[1].Id}/production-records",
            new Core.Contracts.Execution.ProductionRecordRequest(
                6m, 4m, AtLocalTime(22, 0), null, ctx.ProductLocationId, false));
        nightPost.EnsureSuccessStatusCode();
        var nightRecord = (await nightPost.Content
            .ReadFromJsonAsync<Core.Contracts.Execution.ProductionRecordResponse>())!;

        // 記録した直は集計だけでなく実績照会でも辿れる（どの直に入った実績かを現場が確認できる）
        Assert.Equal("N", nightRecord.ShiftCode);
        var listed = await admin.GetFromJsonAsync<List<Core.Contracts.Execution.ProductionRecordResponse>>(
            $"/api/work-orders/{nightOrder.WorkOrders[1].Id}/production-records");
        Assert.Equal("夜勤", Assert.Single(listed!).ShiftName);

        // ロット履歴にも作業者と並べて出す（H-30-10-03）
        var history = await admin.GetFromJsonAsync<LotHistoryResponse>(
            $"/api/traceability/{nightRecord.OutputLotId}/history");
        Assert.Contains("直: N 夜勤", Assert.Single(history!.ProductionHistory), StringComparison.Ordinal);

        // 数量を訂正しても直は動かない（記録時に固定した値）
        var corrected = await admin.PutAsJsonAsync($"/api/production-records/{nightRecord.Id}",
            new Core.Contracts.Execution.ProductionRecordCorrectionRequest(6m, 4m, "検査結果の反映"));
        corrected.EnsureSuccessStatusCode();
        Assert.Equal("N", (await corrected.Content
            .ReadFromJsonAsync<Core.Contracts.Execution.ProductionRecordResponse>())!.ShiftCode);

        var summary = await admin.GetFromJsonAsync<QualitySummaryResponse>("/api/quality/summary");
        var day = summary!.ByShift.Single(r => r.Key == "D 昼勤");
        var night = summary.ByShift.Single(r => r.Key == "N 夜勤");
        Assert.Equal(0m, day.DefectRate);
        Assert.Equal(40m, night.DefectRate);

        // 直の時間帯定義を変えても、記録済みの実績の直は動かない（Spec.md 5.7）
        var shifts = await admin.GetFromJsonAsync<List<Core.Contracts.Masters.ShiftResponse>>("/api/shifts");
        var nightId = shifts!.Single(s => s.Code == "N").Id;
        (await admin.PutAsJsonAsync($"/api/shifts/{nightId}",
            new Core.Contracts.Masters.ShiftRequest("N", "夜勤", new TimeOnly(18, 0), new TimeOnly(6, 0))))
            .EnsureSuccessStatusCode();
        var after = await admin.GetFromJsonAsync<QualitySummaryResponse>("/api/quality/summary");
        Assert.Equal(40m, after!.ByShift.Single(r => r.Key == "N 夜勤").DefectRate);
    }

    /// <summary>その日の指定時刻（サーバーのローカル時刻）。直の判定は工場のローカル時刻で行うため</summary>
    private static DateTimeOffset AtLocalTime(int hour, int minute) =>
        new(DateTime.Today.AddHours(hour).AddMinutes(minute), DateTimeOffset.Now.Offset);

    [Fact]
    public async Task 品質系操作は担当ロールのみ実行できる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var item = await CreateFinalInspectionItemAsync(admin, ctx.ProductId);
        var lot = await Phase3TestData.ReceiveAsync(admin, ctx.ProductId, 100m, ctx.ProductLocationId);
        using var operator_ = await TestAuth.CreateUserClientAsync(
            factory, admin, "operator1", "Passw0rd123", MesRoles.Operator);
        using var qc = await TestAuth.CreateUserClientAsync(
            factory, admin, "qc1", "Passw0rd123", MesRoles.QualityControl);
        using var qa = await TestAuth.CreateUserClientAsync(
            factory, admin, "qa1", "Passw0rd123", MesRoles.QualityAssurance);

        // 作業者は検査指示を発行できない／不適合の起票はできる
        var byOperator = await operator_.PostAsJsonAsync("/api/inspection-orders",
            new InspectionOrderCreateRequest(InspectionOrderType.FinalProduct, lot.Id, null, null, null));
        Assert.Equal(HttpStatusCode.Forbidden, byOperator.StatusCode);
        var ncByOperator = await operator_.PostAsJsonAsync("/api/nonconformances",
            new NonconformanceCreateRequest(NonconformanceSource.Production, lot.Id, null, null, "逸脱", null, null));
        Assert.Equal(HttpStatusCode.Created, ncByOperator.StatusCode);

        // 品質管理は検査指示を発行できる／出荷判定はできない
        var byQc = await qc.PostAsJsonAsync("/api/inspection-orders",
            new InspectionOrderCreateRequest(InspectionOrderType.FinalProduct, lot.Id, null, [item.Id], null));
        Assert.Equal(HttpStatusCode.Created, byQc.StatusCode);
        var judgeByQc = await qc.PostAsJsonAsync("/api/shipment-judgments",
            new ShipmentJudgmentCreateRequest(lot.Id, null, ShipmentJudgmentResult.Approved, null));
        Assert.Equal(HttpStatusCode.Forbidden, judgeByQc.StatusCode);

        // 品質保証は出荷判定できる
        var judgeByQa = await qa.PostAsJsonAsync("/api/shipment-judgments",
            new ShipmentJudgmentCreateRequest(lot.Id, null, ShipmentJudgmentResult.Approved, null));
        Assert.Equal(HttpStatusCode.Created, judgeByQa.StatusCode);
    }

    [Fact]
    public async Task 不良理由別の集計が多い順に累積構成比を持つ()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 100m, ctx.MaterialLocationId);
        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);

        async Task<int> CreateReasonAsync(string code, string name)
        {
            var response = await admin.PostAsJsonAsync("/api/defect-reasons",
                new Core.Contracts.Masters.DefectReasonRequest(code, name, DefectReasonCategory.Process));
            response.EnsureSuccessStatusCode();
            return (await response.Content
                .ReadFromJsonAsync<Core.Contracts.Masters.DefectReasonResponse>())!.Id;
        }

        var major = await CreateReasonAsync("DF-01", "寸法外れ");
        var minor = await CreateReasonAsync("DF-02", "キズ");
        (await admin.PostAsJsonAsync($"/api/work-orders/{order.WorkOrders[1].Id}/production-records",
                new Core.Contracts.Execution.ProductionRecordRequest(
                    2m, 8m, DateTimeOffset.Now, null, ctx.ProductLocationId, false,
                    Defects: [new(major, 6m), new(minor, 2m)])))
            .EnsureSuccessStatusCode();

        var summary = await admin.GetFromJsonAsync<QualitySummaryResponse>("/api/quality/summary");

        // 多い順に並び、累積構成比は最後で100%になる（パレート図として読む。C-40-10-01）
        Assert.Equal("DF-01", summary!.ByDefectReason[0].Code);
        Assert.Equal(75m, summary.ByDefectReason[0].Share);
        Assert.Equal(75m, summary.ByDefectReason[0].CumulativeShare);
        Assert.Equal(25m, summary.ByDefectReason[1].Share);
        Assert.Equal(100m, summary.ByDefectReason[1].CumulativeShare);
    }
}
