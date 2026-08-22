using System.Net;
using System.Net.Http.Json;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Contracts.Users;
using MesApp.Core.Entities;

namespace MesApp.Api.Tests;

public class MasterTests
{
    [Fact]
    public async Task 品目マスタのCRUDと論理削除ができる()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        // 登録
        var created = await client.PostAsJsonAsync("/api/products",
            new ProductRequest("P-001", "テスト製品", "個", "規格A", ProductType.Product, 1.5m));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var product = await created.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.NotNull(product);

        // コード重複は409
        var duplicated = await client.PostAsJsonAsync("/api/products",
            new ProductRequest("P-001", "別製品", "個", null, ProductType.Product, 0m));
        Assert.Equal(HttpStatusCode.Conflict, duplicated.StatusCode);

        // 更新
        var updated = await client.PutAsJsonAsync($"/api/products/{product.Id}",
            new ProductRequest("P-001", "テスト製品（改）", "個", "規格B", ProductType.Product, 2.0m));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        // 論理削除 → 既定一覧から消える
        var deleted = await client.DeleteAsync($"/api/products/{product.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var list = await client.GetFromJsonAsync<List<ProductResponse>>("/api/products");
        Assert.DoesNotContain(list!, p => p.Id == product.Id);
        var listAll = await client.GetFromJsonAsync<List<ProductResponse>>("/api/products?includeInactive=true");
        Assert.Contains(listAll!, p => p.Id == product.Id && !p.IsActive);
    }

    [Fact]
    public async Task 作業者ロールはマスタ参照はできるが更新はできない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        using var operator_ = await TestAuth.CreateUserClientAsync(
            factory, admin, "operator1", "Passw0rd123", MesRoles.Operator);

        var read = await operator_.GetAsync("/api/products");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        var write = await operator_.PostAsJsonAsync("/api/products",
            new ProductRequest("P-100", "作業者登録", "個", null, ProductType.Product, 0m));
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task MBOMと工順を一括登録できる()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        var parent = await CreateProductAsync(client, "FG-01", "完成品", ProductType.Product);
        var child = await CreateProductAsync(client, "RM-01", "部材", ProductType.Material);
        var process = await CreateProcessAsync(client, "PR-01", "組立");

        // MBOM登録
        var bomResult = await client.PutAsJsonAsync($"/api/products/{parent.Id}/bom",
            new List<BomItemRequest> { new(child.Id, 2.0m, MakeOrBuy.InHouse, null) });
        Assert.Equal(HttpStatusCode.OK, bomResult.StatusCode);
        var bom = await bomResult.Content.ReadFromJsonAsync<List<BomItemResponse>>();
        Assert.Single(bom!);
        Assert.Equal("RM-01", bom![0].ChildProductCode);

        // 自己参照は400
        var selfRef = await client.PutAsJsonAsync($"/api/products/{parent.Id}/bom",
            new List<BomItemRequest> { new(parent.Id, 1.0m, MakeOrBuy.InHouse, null) });
        Assert.Equal(HttpStatusCode.BadRequest, selfRef.StatusCode);

