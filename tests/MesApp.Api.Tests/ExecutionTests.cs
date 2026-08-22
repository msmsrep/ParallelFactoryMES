using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MesApp.Core.Contracts.Execution;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Contracts.Production;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MesApp.Api.Tests;

public class ExecutionTests
{
    [Fact]
    public async Task 着手から実績入力承認まで通しで動作し最終工程で在庫計上される()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 100m, ctx.MaterialLocationId);
        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);
        var firstWo = order.WorkOrders[0];  // 工順1
        var finalWo = order.WorkOrders[1];  // 工順2（最終）

        // 着手（B-30-30-01）
        var started = await admin.PostAsync($"/api/work-orders/{firstWo.Id}/start", null);
        Assert.Equal(HttpStatusCode.NoContent, started.StatusCode);

        // 工順1の実績（非最終工程 → 在庫計上なし）
        var record1 = await admin.PostAsJsonAsync($"/api/work-orders/{firstWo.Id}/production-records",
            new ProductionRecordRequest(10m, 0m, DateTimeOffset.Now.AddHours(-1), DateTimeOffset.Now, null, false));
        Assert.Equal(HttpStatusCode.OK, record1.StatusCode);
        var body1 = await record1.Content.ReadFromJsonAsync<ProductionRecordResponse>();
        Assert.Null(body1!.OutputLotId);

        // 最終工程の実績：入庫先未指定は400
        var noLocation = await admin.PostAsJsonAsync($"/api/work-orders/{finalWo.Id}/production-records",
            new ProductionRecordRequest(9m, 1m, DateTimeOffset.Now.AddHours(-1), DateTimeOffset.Now, null, false));
        Assert.Equal(HttpStatusCode.BadRequest, noLocation.StatusCode);

        // 最終工程の実績 → 産出ロットへ在庫計上（B-40-10-02）
        var record2 = await admin.PostAsJsonAsync($"/api/work-orders/{finalWo.Id}/production-records",
            new ProductionRecordRequest(9m, 1m, DateTimeOffset.Now.AddHours(-1), DateTimeOffset.Now,
                ctx.ProductLocationId, false));
        Assert.Equal(HttpStatusCode.OK, record2.StatusCode);
        var body2 = await record2.Content.ReadFromJsonAsync<ProductionRecordResponse>();
        Assert.NotNull(body2!.OutputLotId);
        Assert.Equal(order.Order.OutputLotNumber, body2.OutputLotNumber);
        Assert.Equal(9m, await Phase3TestData.GetStockQuantityAsync(admin, body2.OutputLotId!.Value));

        // 製造完了承認（B-40-10-10）→ 全作業指示承認で指図完了
        (await admin.PostAsync($"/api/work-orders/{firstWo.Id}/approve", null)).EnsureSuccessStatusCode();
        (await admin.PostAsync($"/api/work-orders/{finalWo.Id}/approve", null)).EnsureSuccessStatusCode();
        var detail = await admin.GetFromJsonAsync<ManufacturingOrderDetailResponse>(
            $"/api/manufacturing-orders/{order.Order.Id}");
        Assert.Equal(ManufacturingOrderStatus.Completed, detail!.Order.Status);
        Assert.All(detail.WorkOrders, w => Assert.Equal(WorkOrderStatus.Approved, w.Status));
    }

    [Fact]
    public async Task 部材投入で在庫が引き落とされ投入実績が残る()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var materialLot = await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 100m, ctx.MaterialLocationId);
        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);
        var workOrderId = order.WorkOrders[0].Id;

        var consumed = await admin.PostAsJsonAsync($"/api/work-orders/{workOrderId}/consumptions",
            new ConsumptionRequest(materialLot.Id, ctx.MaterialLocationId, 20m));
        Assert.Equal(HttpStatusCode.OK, consumed.StatusCode);
        var body = await consumed.Content.ReadFromJsonAsync<ConsumptionResponse>();
        Assert.Equal(ConsumptionMethod.Manual, body!.Method);
        Assert.Equal(80m, await Phase3TestData.GetStockQuantityAsync(admin, materialLot.Id));

        // 在庫超過の投入は400
        var over = await admin.PostAsJsonAsync($"/api/work-orders/{workOrderId}/consumptions",
            new ConsumptionRequest(materialLot.Id, ctx.MaterialLocationId, 999m));
        Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);

        // 保留ロットは投入不可
        await admin.PostAsJsonAsync("/api/inventory/status",
            new Core.Contracts.Inventory.LotStatusRequest(materialLot.Id, LotStockStatus.OnHold, null));
        var onHold = await admin.PostAsJsonAsync($"/api/work-orders/{workOrderId}/consumptions",
            new ConsumptionRequest(materialLot.Id, ctx.MaterialLocationId, 1m));
        Assert.Equal(HttpStatusCode.BadRequest, onHold.StatusCode);
    }

    [Fact]
    public async Task MBOMに含まれない品目のロットは投入できない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin); // FG-01 の MBOM は RM-01 のみ
        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);
        var workOrderId = order.WorkOrders[0].Id;

        // MBOMにない品目（RM-02）のロットは、在庫があっても投入できない
        var other = await MasterTests.CreateProductAsync(admin, "RM-02", "別部材", ProductType.Material);
        var otherLot = await Phase3TestData.ReceiveAsync(admin, other.Id, 100m, ctx.MaterialLocationId);
        var rejected = await admin.PostAsJsonAsync($"/api/work-orders/{workOrderId}/consumptions",
            new ConsumptionRequest(otherLot.Id, ctx.MaterialLocationId, 5m));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(100m, await Phase3TestData.GetStockQuantityAsync(admin, otherLot.Id));

        // MBOMの部材なら投入できる
        var materialLot = await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 100m, ctx.MaterialLocationId);
        var accepted = await admin.PostAsJsonAsync($"/api/work-orders/{workOrderId}/consumptions",
            new ConsumptionRequest(materialLot.Id, ctx.MaterialLocationId, 20m));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        // 代替部品としてMBOMへ登録すれば投入できるようになる（A-40-10-04）
        (await admin.PutAsJsonAsync($"/api/products/{ctx.ProductId}/bom",
            new List<Core.Contracts.Masters.BomItemRequest>
            {
                new(ctx.MaterialId, Phase3TestData.BomQuantityPer, MakeOrBuy.InHouse, "G1"),
                new(other.Id, Phase3TestData.BomQuantityPer, MakeOrBuy.InHouse, "G1"),
            })).EnsureSuccessStatusCode();
        var retried = await admin.PostAsJsonAsync($"/api/work-orders/{workOrderId}/consumptions",
            new ConsumptionRequest(otherLot.Id, ctx.MaterialLocationId, 5m));
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal(95m, await Phase3TestData.GetStockQuantityAsync(admin, otherLot.Id));
    }

    [Fact]
    public async Task MBOM未登録の品目には部材を投入できない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);

        // MBOMを持たない品目（工順のみ）を作る
        var noBom = await MasterTests.CreateProductAsync(admin, "FG-02", "MBOM未登録品", ProductType.Product);
        (await admin.PutAsJsonAsync($"/api/products/{noBom.Id}/routing",
            new List<Core.Contracts.Masters.RoutingStepRequest>
            {
                new(1, ctx.ProcessId, 30m, 10m, null, null, null, null, null),
            })).EnsureSuccessStatusCode();
        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, noBom.Id, 10m);
        var materialLot = await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 100m, ctx.MaterialLocationId);

        // 照合の基準がないため投入は拒否される（MBOMの整備を促す）
        var rejected = await admin.PostAsJsonAsync(
            $"/api/work-orders/{order.WorkOrders[0].Id}/consumptions",
            new ConsumptionRequest(materialLot.Id, ctx.MaterialLocationId, 5m));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(100m, await Phase3TestData.GetStockQuantityAsync(admin, materialLot.Id));
    }

    [Fact]
    public async Task 有効期限切れの部材ロットは投入もバックフラッシュもできない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var expired = await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 100m, ctx.MaterialLocationId,
            // 業務日付の境界（既定6時）で前日扱いになる時間帯でも確実に期限切れになるよう2日前にする
            expiresOn: DateOnly.FromDateTime(DateTime.Today).AddDays(-2));
        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);

        // 手動投入は拒否される
        var manual = await admin.PostAsJsonAsync($"/api/work-orders/{order.WorkOrders[0].Id}/consumptions",
            new ConsumptionRequest(expired.Id, ctx.MaterialLocationId, 20m));
        Assert.Equal(HttpStatusCode.BadRequest, manual.StatusCode);
        Assert.Equal(100m, await Phase3TestData.GetStockQuantityAsync(admin, expired.Id));

        // FEFO引当（バックフラッシュ）の対象からも除外され、他に在庫がなければ失敗する
        var record = await admin.PostAsJsonAsync(
            $"/api/work-orders/{order.WorkOrders[1].Id}/production-records",
            new ProductionRecordRequest(10m, 0m, DateTimeOffset.Now, null, ctx.ProductLocationId, Backflush: true));
        Assert.Equal(HttpStatusCode.BadRequest, record.StatusCode);

        // 期限内のロットを追加すればそちらが引き当てられる
        var valid = await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 100m, ctx.MaterialLocationId,
            expiresOn: DateOnly.FromDateTime(DateTime.Today).AddDays(30));
        var retried = await admin.PostAsJsonAsync(
            $"/api/work-orders/{order.WorkOrders[1].Id}/production-records",
            new ProductionRecordRequest(10m, 0m, DateTimeOffset.Now, null, ctx.ProductLocationId, Backflush: true));
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal(100m, await Phase3TestData.GetStockQuantityAsync(admin, expired.Id));
        Assert.Equal(80m, await Phase3TestData.GetStockQuantityAsync(admin, valid.Id));
    }

    [Fact]
    public async Task バックフラッシュでMBOM数量分の部材が自動消費される()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var materialLot = await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 100m, ctx.MaterialLocationId);
        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);
        var finalWo = order.WorkOrders[1];

        // 良品9＋不良1 → 部材消費 = 2 × 10 = 20（B-40-10-09）
        var record = await admin.PostAsJsonAsync($"/api/work-orders/{finalWo.Id}/production-records",
            new ProductionRecordRequest(9m, 1m, DateTimeOffset.Now.AddHours(-1), DateTimeOffset.Now,
                ctx.ProductLocationId, Backflush: true));
        Assert.Equal(HttpStatusCode.OK, record.StatusCode);
        Assert.Equal(80m, await Phase3TestData.GetStockQuantityAsync(admin, materialLot.Id));

        var consumptions = await admin.GetFromJsonAsync<List<ConsumptionResponse>>(
            $"/api/work-orders/{finalWo.Id}/consumptions");
        Assert.Single(consumptions!);
        Assert.Equal(ConsumptionMethod.Backflush, consumptions![0].Method);
        Assert.Equal(20m, consumptions[0].Quantity);
    }

    [Fact]
    public async Task 部材在庫が不足するとバックフラッシュは失敗する()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 5m, ctx.MaterialLocationId); // 20必要だが5しかない
        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);

        var record = await admin.PostAsJsonAsync(
            $"/api/work-orders/{order.WorkOrders[1].Id}/production-records",
            new ProductionRecordRequest(10m, 0m, DateTimeOffset.Now, null, ctx.ProductLocationId, Backflush: true));
        Assert.Equal(HttpStatusCode.BadRequest, record.StatusCode);

        // 失敗時は在庫・実績とも変化しない
        var consumptions = await admin.GetFromJsonAsync<List<ConsumptionResponse>>(
            $"/api/work-orders/{order.WorkOrders[1].Id}/consumptions");
        Assert.Empty(consumptions!);
    }

    [Fact]
    public async Task 段取り実績とチェックリストと製造条件データを記録できる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);
        var workOrderId = order.WorkOrders[0].Id;

        // 段取り実績（前段取り。B-20-50）
        var setup = await admin.PostAsJsonAsync($"/api/work-orders/{workOrderId}/setup-records",
            new SetupRecordRequest(SetupType.Pre, DateTimeOffset.Now.AddMinutes(-30), DateTimeOffset.Now, null));
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);

        // チェックリスト（必須項目未チェックは400）
        var checklistCreated = await admin.PostAsJsonAsync("/api/checklists",
            new ChecklistRequest("CL-01", "作業前確認", ChecklistCategory.Process,
                [new(1, "安全確認", true), new(2, "任意確認", false)]));
        var checklist = await checklistCreated.Content.ReadFromJsonAsync<ChecklistResponse>();
        var requiredItemId = checklist!.Items.First(i => i.IsRequired).Id;

        var unchecked_ = await admin.PostAsJsonAsync($"/api/work-orders/{workOrderId}/checklist-records",
            new ChecklistRecordRequest(checklist.Id, [new(requiredItemId, false, null)]));
        Assert.Equal(HttpStatusCode.BadRequest, unchecked_.StatusCode);

        var checked_ = await admin.PostAsJsonAsync($"/api/work-orders/{workOrderId}/checklist-records",
            new ChecklistRecordRequest(checklist.Id, [new(requiredItemId, true, "OK")]));
        Assert.Equal(HttpStatusCode.OK, checked_.StatusCode);
        var checklistRecord = await checked_.Content.ReadFromJsonAsync<ChecklistRecordResponse>();
        Assert.Equal(2, checklistRecord!.Results.Count); // 全項目分の結果が記録される

        // 製造条件データ（B-30-30-04）
        var data = await admin.PostAsJsonAsync($"/api/work-orders/{workOrderId}/data-records",
            new List<DataRecordRequest> { new("温度", "185"), new("回転数", "1200") });
        Assert.Equal(HttpStatusCode.OK, data.StatusCode);
        var records = await data.Content.ReadFromJsonAsync<List<DataRecordResponse>>();
        Assert.Equal(2, records!.Count);
    }

    [Fact]
    public async Task トラブル報告と対応履歴と作業時間を記録できる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);
        var workOrderId = order.WorkOrders[0].Id;

        // トラブル報告（B-40-10-06）
        var reported = await admin.PostAsJsonAsync("/api/trouble-reports",
            new TroubleReportRequest(DateTimeOffset.Now, TroubleCategory.Quality, workOrderId, null, "寸法不良多発"));
        Assert.Equal(HttpStatusCode.Created, reported.StatusCode);
        var trouble = await reported.Content.ReadFromJsonAsync<TroubleReportResponse>();
        Assert.Equal(TroubleStatus.Open, trouble!.Status);

        // 対応履歴の追記＋完了（B-60-10-03〜04）
        var updated = await admin.PutAsJsonAsync($"/api/trouble-reports/{trouble.Id}",
            new TroubleUpdateRequest("金型を交換して復旧", TroubleStatus.Closed));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var closed = await updated.Content.ReadFromJsonAsync<TroubleReportResponse>();
        Assert.Equal(TroubleStatus.Closed, closed!.Status);
        Assert.Contains("金型を交換して復旧", closed.ResponseHistory);

        // 作業時間：直接作業は作業指示必須（B-30-30-02）
        var directNoWo = await admin.PostAsJsonAsync("/api/work-time-records",
            new WorkTimeRequest(WorkTimeType.Direct, null, null, DateTimeOffset.Now.AddHours(-2), DateTimeOffset.Now, null));
        Assert.Equal(HttpStatusCode.BadRequest, directNoWo.StatusCode);

        // 間接時間（F-30-20-02）
        var indirect = await admin.PostAsJsonAsync("/api/work-time-records",
            new WorkTimeRequest(WorkTimeType.Indirect, "設備メンテ", null,
                DateTimeOffset.Now.AddHours(-1), DateTimeOffset.Now, null));
        Assert.Equal(HttpStatusCode.Created, indirect.StatusCode);

        var list = await admin.GetFromJsonAsync<List<WorkTimeResponse>>("/api/work-time-records");
        Assert.Single(list!);
    }

    [Fact]
    public async Task 製造履歴訂正で実績と在庫が差分調整される()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 100m, ctx.MaterialLocationId);
        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);
        var finalWo = order.WorkOrders[1];

        var posted = await admin.PostAsJsonAsync($"/api/work-orders/{finalWo.Id}/production-records",
            new ProductionRecordRequest(10m, 0m, DateTimeOffset.Now, null, ctx.ProductLocationId, false));
        var record = await posted.Content.ReadFromJsonAsync<ProductionRecordResponse>();
        Assert.Equal(10m, await Phase3TestData.GetStockQuantityAsync(admin, record!.OutputLotId!.Value));

        // 良品10→8に訂正（B-70-30-01）→ 在庫も-2
        var corrected = await admin.PutAsJsonAsync($"/api/production-records/{record.Id}",
            new ProductionRecordCorrectionRequest(8m, 2m, "検査で2個不良判明"));
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        var correctedBody = await corrected.Content.ReadFromJsonAsync<ProductionRecordResponse>();
        Assert.Equal(8m, correctedBody!.GoodQuantity);
        Assert.Equal(8m, await Phase3TestData.GetStockQuantityAsync(admin, record.OutputLotId.Value));

        // 訂正前の値は業務履歴として残り、トレース画面の履歴から参照できる（B-70-30-01）
        var history = await admin.GetFromJsonAsync<Core.Contracts.Quality.LotHistoryResponse>(
            $"/api/traceability/{record.OutputLotId.Value}/history");
        var correction = Assert.Single(history!.CorrectionHistory);
        Assert.Equal(10m, correction.BeforeGoodQuantity);
        Assert.Equal(0m, correction.BeforeDefectQuantity);
        Assert.Equal(8m, correction.AfterGoodQuantity);
        Assert.Equal(2m, correction.AfterDefectQuantity);
        Assert.Equal("検査で2個不良判明", correction.Reason);
        Assert.Equal(finalWo.WorkOrderNo, correction.WorkOrderNo);
        Assert.NotNull(correction.CorrectedByName);

        // 訂正の監査証跡は変更前後と理由をJSONで残す（Spec.md 7.6）
        var detail = await GetLatestAuditDetailAsync(factory, "Execution", "Correct");
        using var json = JsonDocument.Parse(detail);
        Assert.Equal(10m, json.RootElement.GetProperty("before").GetProperty("good").GetDecimal());
        Assert.Equal(0m, json.RootElement.GetProperty("before").GetProperty("defect").GetDecimal());
        Assert.Equal(8m, json.RootElement.GetProperty("after").GetProperty("good").GetDecimal());
        Assert.Equal(2m, json.RootElement.GetProperty("after").GetProperty("defect").GetDecimal());
        Assert.Equal("検査で2個不良判明", json.RootElement.GetProperty("reason").GetString());
        // 日本語はエスケープせずそのまま保存する（監査ログは人が読む前提）
        Assert.Contains("検査で2個不良判明", detail);
    }

    /// <summary>指定した分類・操作の最新の監査ログ詳細を取得する（監査証跡の検証用）</summary>
    private static async Task<string> GetLatestAuditDetailAsync(
        ApiFactory factory, string category, string action)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesAppDbContext>();
        var log = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Category == category && a.Action == action)
            .OrderByDescending(a => a.Id)
            .FirstAsync();
        return log.Detail!;
    }

    [Fact]
    public async Task 作業者ロールは実績記録はできるが履歴訂正と完了承認はできない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 100m, ctx.MaterialLocationId);
        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);
        var finalWo = order.WorkOrders[1];
        using var operator_ = await TestAuth.CreateUserClientAsync(
            factory, admin, "operator1", "Passw0rd123", Core.Constants.MesRoles.Operator);

        // 作業者による実績記録は可能（B-40-10-01）
        var posted = await operator_.PostAsJsonAsync($"/api/work-orders/{finalWo.Id}/production-records",
            new ProductionRecordRequest(10m, 0m, DateTimeOffset.Now, null, ctx.ProductLocationId, false));
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        var record = await posted.Content.ReadFromJsonAsync<ProductionRecordResponse>();

        // 履歴訂正は生産管理ロールのみ（B-70-30-01）
        var correct = await operator_.PutAsJsonAsync($"/api/production-records/{record!.Id}",
            new ProductionRecordCorrectionRequest(5m, 5m, "試し"));
        Assert.Equal(HttpStatusCode.Forbidden, correct.StatusCode);

        // 完了承認も生産管理ロールのみ（B-40-10-10）
        var approve = await operator_.PostAsync($"/api/work-orders/{finalWo.Id}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, approve.StatusCode);
    }
}
