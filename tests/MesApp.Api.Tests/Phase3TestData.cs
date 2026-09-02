using System.Net.Http.Json;
using MesApp.Core.Contracts.Common;
using MesApp.Core.Contracts.Inventory;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Contracts.Production;
using MesApp.Core.Entities;

namespace MesApp.Api.Tests;

internal record Phase3Context(
    int ProductId,      // 完成品 FG-01（BOM: RM-01 x 2）
    int MaterialId,     // 部材 RM-01
    int ProcessId,
    int MaterialLocationId,  // LOC-M（部材倉庫）
    int ProductLocationId);  // LOC-P（製品倉庫）

/// <summary>Phase 3テスト用の共通マスタセットアップ（品目・BOM・工順・ロケーション）と操作ヘルパー</summary>
internal static class Phase3TestData
{
    public const decimal BomQuantityPer = 2m;

    public static async Task<Phase3Context> SetupAsync(HttpClient admin)
    {
        var product = await MasterTests.CreateProductAsync(admin, "FG-01", "完成品", ProductType.Product);
        var material = await MasterTests.CreateProductAsync(admin, "RM-01", "部材", ProductType.Material);
        var process = await MasterTests.CreateProcessAsync(admin, "PR-01", "組立");

        var bom = await admin.PutAsJsonAsync($"/api/products/{product.Id}/bom",
            new List<BomItemRequest> { new(material.Id, BomQuantityPer, MakeOrBuy.InHouse, null) });
        bom.EnsureSuccessStatusCode();

        var routing = await admin.PutAsJsonAsync($"/api/products/{product.Id}/routing",
            new List<RoutingStepRequest>
            {
                new(1, process.Id, 30m, 10m, null, null, null, null, null),
                new(2, process.Id, 15m, 5m, null, null, null, null, null),
            });
        routing.EnsureSuccessStatusCode();

        var locM = await admin.PostAsJsonAsync("/api/locations",
            new LocationRequest("LOC-M", LocationAreaType.MaterialWarehouse, null));
        locM.EnsureSuccessStatusCode();
        var locP = await admin.PostAsJsonAsync("/api/locations",
            new LocationRequest("LOC-P", LocationAreaType.ProductWarehouse, null));
        locP.EnsureSuccessStatusCode();
        var materialLocation = (await locM.Content.ReadFromJsonAsync<LocationResponse>())!;
        var productLocation = (await locP.Content.ReadFromJsonAsync<LocationResponse>())!;

        return new Phase3Context(product.Id, material.Id, process.Id, materialLocation.Id, productLocation.Id);
    }

    /// <summary>受入登録（在庫計上済みのロットを返す）</summary>
    public static async Task<LotResponse> ReceiveAsync(
        HttpClient client, int productId, decimal quantity, int locationId,
        DateOnly? expiresOn = null, string? lotNumber = null)
    {
        var response = await client.PostAsJsonAsync("/api/receiving",
            new ReceivingRequest(productId, quantity, locationId, lotNumber, expiresOn, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LotResponse>())!;
    }

    /// <summary>指図の作成→承認→展開（作業指示一覧付きの詳細を返す）</summary>
    public static async Task<ManufacturingOrderDetailResponse> CreateReleasedOrderAsync(
        HttpClient client, int productId, decimal quantity)
    {
        var created = await client.PostAsJsonAsync("/api/manufacturing-orders",
            new CreateManufacturingOrderRequest(productId, quantity, null,
                ManufacturingOrderType.Normal, null, null));
        created.EnsureSuccessStatusCode();
        var order = (await created.Content.ReadFromJsonAsync<ManufacturingOrderResponse>())!;
        (await client.PostAsync($"/api/manufacturing-orders/{order.Id}/approve", null)).EnsureSuccessStatusCode();
        var expanded = await client.PostAsJsonAsync(
            $"/api/manufacturing-orders/{order.Id}/expand", new ExpandRequest(null));
        expanded.EnsureSuccessStatusCode();
        return (await expanded.Content.ReadFromJsonAsync<ManufacturingOrderDetailResponse>())!;
    }

    /// <summary>ロットの現在庫合計を取得</summary>
    public static async Task<decimal> GetStockQuantityAsync(HttpClient client, int lotId, int? locationId = null)
    {
        var url = $"/api/inventory/stocks?lotId={lotId}&includeEmpty=true";
        if (locationId is not null)
        {
            url += $"&locationId={locationId}";
        }
        // 1ロット分の在庫行はページに収まる前提（テストデータの規模）
        var stocks = await client.GetFromJsonAsync<PagedResult<StockResponse>>(url);
        return stocks!.Items.Sum(s => s.Quantity);
    }
}