        // 工順登録
        var routingResult = await client.PutAsJsonAsync($"/api/products/{parent.Id}/routing",
            new List<RoutingStepRequest> { new(1, process.Id, 30m, 10m, null, null, null, "温度", null) });
        Assert.Equal(HttpStatusCode.OK, routingResult.StatusCode);
        var routing = await routingResult.Content.ReadFromJsonAsync<List<RoutingStepResponse>>();
        Assert.Single(routing!);
        Assert.Equal("PR-01", routing![0].ProcessCode);
    }

    [Fact]
    public async Task ユーザーにスキルを割り当てて有効期限切れを検出できる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);

        // スキルマスタ（有効期限管理あり）
        var skillCreated = await admin.PostAsJsonAsync("/api/skills",
            new SkillRequest("SK-01", "溶接資格", SkillType.Certification, true));
        Assert.Equal(HttpStatusCode.Created, skillCreated.StatusCode);
        var skill = await skillCreated.Content.ReadFromJsonAsync<SkillResponse>();

        // ユーザー作成とスキル割当（期限切れ）
        var userCreated = await admin.PostAsJsonAsync("/api/users",
            new CreateUserRequest("worker1", "Passw0rd123", "作業者1", [MesRoles.Operator]));
        Assert.Equal(HttpStatusCode.Created, userCreated.StatusCode);
        var user = await userCreated.Content.ReadFromJsonAsync<UserSummaryResponse>();

        var yesterday = DateOnly.FromDateTime(DateTime.Today).AddDays(-2);
        var assigned = await admin.PutAsJsonAsync($"/api/users/{user!.Id}/skills",
            new List<UserSkillRequest> { new(skill!.Id, yesterday.AddYears(-1), yesterday) });
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        var skills = await assigned.Content.ReadFromJsonAsync<List<UserSkillResponse>>();
        Assert.Single(skills!);
        Assert.True(skills![0].IsExpired);
    }

    [Fact]
    public async Task 不良理由マスタを登録更新無効化でき重複コードは拒否される()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        var created = await client.PostAsJsonAsync("/api/defect-reasons",
            new DefectReasonRequest("DF-01", "寸法外れ", DefectReasonCategory.Process));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var reason = (await created.Content.ReadFromJsonAsync<DefectReasonResponse>())!;
        Assert.Equal(DefectReasonCategory.Process, reason.Category);
        Assert.True(reason.IsActive);

        // 同じコードは登録できない
        var duplicate = await client.PostAsJsonAsync("/api/defect-reasons",
            new DefectReasonRequest("DF-01", "別名称", DefectReasonCategory.Material));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var updated = await client.PutAsJsonAsync($"/api/defect-reasons/{reason.Id}",
            new DefectReasonRequest("DF-01", "寸法外れ（外径）", DefectReasonCategory.Equipment));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var updatedBody = (await updated.Content.ReadFromJsonAsync<DefectReasonResponse>())!;
        Assert.Equal("寸法外れ（外径）", updatedBody.Name);
        Assert.Equal(DefectReasonCategory.Equipment, updatedBody.Category);

        // 無効化すると既定の一覧から外れる
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/defect-reasons/{reason.Id}")).StatusCode);
        var active = await client.GetFromJsonAsync<List<DefectReasonResponse>>("/api/defect-reasons");
        Assert.Empty(active!);
        var all = await client.GetFromJsonAsync<List<DefectReasonResponse>>(
            "/api/defect-reasons?includeInactive=true");
        Assert.Single(all!);
    }

    [Fact]
    public async Task 不良理由マスタの書き込みはマスタ管理ロールのみ()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        using var operator_ = await TestAuth.CreateUserClientAsync(
            factory, admin, "operator1", "Passw0rd123", MesRoles.Operator);

        var rejected = await operator_.PostAsJsonAsync("/api/defect-reasons",
            new DefectReasonRequest("DF-99", "作業者が登録", DefectReasonCategory.Other));
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);

        // 参照はできる
        var list = await operator_.GetAsync("/api/defect-reasons");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [Fact]
    public async Task 設備と治工具とロケーションと検査項目とチェックリストを登録できる()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        var equipment = await client.PostAsJsonAsync("/api/equipments",
            new EquipmentRequest("EQ-01", "プレス機", "第1工場", EquipmentStatus.Available,
                MaintenanceType.Count, 10000m, "金型"));
        Assert.Equal(HttpStatusCode.Created, equipment.StatusCode);

        var tool = await client.PostAsJsonAsync("/api/tools",
            new ToolRequest("T-01", "金型A", "型", 5000, null, ToolStatus.Available));
        Assert.Equal(HttpStatusCode.Created, tool.StatusCode);

        var location = await client.PostAsJsonAsync("/api/locations",
            new LocationRequest("LOC-01", LocationAreaType.MaterialWarehouse, "A-1-1"));
        Assert.Equal(HttpStatusCode.Created, location.StatusCode);

        var inspection = await client.PostAsJsonAsync("/api/inspection-items",
            new InspectionItemRequest("INS-01", "外径測定", null, null, InspectionType.InProcess,
                9.5m, 10.5m, 10m, "ノギス", 5));
        Assert.Equal(HttpStatusCode.Created, inspection.StatusCode);

        var checklist = await client.PostAsJsonAsync("/api/checklists",
            new ChecklistRequest("CL-01", "始業前点検", ChecklistCategory.Setup,
                [new(1, "安全カバー確認", true), new(2, "油量確認", false)]));
        Assert.Equal(HttpStatusCode.Created, checklist.StatusCode);
        var checklistBody = await checklist.Content.ReadFromJsonAsync<ChecklistResponse>();
        Assert.Equal(2, checklistBody!.Items.Count);

        // 検査項目の更新で版数が上がる（C-10-10-03）
        var inspectionBody = await inspection.Content.ReadFromJsonAsync<InspectionItemResponse>();
        var updated = await client.PutAsJsonAsync($"/api/inspection-items/{inspectionBody!.Id}",
            new InspectionItemRequest("INS-01", "外径測定", null, null, InspectionType.InProcess,
                9.0m, 11.0m, 10m, "マイクロメータ", 10));
        var updatedBody = await updated.Content.ReadFromJsonAsync<InspectionItemResponse>();
        Assert.Equal(2, updatedBody!.Version);
    }

    internal static async Task<ProductResponse> CreateProductAsync(
        HttpClient client, string code, string name, ProductType type)
    {
        var response = await client.PostAsJsonAsync("/api/products",
            new ProductRequest(code, name, "個", null, type, 0m));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    internal static async Task<ProcessResponse> CreateProcessAsync(HttpClient client, string code, string name)
    {
        var response = await client.PostAsJsonAsync("/api/processes",
            new ProcessRequest(code, name, MakeOrBuy.InHouse));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProcessResponse>())!;
    }
}
