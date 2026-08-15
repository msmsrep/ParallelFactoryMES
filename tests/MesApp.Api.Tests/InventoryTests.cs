using System.Net;
using System.Net.Http.Json;
using MesApp.Core.Constants;
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

        // 在庫トランザクションに受入が記録される
        var transactions = await admin.GetFromJsonAsync<List<TransactionResponse>>(
            $"/api/inventory/transactions?lotId={lot.Id}");
        Assert.Single(transactions!);
        Assert.Equal(InventoryTransactionType.Receipt, transactions![0].Type);
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

        // 参照は可能
        var stocks = await operator_.GetAsync("/api/inventory/stocks");
        Assert.Equal(HttpStatusCode.OK, stocks.StatusCode);
    }
}
