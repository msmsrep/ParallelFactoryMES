using System.Net;
using System.Net.Http.Json;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Maintenance;
using MesApp.Core.Contracts.Masters;
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
