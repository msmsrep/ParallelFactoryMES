using System.Net;
using System.Net.Http.Json;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Contracts.Production;
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
}
