using System.Net;
using System.Net.Http.Json;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Contracts.Production;
using MesApp.Core.Contracts.Quality;
using MesApp.Core.Entities;

namespace MesApp.Api.Tests;

public class QualityTests
{
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
        var nonconformances = await admin.GetFromJsonAsync<List<NonconformanceResponse>>(
            "/api/nonconformances");
        Assert.Single(nonconformances!);
        Assert.Equal(NonconformanceSource.Inspection, nonconformances![0].Source);
        Assert.Equal(lot.Id, nonconformances[0].LotId);
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
        await admin.PostAsJsonAsync($"/api/work-orders/{order.WorkOrders[1].Id}/production-records",
            new Core.Contracts.Execution.ProductionRecordRequest(
                8m, 2m, DateTimeOffset.Now, null, ctx.ProductLocationId, false));
        await admin.PostAsJsonAsync("/api/nonconformances",
            new NonconformanceCreateRequest(NonconformanceSource.Production, null,
                order.WorkOrders[1].Id, null, "不良2個", "設備不調", null));

        var summary = await admin.GetFromJsonAsync<QualitySummaryResponse>("/api/quality/summary");
        var productRow = summary!.ByProduct.Single(r => r.Key == "FG-01");
        Assert.Equal(8m, productRow.GoodQuantity);
        Assert.Equal(2m, productRow.DefectQuantity);
        Assert.Equal(20m, productRow.DefectRate);
        Assert.Equal(1, summary.OpenNonconformanceCount);
        Assert.True(summary.NonconformanceByCause.ContainsKey("設備不調"));
    }

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
}
