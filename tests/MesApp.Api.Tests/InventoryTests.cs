using System.Net;
using System.Net.Http.Json;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Common;
using MesApp.Core.Contracts.Execution;
using MesApp.Core.Contracts.Inventory;
using MesApp.Core.Entities;

namespace MesApp.Api.Tests;

public class InventoryTests
{
    [Fact]
    public async Task 受入で在庫計上されロットが自動採番される()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);

        var lot = await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 100m, ctx.MaterialLocationId);
        Assert.StartsWith("RM-01-", lot.LotNumber);
        Assert.Equal(LotOriginType.Receiving, lot.OriginType);

        Assert.Equal(100m, await Phase3TestData.GetStockQuantityAsync(admin, lot.Id));

        // 在庫トランザクションに受入が記録される（一覧はページング応答）
        var transactions = await admin.GetFromJsonAsync<PagedResult<TransactionResponse>>(
            $"/api/inventory/transactions?lotId={lot.Id}");
        Assert.Equal(1, transactions!.Total);
        Assert.Equal(InventoryTransactionType.Receipt, Assert.Single(transactions.Items).Type);
    }

    [Fact]
    public async Task 受入取消は在庫未変動時のみ可能()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var lot = await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 100m, ctx.MaterialLocationId);

        var canceled = await admin.PostAsync($"/api/receiving/{lot.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.NoContent, canceled.StatusCode);
        Assert.Equal(0m, await Phase3TestData.GetStockQuantityAsync(admin, lot.Id));

        // 在庫が動いた後は取消不可
        var lot2 = await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 50m, ctx.MaterialLocationId);
        await admin.PostAsJsonAsync("/api/inventory/move",
            new MoveRequest(lot2.Id, ctx.MaterialLocationId, ctx.ProductLocationId, 10m));
        var rejected = await admin.PostAsync($"/api/receiving/{lot2.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
    }

    [Fact]
    public async Task 在庫の移動と数量調整ができる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var lot = await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 100m, ctx.MaterialLocationId);

        // 移動
        var moved = await admin.PostAsJsonAsync("/api/inventory/move",
            new MoveRequest(lot.Id, ctx.MaterialLocationId, ctx.ProductLocationId, 30m));
        Assert.Equal(HttpStatusCode.NoContent, moved.StatusCode);
        Assert.Equal(70m, await Phase3TestData.GetStockQuantityAsync(admin, lot.Id, ctx.MaterialLocationId));
        Assert.Equal(30m, await Phase3TestData.GetStockQuantityAsync(admin, lot.Id, ctx.ProductLocationId));

        // 在庫を超える移動は400
        var overMove = await admin.PostAsJsonAsync("/api/inventory/move",
            new MoveRequest(lot.Id, ctx.MaterialLocationId, ctx.ProductLocationId, 999m));
        Assert.Equal(HttpStatusCode.BadRequest, overMove.StatusCode);

        // 数量調整（理由必須）
        var adjusted = await admin.PostAsJsonAsync("/api/inventory/adjust",
            new AdjustRequest(lot.Id, ctx.MaterialLocationId, 65m, "実棚差異"));
        Assert.Equal(HttpStatusCode.NoContent, adjusted.StatusCode);
        Assert.Equal(65m, await Phase3TestData.GetStockQuantityAsync(admin, lot.Id, ctx.MaterialLocationId));
    }

    [Fact]
    public async Task ロットの分割と品目振替とステータス変更ができる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var lot = await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 100m, ctx.MaterialLocationId);

        // 分割：新ロットに30を移す（系譜＝親ロットID）
        var splitResponse = await admin.PostAsJsonAsync("/api/inventory/split",
            new SplitRequest(lot.Id, ctx.MaterialLocationId, 30m, null));
        Assert.Equal(HttpStatusCode.OK, splitResponse.StatusCode);
        var child = await splitResponse.Content.ReadFromJsonAsync<LotResponse>();
        Assert.Equal(lot.Id, child!.ParentLotId);
        Assert.Equal(70m, await Phase3TestData.GetStockQuantityAsync(admin, lot.Id));
        Assert.Equal(30m, await Phase3TestData.GetStockQuantityAsync(admin, child.Id));

        // 品目振替：子ロットの10を完成品品目へ振替
        var transferred = await admin.PostAsJsonAsync("/api/inventory/transfer",
            new LotTransferRequest(child.Id, ctx.MaterialLocationId, 10m, ctx.ProductId, null));
        Assert.Equal(HttpStatusCode.OK, transferred.StatusCode);
        var newLot = await transferred.Content.ReadFromJsonAsync<LotResponse>();
        Assert.Equal(ctx.ProductId, newLot!.ProductId);
        Assert.Equal(child.Id, newLot.ParentLotId);
        Assert.Equal(20m, await Phase3TestData.GetStockQuantityAsync(admin, child.Id));

        // ステータス変更（保留）
        var status = await admin.PostAsJsonAsync("/api/inventory/status",
            new LotStatusRequest(lot.Id, LotStockStatus.OnHold, "品質確認中"));
        Assert.Equal(HttpStatusCode.NoContent, status.StatusCode);
        var lotInfo = await admin.GetFromJsonAsync<LotResponse>($"/api/inventory/lots/{lot.Id}");
        Assert.Equal(LotStockStatus.OnHold, lotInfo!.StockStatus);
    }

    [Fact]
    public async Task ピッキングは有効期限の近いロットから引き当てられる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var today = DateOnly.FromDateTime(DateTime.Today);

        // 期限の遠いロットを先に受入、近いロットを後に受入
        var lotLater = await Phase3TestData.ReceiveAsync(
            admin, ctx.MaterialId, 50m, ctx.MaterialLocationId, expiresOn: today.AddDays(60));
        var lotSooner = await Phase3TestData.ReceiveAsync(
            admin, ctx.MaterialId, 50m, ctx.MaterialLocationId, expiresOn: today.AddDays(10));

        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);
        var workOrderId = order.WorkOrders[0].Id;

        // 60個 → 期限の近い50 + 遠い10 の2明細に引当（FEFO）
        var created = await admin.PostAsJsonAsync("/api/picking-orders",
            new PickingOrderCreateRequest(PickingOrderType.ProcessIssue, workOrderId, null,
                [new(ctx.MaterialId, 60m)]));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var picking = await created.Content.ReadFromJsonAsync<PickingOrderResponse>();
        Assert.Equal(2, picking!.Lines.Count);
        Assert.Equal(lotSooner.Id, picking.Lines[0].LotId);
        Assert.Equal(50m, picking.Lines[0].Quantity);
        Assert.Equal(lotLater.Id, picking.Lines[1].LotId);
        Assert.Equal(10m, picking.Lines[1].Quantity);

        // 実行で在庫が引き落とされる
        var executed = await admin.PostAsync($"/api/picking-orders/{picking.Id}/execute", null);
        Assert.Equal(HttpStatusCode.OK, executed.StatusCode);
        Assert.Equal(0m, await Phase3TestData.GetStockQuantityAsync(admin, lotSooner.Id));
        Assert.Equal(40m, await Phase3TestData.GetStockQuantityAsync(admin, lotLater.Id));

        // 払出戻し（D-20-20-03）
        var returned = await admin.PostAsJsonAsync("/api/inventory/issue-return",
            new IssueReturnRequest(lotSooner.Id, ctx.MaterialLocationId, 5m, workOrderId));
        Assert.Equal(HttpStatusCode.NoContent, returned.StatusCode);
        Assert.Equal(5m, await Phase3TestData.GetStockQuantityAsync(admin, lotSooner.Id));
    }

    [Fact]
    public async Task 在庫不足のピッキング指示は作成できない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 10m, ctx.MaterialLocationId);
        var order = await Phase3TestData.CreateReleasedOrderAsync(admin, ctx.ProductId, 10m);

        var created = await admin.PostAsJsonAsync("/api/picking-orders",
            new PickingOrderCreateRequest(PickingOrderType.ProcessIssue, order.WorkOrders[0].Id, null,
                [new(ctx.MaterialId, 100m)]));
        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
    }

    [Fact]
    public async Task 出荷指示から出荷実行で在庫が引き落とされ完了になる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var lot = await Phase3TestData.ReceiveAsync(admin, ctx.ProductId, 100m, ctx.ProductLocationId);

        var created = await admin.PostAsJsonAsync("/api/shipping-orders",
            new ShippingOrderCreateRequest("出荷先A", null, [new(ctx.ProductId, 60m)]));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var shipping = await created.Content.ReadFromJsonAsync<ShippingOrderResponse>();
        Assert.StartsWith("SH", shipping!.ShippingNo);

        // 出荷判定（承認済みの「可」）がないと出荷できない（H-10-10ゲート）
        var blocked = await admin.PostAsJsonAsync($"/api/shipping-orders/{shipping.Id}/ship",
            new ShipExecuteRequest([new(lot.Id, ctx.ProductLocationId, 20m)]));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        // 出荷判定→承認
        var judged = await admin.PostAsJsonAsync("/api/shipment-judgments",
            new Core.Contracts.Quality.ShipmentJudgmentCreateRequest(
                null, shipping.Id, ShipmentJudgmentResult.Approved, null));
        Assert.Equal(HttpStatusCode.Created, judged.StatusCode);
        var judgment = await judged.Content.ReadFromJsonAsync<Core.Contracts.Quality.ShipmentJudgmentResponse>();
        (await admin.PostAsync($"/api/shipment-judgments/{judgment!.Id}/approve", null)).EnsureSuccessStatusCode();

        // 指示数量超過は400
        var over = await admin.PostAsJsonAsync($"/api/shipping-orders/{shipping.Id}/ship",
            new ShipExecuteRequest([new(lot.Id, ctx.ProductLocationId, 70m)]));
        Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);

        // 部分出荷 → 指示中のまま
        var partial = await admin.PostAsJsonAsync($"/api/shipping-orders/{shipping.Id}/ship",
            new ShipExecuteRequest([new(lot.Id, ctx.ProductLocationId, 20m)]));
        Assert.Equal(HttpStatusCode.OK, partial.StatusCode);
        var afterPartial = await partial.Content.ReadFromJsonAsync<ShippingOrderResponse>();
        Assert.Equal(ShippingOrderStatus.Instructed, afterPartial!.Status);

        // 残数出荷 → 完了
        var final = await admin.PostAsJsonAsync($"/api/shipping-orders/{shipping.Id}/ship",
            new ShipExecuteRequest([new(lot.Id, ctx.ProductLocationId, 40m)]));
        var completed = await final.Content.ReadFromJsonAsync<ShippingOrderResponse>();
        Assert.Equal(ShippingOrderStatus.Completed, completed!.Status);
        Assert.Equal(40m, await Phase3TestData.GetStockQuantityAsync(admin, lot.Id));
    }

    [Fact]
    public async Task 出荷判定の承認後に保留になったロットは出荷できない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var lot = await Phase3TestData.ReceiveAsync(admin, ctx.ProductId, 100m, ctx.ProductLocationId);

        var created = await admin.PostAsJsonAsync("/api/shipping-orders",
            new ShippingOrderCreateRequest("出荷先A", null, [new(ctx.ProductId, 60m)]));
        var shipping = (await created.Content.ReadFromJsonAsync<ShippingOrderResponse>())!;
        var judged = await admin.PostAsJsonAsync("/api/shipment-judgments",
            new Core.Contracts.Quality.ShipmentJudgmentCreateRequest(
                null, shipping.Id, ShipmentJudgmentResult.Approved, null));
        var judgment = (await judged.Content.ReadFromJsonAsync<Core.Contracts.Quality.ShipmentJudgmentResponse>())!;
        (await admin.PostAsync($"/api/shipment-judgments/{judgment.Id}/approve", null)).EnsureSuccessStatusCode();

        // 判定は承認済みでも、ロットが保留になっていれば出荷できない
        await admin.PostAsJsonAsync("/api/inventory/status",
            new LotStatusRequest(lot.Id, LotStockStatus.OnHold, "調査中"));
        var held = await admin.PostAsJsonAsync($"/api/shipping-orders/{shipping.Id}/ship",
            new ShipExecuteRequest([new(lot.Id, ctx.ProductLocationId, 20m)]));
        Assert.Equal(HttpStatusCode.Conflict, held.StatusCode);
        Assert.Equal(100m, await Phase3TestData.GetStockQuantityAsync(admin, lot.Id));

        // 保留を解除すれば出荷できる
        await admin.PostAsJsonAsync("/api/inventory/status",
            new LotStatusRequest(lot.Id, LotStockStatus.Normal, "調査完了"));
        var shipped = await admin.PostAsJsonAsync($"/api/shipping-orders/{shipping.Id}/ship",
            new ShipExecuteRequest([new(lot.Id, ctx.ProductLocationId, 20m)]));
        Assert.Equal(HttpStatusCode.OK, shipped.StatusCode);
        Assert.Equal(80m, await Phase3TestData.GetStockQuantityAsync(admin, lot.Id));
    }

    [Fact]
    public async Task 有効期限切れのロットは出荷できない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var lot = await Phase3TestData.ReceiveAsync(admin, ctx.ProductId, 100m, ctx.ProductLocationId,
            // 業務日付の境界（既定6時）で前日扱いになる時間帯でも確実に期限切れになるよう2日前にする
            expiresOn: DateOnly.FromDateTime(DateTime.Today).AddDays(-2));

        var created = await admin.PostAsJsonAsync("/api/shipping-orders",
            new ShippingOrderCreateRequest("出荷先A", null, [new(ctx.ProductId, 60m)]));
        var shipping = (await created.Content.ReadFromJsonAsync<ShippingOrderResponse>())!;
        var judged = await admin.PostAsJsonAsync("/api/shipment-judgments",
            new Core.Contracts.Quality.ShipmentJudgmentCreateRequest(
                null, shipping.Id, ShipmentJudgmentResult.Approved, null));
        var judgment = (await judged.Content.ReadFromJsonAsync<Core.Contracts.Quality.ShipmentJudgmentResponse>())!;
        (await admin.PostAsync($"/api/shipment-judgments/{judgment.Id}/approve", null)).EnsureSuccessStatusCode();

        var expired = await admin.PostAsJsonAsync($"/api/shipping-orders/{shipping.Id}/ship",
            new ShipExecuteRequest([new(lot.Id, ctx.ProductLocationId, 20m)]));
        Assert.Equal(HttpStatusCode.Conflict, expired.StatusCode);
        Assert.Equal(100m, await Phase3TestData.GetStockQuantityAsync(admin, lot.Id));
    }

    [Fact]
    public async Task 棚卸で差異が調整される()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var lot1 = await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 100m, ctx.MaterialLocationId);
        var lot2 = await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 50m, ctx.MaterialLocationId);

        var created = await admin.PostAsJsonAsync("/api/stocktakes",
            new StocktakeCreateRequest(ctx.MaterialLocationId));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var stocktake = await created.Content.ReadFromJsonAsync<StocktakeResponse>();
        Assert.Equal(2, stocktake!.Lines.Count);

        // 実棚：lot1は95（差異-5）、lot2は50（差異なし）
        var line1 = stocktake.Lines.First(l => l.LotId == lot1.Id);
        var line2 = stocktake.Lines.First(l => l.LotId == lot2.Id);
        var counted = await admin.PutAsJsonAsync($"/api/stocktakes/{stocktake.Id}/counts",
            new StocktakeCountRequest([new(line1.Id, 95m), new(line2.Id, 50m)]));
        Assert.Equal(HttpStatusCode.OK, counted.StatusCode);
        var withCounts = await counted.Content.ReadFromJsonAsync<StocktakeResponse>();
        Assert.Equal(-5m, withCounts!.Lines.First(l => l.LotId == lot1.Id).Difference);

        // 確定 → 差異のある明細のみ棚卸調整
        var finalized = await admin.PostAsync($"/api/stocktakes/{stocktake.Id}/finalize", null);
        Assert.Equal(HttpStatusCode.OK, finalized.StatusCode);
        var result = await finalized.Content.ReadFromJsonAsync<StocktakeResponse>();
        Assert.Equal(StocktakeStatus.Finalized, result!.Status);
        Assert.True(result.Lines.First(l => l.LotId == lot1.Id).IsAdjusted);
        Assert.False(result.Lines.First(l => l.LotId == lot2.Id).IsAdjusted);
        Assert.Equal(95m, await Phase3TestData.GetStockQuantityAsync(admin, lot1.Id));
    }

    [Fact]
    public async Task 搬送指示の実行で在庫が移動する()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var lot = await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 100m, ctx.MaterialLocationId);

        var created = await admin.PostAsJsonAsync("/api/transfer-orders",
            new TransferOrderRequest(lot.Id, 40m, ctx.MaterialLocationId, ctx.ProductLocationId));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var order = await created.Content.ReadFromJsonAsync<TransferOrderResponse>();

        var executed = await admin.PostAsync($"/api/transfer-orders/{order!.Id}/execute", null);
        Assert.Equal(HttpStatusCode.OK, executed.StatusCode);
        var result = await executed.Content.ReadFromJsonAsync<TransferOrderResponse>();
        Assert.Equal(TransferOrderStatus.Completed, result!.Status);
        Assert.Equal(60m, await Phase3TestData.GetStockQuantityAsync(admin, lot.Id, ctx.MaterialLocationId));
        Assert.Equal(40m, await Phase3TestData.GetStockQuantityAsync(admin, lot.Id, ctx.ProductLocationId));
    }

    [Fact]
    public async Task 作業者ロールは受入や在庫更新ができない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var lot = await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 100m, ctx.MaterialLocationId);
        using var operator_ = await TestAuth.CreateUserClientAsync(
            factory, admin, "operator1", "Passw0rd123", MesRoles.Operator);

        var receive = await operator_.PostAsJsonAsync("/api/receiving",
            new ReceivingRequest(ctx.MaterialId, 10m, ctx.MaterialLocationId, null, null, null));
        Assert.Equal(HttpStatusCode.Forbidden, receive.StatusCode);

        var move = await operator_.PostAsJsonAsync("/api/inventory/move",
            new MoveRequest(lot.Id, ctx.MaterialLocationId, ctx.ProductLocationId, 1m));
        Assert.Equal(HttpStatusCode.Forbidden, move.StatusCode);

        // 搬送指示も実在庫を動かすので在庫権限が要る
        var transfer = await operator_.PostAsJsonAsync("/api/transfer-orders",
            new TransferOrderRequest(lot.Id, 1m, ctx.MaterialLocationId, ctx.ProductLocationId));
        Assert.Equal(HttpStatusCode.Forbidden, transfer.StatusCode);

        // 参照は可能
        var stocks = await operator_.GetAsync("/api/inventory/stocks");
        Assert.Equal(HttpStatusCode.OK, stocks.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await operator_.GetAsync("/api/transfer-orders")).StatusCode);
    }

    [Fact]
    public async Task 同時受入でもロット番号が重複せず全件成立する()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);

        // 採番はNumberSequenceの1行を更新してから読むため、同時に採番しても同じ番号にならない
        const int concurrency = 8;
        var responses = await Task.WhenAll(Enumerable.Range(0, concurrency).Select(_ =>
            admin.PostAsJsonAsync("/api/receiving",
                new ReceivingRequest(ctx.MaterialId, 1m, ctx.MaterialLocationId, null, null, null))));

        foreach (var response in responses)
        {
            Assert.True(response.IsSuccessStatusCode,
                $"採番が衝突しました：{(int)response.StatusCode}");
        }

        var lotNumbers = new List<string>();
        foreach (var response in responses)
        {
            lotNumbers.Add((await response.Content.ReadFromJsonAsync<LotResponse>())!.LotNumber);
        }
        Assert.Equal(concurrency, lotNumbers.Distinct().Count());
    }

    [Fact]
    public async Task 採番は既存番号の続きから始まる()
    {
        // 業務日付の境界（既定6時）で日付がずれると番号のプレフィックスが変わるため、境界を0時にして固定する
        using var factory = new ApiFactory(new Dictionary<string, string> { ["BusinessDay:BoundaryHour"] = "0" });
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);

        // 採番テーブルが無い状態（既存DBからの移行）でも、手入力の番号と衝突しない
        var manual = await Phase3TestData.ReceiveAsync(
            admin, ctx.MaterialId, 1m, ctx.MaterialLocationId,
            lotNumber: $"RM-01-{DateTime.Today:yyyyMMdd}-005");
        Assert.EndsWith("-005", manual.LotNumber, StringComparison.Ordinal);

        var next = await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 1m, ctx.MaterialLocationId);
        Assert.EndsWith("-006", next.LotNumber, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 在庫トランザクションはページングされ総件数が返る()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var lot = await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 100m, ctx.MaterialLocationId);

        // 受入1件＋移動（移動元・移動先で1件）を繰り返し、5件のトランザクションを作る
        for (var i = 0; i < 4; i++)
        {
            var from = i % 2 == 0 ? ctx.MaterialLocationId : ctx.ProductLocationId;
            var to = i % 2 == 0 ? ctx.ProductLocationId : ctx.MaterialLocationId;
            (await admin.PostAsJsonAsync("/api/inventory/move", new MoveRequest(lot.Id, from, to, 1m)))
                .EnsureSuccessStatusCode();
        }

        var first = await admin.GetFromJsonAsync<PagedResult<TransactionResponse>>(
            $"/api/inventory/transactions?lotId={lot.Id}&page=1&pageSize=2");
        Assert.Equal(5, first!.Total);
        Assert.Equal(2, first.Items.Count);
        Assert.True(first.HasNext);
        Assert.False(first.HasPrevious);
        Assert.Equal(3, first.PageCount);

        var last = await admin.GetFromJsonAsync<PagedResult<TransactionResponse>>(
            $"/api/inventory/transactions?lotId={lot.Id}&page=3&pageSize=2");
        Assert.Single(last!.Items);
        Assert.False(last.HasNext);
        Assert.True(last.HasPrevious);

        // ページ間で内容が重複しない（並べ替えが確定している）
        Assert.Empty(first.Items.Select(t => t.Id).Intersect(last.Items.Select(t => t.Id)));

        // pageSizeの指定は上限で頭打ちにする
        var capped = await admin.GetFromJsonAsync<PagedResult<TransactionResponse>>(
            $"/api/inventory/transactions?lotId={lot.Id}&pageSize=9999");
        Assert.Equal(PageQuery.MaxPageSize, capped!.PageSize);
    }

    [Fact]
    public async Task ロット選択肢は検索で絞り込め上限超過を知らせる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);

        var target = await Phase3TestData.ReceiveAsync(
            admin, ctx.MaterialId, 10m, ctx.MaterialLocationId, lotNumber: "FIND-ME-001");
        for (var i = 0; i < 3; i++)
        {
            await Phase3TestData.ReceiveAsync(
                admin, ctx.MaterialId, 10m, ctx.MaterialLocationId, lotNumber: $"OTHER-{i:000}");
        }

        // 絞り込みなしなら全件（上限内なので truncated は false）
        var all = await admin.GetFromJsonAsync<OptionsResult<StockResponse>>("/api/inventory/stocks/options");
        Assert.Equal(4, all!.Items.Count);
        Assert.False(all.Truncated);

        // ロット番号の部分一致（スキャンした値をそのまま渡せる）
        var found = await admin.GetFromJsonAsync<OptionsResult<StockResponse>>(
            "/api/inventory/stocks/options?q=FIND-ME");
        Assert.Equal(target.Id, Assert.Single(found!.Items).LotId);

        // 品目コードでも引ける
        var byProduct = await admin.GetFromJsonAsync<OptionsResult<StockResponse>>(
            "/api/inventory/stocks/options?q=RM-01");
        Assert.Equal(4, byProduct!.Items.Count);

        // 上限を超えたら黙って切らずに知らせる
        var limited = await admin.GetFromJsonAsync<OptionsResult<StockResponse>>(
            "/api/inventory/stocks/options?limit=2");
        Assert.Equal(2, limited!.Items.Count);
        Assert.True(limited.Truncated);
    }

    [Fact]
    public async Task 品質保証ロールは出荷_出庫_棚卸を参照できるが更新はできない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        using var qa = await TestAuth.CreateUserClientAsync(
            factory, admin, "qa1", "Passw0rd123", MesRoles.QualityAssurance);

        // 参照：出荷判定（H-10-10）は対象の出荷指示を選ぶところから始まるため、
        // 参照できないと品質保証ロール単独では判定を作成できない
        Assert.Equal(HttpStatusCode.OK, (await qa.GetAsync("/api/shipping-orders")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await qa.GetAsync("/api/shipping-orders/options")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await qa.GetAsync("/api/picking-orders")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await qa.GetAsync("/api/stocktakes")).StatusCode);

        // 更新：実在庫を動かす操作は在庫権限のまま
        var shipping = await qa.PostAsJsonAsync("/api/shipping-orders",
            new ShippingOrderCreateRequest("出荷先A", null, [new(ctx.ProductId, 1m)]));
        Assert.Equal(HttpStatusCode.Forbidden, shipping.StatusCode);

        var stocktake = await qa.PostAsJsonAsync("/api/stocktakes",
            new StocktakeCreateRequest(ctx.MaterialLocationId));
        Assert.Equal(HttpStatusCode.Forbidden, stocktake.StatusCode);

        var picking = await qa.PostAsJsonAsync("/api/picking-orders",
            new PickingOrderCreateRequest(PickingOrderType.ProcessIssue, null, null, []));
        Assert.Equal(HttpStatusCode.Forbidden, picking.StatusCode);
    }
    [Fact]
    public async Task サンプル採取で在庫から抜け保管期限と廃棄が記録される()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var lot = await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 100m, ctx.MaterialLocationId);

        var today = (await admin.GetFromJsonAsync<BusinessDateResponse>("/api/business-date"))!.Today;

        // 採取した分は在庫から抜ける（保管棚へ移り、出荷・投入には使えないため）
        var created = await admin.PostAsJsonAsync("/api/sample-storages",
            new SampleCollectRequest(lot.Id, ctx.ProductLocationId, 3m, today.AddDays(30), null, "受入検査分"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var sample = (await created.Content.ReadFromJsonAsync<SampleStorageResponse>())!;
        Assert.StartsWith("SP", sample.SampleNo);
        Assert.Equal(SampleStorageStatus.Stored, sample.Status);
        Assert.Equal(30, sample.DaysUntilRetentionEnd);
        Assert.False(sample.IsRetentionOver);
        Assert.Equal(97m, await Phase3TestData.GetStockQuantityAsync(admin, lot.Id));

        // 保管期限を過ぎたものだけの一覧には出ない
        var over = await admin.GetFromJsonAsync<List<SampleStorageResponse>>(
            "/api/sample-storages?retentionOverOnly=true");
        Assert.Empty(over!);

        // 在庫を超える採取はできない
        var tooMuch = await admin.PostAsJsonAsync("/api/sample-storages",
            new SampleCollectRequest(lot.Id, ctx.ProductLocationId, 1000m, null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);

        // 廃棄しても在庫は動かない（採取時に既に抜いてある）
        var closed = await admin.PostAsJsonAsync($"/api/sample-storages/{sample.Id}/close",
            new SampleCloseRequest(SampleStorageStatus.Disposed, "期限前だが試験で使い切り"));
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        var disposed = (await closed.Content.ReadFromJsonAsync<SampleStorageResponse>())!;
        Assert.Equal(SampleStorageStatus.Disposed, disposed.Status);
        Assert.Equal(today, disposed.ClosedOn);
        Assert.Equal(97m, await Phase3TestData.GetStockQuantityAsync(admin, lot.Id));

        // 保管を終えたものは二度閉じられない
        var again = await admin.PostAsJsonAsync($"/api/sample-storages/{sample.Id}/close",
            new SampleCloseRequest(SampleStorageStatus.Consumed, null));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task 倉庫業務進捗は指示と完了を業務種別ごとに数える()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        var lot = await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 100m, ctx.MaterialLocationId);

        // 搬送指示を2件作り、片方だけ実行する
        var first = await admin.PostAsJsonAsync("/api/transfer-orders",
            new TransferOrderRequest(lot.Id, 10m, ctx.MaterialLocationId, ctx.ProductLocationId));
        var firstOrder = (await first.Content.ReadFromJsonAsync<TransferOrderResponse>())!;
        (await admin.PostAsync($"/api/transfer-orders/{firstOrder.Id}/execute", null))
            .EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/transfer-orders",
            new TransferOrderRequest(lot.Id, 10m, ctx.MaterialLocationId, ctx.ProductLocationId)))
            .EnsureSuccessStatusCode();

        var progress = await admin.GetFromJsonAsync<WarehouseProgressResponse>(
            "/api/inventory/warehouse-progress");
        var transfer = progress!.Rows.Single(r => r.Kind == "在庫移動");
        Assert.Equal(2, transfer.TotalCount);
        Assert.Equal(1, transfer.CompletedCount);
        Assert.Equal(1, transfer.OpenCount);
        Assert.Equal(0, transfer.OldestOpenAgeDays);

        // 指示の無い業務は0件で並ぶ（行そのものは消さない）
        Assert.Equal(0, progress.Rows.Single(r => r.Kind == "棚卸").TotalCount);

        // 受入は「指示」を持たないため進捗の対象にしない
        Assert.DoesNotContain(progress.Rows, r => r.Kind == "受入");
    }

}
