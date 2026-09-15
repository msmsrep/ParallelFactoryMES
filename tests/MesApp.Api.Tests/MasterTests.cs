using System.Net;
using System.Net.Http.Json;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Contracts.Common;
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

    [Fact]
    public async Task 作業区の階層を登録でき段の飛び越しと循環は拒否される()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        // 工場 → ライン → エリア → 作業区 の順に積める
        var plant = await CreateWorkCenterAsync(client, "P1", "第一工場", WorkCenterLevel.Plant, null);
        var line = await CreateWorkCenterAsync(client, "L1", "組立1ライン", WorkCenterLevel.Line, plant.Id);
        var area = await CreateWorkCenterAsync(client, "A1", "前工程エリア", WorkCenterLevel.Area, line.Id);
        var wc = await CreateWorkCenterAsync(client, "WC01", "溶接作業区", WorkCenterLevel.WorkCenter, area.Id);
        Assert.Equal("A1", wc.ParentCode);

        // コード重複は409
        var duplicated = await client.PostAsJsonAsync("/api/work-centers",
            new WorkCenterRequest("P1", "別工場", WorkCenterLevel.Plant, null));
        Assert.Equal(HttpStatusCode.Conflict, duplicated.StatusCode);

        // 段の飛び越し（作業区の上位に工場）は400
        var skipped = await client.PostAsJsonAsync("/api/work-centers",
            new WorkCenterRequest("WC02", "検査作業区", WorkCenterLevel.WorkCenter, plant.Id));
        Assert.Equal(HttpStatusCode.BadRequest, skipped.StatusCode);

        // 上位なしの下位段は400
        var orphan = await client.PostAsJsonAsync("/api/work-centers",
            new WorkCenterRequest("L2", "組立2ライン", WorkCenterLevel.Line, null));
        Assert.Equal(HttpStatusCode.BadRequest, orphan.StatusCode);

        // 工場に上位を付けると400
        var rooted = await client.PostAsJsonAsync("/api/work-centers",
            new WorkCenterRequest("P2", "第二工場", WorkCenterLevel.Plant, plant.Id));
        Assert.Equal(HttpStatusCode.BadRequest, rooted.StatusCode);

        // 自分の配下を上位にすると400（循環）
        var cyclic = await client.PutAsJsonAsync($"/api/work-centers/{plant.Id}",
            new WorkCenterRequest("P1", "第一工場", WorkCenterLevel.Plant, area.Id));
        Assert.Equal(HttpStatusCode.BadRequest, cyclic.StatusCode);

        // 有効な下位が残っている間は無効化できない
        var blocked = await client.DeleteAsync($"/api/work-centers/{area.Id}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        // 下位から順に無効化すれば通る
        var leaf = await client.DeleteAsync($"/api/work-centers/{wc.Id}");
        Assert.Equal(HttpStatusCode.NoContent, leaf.StatusCode);
        var parent = await client.DeleteAsync($"/api/work-centers/{area.Id}");
        Assert.Equal(HttpStatusCode.NoContent, parent.StatusCode);

        // 既定は有効なものだけ返す
        var active = await client.GetFromJsonAsync<List<WorkCenterResponse>>("/api/work-centers");
        Assert.DoesNotContain(active!, x => x.Code == "WC01");
        var all = await client.GetFromJsonAsync<List<WorkCenterResponse>>("/api/work-centers?includeInactive=true");
        Assert.Contains(all!, x => x.Code == "WC01");
    }

    [Fact]
    public async Task 作業区の登録はマスタ更新権限が要る()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        using var operator_ = await TestAuth.CreateUserClientAsync(
            factory, admin, "wc-op", "Passw0rd!x", MesRoles.Operator);

        var denied = await operator_.PostAsJsonAsync("/api/work-centers",
            new WorkCenterRequest("P9", "第九工場", WorkCenterLevel.Plant, null));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        // 参照は開いている
        var list = await operator_.GetAsync("/api/work-centers");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [Fact]
    public async Task 設備は作業区にだけ紐付きロケーションはどの段でも紐付く()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        var plant = await CreateWorkCenterAsync(client, "P1", "第一工場", WorkCenterLevel.Plant, null);
        var line = await CreateWorkCenterAsync(client, "L1", "組立1ライン", WorkCenterLevel.Line, plant.Id);
        var area = await CreateWorkCenterAsync(client, "A1", "前工程エリア", WorkCenterLevel.Area, line.Id);
        var wc = await CreateWorkCenterAsync(client, "WC01", "溶接作業区", WorkCenterLevel.WorkCenter, area.Id);

        // 設備は作業区に紐付く
        var equipment = await client.PostAsJsonAsync("/api/equipments",
            new EquipmentRequest("EQ-01", "プレス機", null, EquipmentStatus.Available,
                MaintenanceType.None, null, null, wc.Id));
        Assert.Equal(HttpStatusCode.Created, equipment.StatusCode);
        var equipmentBody = await equipment.Content.ReadFromJsonAsync<EquipmentResponse>();
        Assert.Equal("WC01", equipmentBody!.WorkCenterCode);

        // 作業区以外の段は400（集計軸を一意にするため）
        var wrongLevel = await client.PostAsJsonAsync("/api/equipments",
            new EquipmentRequest("EQ-02", "旋盤", null, EquipmentStatus.Available,
                MaintenanceType.None, null, null, line.Id));
        Assert.Equal(HttpStatusCode.BadRequest, wrongLevel.StatusCode);

        // 未設定でも登録できる（作業区の整備前でも設備台帳を作れる）
        var noWorkCenter = await client.PostAsJsonAsync("/api/equipments",
            new EquipmentRequest("EQ-03", "研磨機", "第1工場", EquipmentStatus.Available,
                MaintenanceType.None, null, null));
        Assert.Equal(HttpStatusCode.Created, noWorkCenter.StatusCode);

        // ロケーションは工場にも紐付く（倉庫は工場直下にあることが多い）
        var warehouse = await client.PostAsJsonAsync("/api/locations",
            new LocationRequest("WH-01", LocationAreaType.MaterialWarehouse, "A-1", plant.Id));
        Assert.Equal(HttpStatusCode.Created, warehouse.StatusCode);
        var warehouseBody = await warehouse.Content.ReadFromJsonAsync<LocationResponse>();
        Assert.Equal("P1", warehouseBody!.WorkCenterCode);

        // 無効な作業区は指定できない
        await client.DeleteAsync($"/api/work-centers/{wc.Id}");
        var toInactive = await client.PostAsJsonAsync("/api/locations",
            new LocationRequest("IP-01", LocationAreaType.InProcess, null, wc.Id));
        Assert.Equal(HttpStatusCode.BadRequest, toInactive.StatusCode);
    }

    [Fact]
    public async Task 工程管理項目のCRUDと版数管理ができ許容範囲の矛盾は拒否される()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);
        var process = await CreateProcessAsync(client, "PR-01", "加熱");

        var created = await client.PostAsJsonAsync("/api/control-items",
            new ControlItemRequest("CI-01", "加熱温度", "℃", null, process.Id, 180m, 175m, 185m));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var item = await created.Content.ReadFromJsonAsync<ControlItemResponse>();
        Assert.Equal(1, item!.Version);
        Assert.Equal("PR-01", item.TargetProcessCode);

        // コード重複は409
        var duplicated = await client.PostAsJsonAsync("/api/control-items",
            new ControlItemRequest("CI-01", "別項目", null, null, null, null, null, null));
        Assert.Equal(HttpStatusCode.Conflict, duplicated.StatusCode);

        // 下限>上限は400
        var reversed = await client.PostAsJsonAsync("/api/control-items",
            new ControlItemRequest("CI-02", "逆転", null, null, null, null, 200m, 100m));
        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);

        // 指示値が許容範囲の外は400（指示どおり作っても逸脱になってしまうため）
        var outOfRange = await client.PostAsJsonAsync("/api/control-items",
            new ControlItemRequest("CI-03", "範囲外", null, null, null, 300m, 175m, 185m));
        Assert.Equal(HttpStatusCode.BadRequest, outOfRange.StatusCode);

        // 存在しない対象工程は400
        var missingProcess = await client.PostAsJsonAsync("/api/control-items",
            new ControlItemRequest("CI-04", "工程なし", null, null, 9999, null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, missingProcess.StatusCode);

        // 更新で版数が上がる
        var updated = await client.PutAsJsonAsync($"/api/control-items/{item.Id}",
            new ControlItemRequest("CI-01", "加熱温度", "℃", null, process.Id, 182m, 178m, 186m));
        var updatedBody = await updated.Content.ReadFromJsonAsync<ControlItemResponse>();
        Assert.Equal(2, updatedBody!.Version);
        Assert.Equal(182m, updatedBody.TargetValue);

        // 無効化すると既定の一覧から消える
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/control-items/{item.Id}")).StatusCode);
        var active = await client.GetFromJsonAsync<List<ControlItemResponse>>("/api/control-items");
        Assert.Empty(active!);
    }

    internal static async Task<WorkCenterResponse> CreateWorkCenterAsync(
        HttpClient client, string code, string name, WorkCenterLevel level, int? parentId)
    {
        var response = await client.PostAsJsonAsync("/api/work-centers",
            new WorkCenterRequest(code, name, level, parentId));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkCenterResponse>())!;
    }

    [Fact]
    public async Task 作業手順書を登録すると版数が上がり工順から紐付けできる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var product = await CreateProductAsync(admin, "FG-01", "完成品", ProductType.Product);
        var process = await CreateProcessAsync(admin, "PR-01", "組立");

        var created = await admin.PostAsJsonAsync("/api/work-procedures",
            new WorkProcedureRequest("SOP-01", "組立作業手順", "1. 部材を並べる／2. 締結する", null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var procedure = (await created.Content.ReadFromJsonAsync<WorkProcedureResponse>())!;
        Assert.Equal(1, procedure.Version);

        // 手順も所在も無い手順書は作業者が何も参照できないので400
        var empty = await admin.PostAsJsonAsync("/api/work-procedures",
            new WorkProcedureRequest("SOP-99", "空の手順書", "", null));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        // 本文を置けない手順書は所在だけでよい
        var external = await admin.PostAsJsonAsync("/api/work-procedures",
            new WorkProcedureRequest("SOP-02", "3Dデータの手順", "", "DOC-1234"));
        Assert.Equal(HttpStatusCode.Created, external.StatusCode);

        // 改訂で版数が上がる
        var updated = await admin.PutAsJsonAsync($"/api/work-procedures/{procedure.Id}",
            new WorkProcedureRequest("SOP-01", "組立作業手順", "1. 部材を並べる／2. 規定トルクで締結する", null));
        updated.EnsureSuccessStatusCode();
        Assert.Equal(2, (await updated.Content.ReadFromJsonAsync<WorkProcedureResponse>())!.Version);

        // 工順（BOP）へ紐付ける（I-30-20-12）
        var routing = await admin.PutAsJsonAsync($"/api/products/{product.Id}/routing",
            new List<RoutingStepRequest>
            {
                new(1, process.Id, 30m, 10m, null, null, null, null, null, null, null, procedure.Id),
            });
        routing.EnsureSuccessStatusCode();
        var steps = await admin.GetFromJsonAsync<List<RoutingStepResponse>>($"/api/products/{product.Id}/routing");
        Assert.Equal("SOP-01", Assert.Single(steps!).WorkProcedureNo);

        // 工順から参照されている手順書は無効化できない
        var blocked = await admin.DeleteAsync($"/api/work-procedures/{procedure.Id}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        // 存在しない手順書を紐付けたら400
        var missing = await admin.PutAsJsonAsync($"/api/products/{product.Id}/routing",
            new List<RoutingStepRequest>
            {
                new(1, process.Id, 30m, 10m, null, null, null, null, null, null, null, 9999),
            });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
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

    [Fact]
    public async Task 作業者の選択肢は全ロールが取得できユーザー管理の一覧は管理者専用()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        using var manager = await TestAuth.CreateUserClientAsync(
            factory, admin, "manager1", "Passw0rd123", MesRoles.ProductionManager);

        var created = await admin.PostAsJsonAsync("/api/users",
            new CreateUserRequest("worker9", "Passw0rd123", "作業者9", [MesRoles.Operator]));
        created.EnsureSuccessStatusCode();

        // 差立で作業者を選べるよう、選択肢は生産管理担当者でも取得できる
        var options = await manager.GetFromJsonAsync<OptionsResult<UserOptionResponse>>("/api/users/options");
        Assert.Contains(options!.Items, u => u.UserName == "worker9");
        Assert.False(options.Truncated);

        // 検索と、ユーザー管理の一覧が管理者専用であることは変わらない
        var filtered = await manager.GetFromJsonAsync<OptionsResult<UserOptionResponse>>(
            "/api/users/options?q=作業者9");
        Assert.Single(filtered!.Items);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task 最後のシステム管理者は無効化も降格もできない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);

        var me = (await admin.GetFromJsonAsync<List<UserSummaryResponse>>("/api/users"))!
            .Single(u => u.UserName == TestAuth.AdminUser);

        // 自分を無効化する
        var deactivate = await admin.PutAsJsonAsync($"/api/users/{me.Id}",
            new UpdateUserRequest(me.DisplayName, [MesRoles.SystemAdmin], false));
        Assert.Equal(HttpStatusCode.Conflict, deactivate.StatusCode);

        // 自分からシステム管理者ロールを外す
        var demote = await admin.PutAsJsonAsync($"/api/users/{me.Id}",
            new UpdateUserRequest(me.DisplayName, [MesRoles.ProductionManager], true));
        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);

        // 変更されていない（管理者のまま操作できる）
        var after = (await admin.GetFromJsonAsync<List<UserSummaryResponse>>("/api/users"))!
            .Single(u => u.UserName == TestAuth.AdminUser);
        Assert.True(after.IsActive);
        Assert.Contains(MesRoles.SystemAdmin, after.Roles);
    }

    [Fact]
    public async Task 別の管理者がいれば降格できる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);

        var created = await admin.PostAsJsonAsync("/api/users",
            new CreateUserRequest("admin2", "Passw0rd123", "管理者2", [MesRoles.SystemAdmin]));
        created.EnsureSuccessStatusCode();

        var me = (await admin.GetFromJsonAsync<List<UserSummaryResponse>>("/api/users"))!
            .Single(u => u.UserName == TestAuth.AdminUser);
        var demote = await admin.PutAsJsonAsync($"/api/users/{me.Id}",
            new UpdateUserRequest(me.DisplayName, [MesRoles.ProductionManager], true));

        Assert.Equal(HttpStatusCode.OK, demote.StatusCode);
    }

    [Fact]
    public async Task 品目の選択肢APIは検索でき上限超過を知らせる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);

        for (var i = 0; i < 3; i++)
        {
            await admin.PostAsJsonAsync("/api/products",
                new ProductRequest($"FIND-{i:000}", $"探す品目{i}", "個", null, ProductType.Product, 0m));
        }
        await admin.PostAsJsonAsync("/api/products",
            new ProductRequest("OTHER-001", "別の品目", "個", null, ProductType.Product, 0m));

        var all = await admin.GetFromJsonAsync<OptionsResult<ProductResponse>>("/api/products/options");
        Assert.Equal(4, all!.Items.Count);
        Assert.False(all.Truncated);

        // コード・名称の部分一致（スキャンした値をそのまま渡せる）
        var byCode = await admin.GetFromJsonAsync<OptionsResult<ProductResponse>>(
            "/api/products/options?q=FIND-");
        Assert.Equal(3, byCode!.Items.Count);
        var byName = await admin.GetFromJsonAsync<OptionsResult<ProductResponse>>(
            "/api/products/options?q=別の");
        Assert.Equal("OTHER-001", Assert.Single(byName!.Items).Code);

        // 上限を超えたら黙って切らずに知らせる
        var limited = await admin.GetFromJsonAsync<OptionsResult<ProductResponse>>(
            "/api/products/options?limit=2");
        Assert.Equal(2, limited!.Items.Count);
        Assert.True(limited.Truncated);

        // 無効化した品目は選択肢に出さない
        var target = byName.Items[0];
        await admin.DeleteAsync($"/api/products/{target.Id}");
        var afterDelete = await admin.GetFromJsonAsync<OptionsResult<ProductResponse>>(
            "/api/products/options?q=別の");
        Assert.Empty(afterDelete!.Items);
    }

    [Fact]
    public async Task 検査項目は対象品目と対象工程のコードまで返す()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);

        var product = await (await admin.PostAsJsonAsync("/api/products",
            new ProductRequest("INS-P-01", "検査対象品", "個", null, ProductType.Product, 0m)))
            .Content.ReadFromJsonAsync<ProductResponse>();
        var process = await (await admin.PostAsJsonAsync("/api/processes",
            new ProcessRequest("INS-PR-01", "検査対象工程", MakeOrBuy.InHouse)))
            .Content.ReadFromJsonAsync<ProcessResponse>();

        // 画面が対象のコードを出すためにマスタを全件持たずに済むよう、応答にコードを載せる（Spec.md 7.5）
        var created = await (await admin.PostAsJsonAsync("/api/inspection-items",
            new InspectionItemRequest("INS-10", "品目基準", product!.Id, null,
                InspectionType.FinalProduct, 1m, 2m, null, null, null)))
            .Content.ReadFromJsonAsync<InspectionItemResponse>();
        Assert.Equal("INS-P-01", created!.TargetProductCode);
        Assert.Null(created.TargetProcessCode);

        var updated = await (await admin.PutAsJsonAsync($"/api/inspection-items/{created.Id}",
            new InspectionItemRequest("INS-10", "工程基準", null, process!.Id,
                InspectionType.InProcess, 1m, 2m, null, null, null)))
            .Content.ReadFromJsonAsync<InspectionItemResponse>();
        Assert.Null(updated!.TargetProductCode);
        Assert.Equal("INS-PR-01", updated.TargetProcessCode);

        var list = await admin.GetFromJsonAsync<List<InspectionItemResponse>>("/api/inspection-items");
        Assert.Equal("INS-PR-01", Assert.Single(list!).TargetProcessCode);
    }
}
