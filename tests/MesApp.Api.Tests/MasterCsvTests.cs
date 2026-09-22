using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Dashboard;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Contracts.Planning;
using MesApp.Core.Contracts.Users;
using MesApp.Core.Entities;

namespace MesApp.Api.Tests;

public class MasterCsvTests
{
    [Fact]
    public async Task 品目をCSVで一括登録して出力できる()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        var csv = """
            Code,Name,Unit,Specification,Type,StandardDefectRate,IsActive
            P-001,テスト製品,個,規格A,Product,1.5,true
            P-002,部材A,kg,,Material,0,true
            P-003,"カンマ, と ""引用符"" を含む名前",個,,SemiFinished,2,true
            """;
        var result = await ImportAsync(client, "products", csv);
        Assert.True(result.Succeeded, string.Join(" / ", result.Errors.Select(e => e.Message)));
        Assert.Equal(3, result.DataRows);
        Assert.Equal(3, result.Created);
        Assert.Equal(0, result.Updated);

        var products = await client.GetFromJsonAsync<List<ProductResponse>>("/api/products");
        Assert.Equal(3, products!.Count);
        Assert.Equal("カンマ, と \"引用符\" を含む名前", products.Single(p => p.Code == "P-003").Name);

        // 出力はUTF-8 BOM付き（Excelでの文字化け回避）で、取り込んだ内容が復元できる
        var export = await client.GetAsync("/api/masters/csv/products");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var bytes = await export.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.StartsWith("Code,Name,Unit,Specification,Type,StandardDefectRate,DefaultLocationCode,IsActive\r\n", text);
        Assert.Contains("P-001,テスト製品,個,規格A,Product,1.5,,true", text);
        Assert.Contains("\"カンマ, と \"\"引用符\"\" を含む名前\"", text);
    }

    [Fact]
    public async Task 既存コードの行は更新され列を省くと現在値が保たれる()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);
        await MasterTests.CreateProductAsync(client, "P-001", "旧名称", ProductType.Product);

        // Unit列などを省いたCSVでも、既存の値はそのまま残る
        var result = await ImportAsync(client, "products", """
            Code,Name,Unit
            P-001,新名称,個
            """);
        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Updated);

        var products = await client.GetFromJsonAsync<List<ProductResponse>>("/api/products");
        var product = Assert.Single(products!);
        Assert.Equal("新名称", product.Name);
        Assert.Equal(ProductType.Product, product.Type);

        // IsActive=false で無効化できる
        var deactivated = await ImportAsync(client, "products", """
            Code,Name,Unit,IsActive
            P-001,新名称,個,false
            """);
        Assert.True(deactivated.Succeeded);
        var active = await client.GetFromJsonAsync<List<ProductResponse>>("/api/products");
        Assert.Empty(active!);
    }

    [Fact]
    public async Task エラー行があると全件ロールバックされ行番号付きで返る()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        var result = await ImportAsync(client, "products", """
            Code,Name,Unit,Type,StandardDefectRate
            P-001,正しい行,個,Product,0
            P-002,,個,Product,0
            P-003,区分が不正,個,Unknown,0
            P-004,不良率が範囲外,個,Product,200
            P-001,コード重複,個,Product,0
            """);

        Assert.False(result.Succeeded);
        Assert.Equal(0, result.Created);
        Assert.Contains(result.Errors, e => e.Line == 3 && e.Message.Contains("Name"));
        Assert.Contains(result.Errors, e => e.Line == 4 && e.Message.Contains("Type"));
        Assert.Contains(result.Errors, e => e.Line == 5 && e.Message.Contains("StandardDefectRate"));
        Assert.Contains(result.Errors, e => e.Line == 6 && e.Message.Contains("複数行"));

        // 正しい行も含めて1件も登録されない
        var products = await client.GetFromJsonAsync<List<ProductResponse>>("/api/products?includeInactive=true");
        Assert.Empty(products!);
    }

    [Fact]
    public async Task 検証のみの取込はDBに反映されない()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        var result = await ImportAsync(client, "processes", """
            Code,Name,Category
            PR-01,組立,InHouse
            """, dryRun: true);

        Assert.True(result.Succeeded);
        Assert.True(result.DryRun);
        Assert.Equal(1, result.Created);
        var processes = await client.GetFromJsonAsync<List<ProcessResponse>>("/api/processes?includeInactive=true");
        Assert.Empty(processes!);
    }

    [Fact]
    public async Task 必須列が無いCSVは取り込まれない()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        var result = await ImportAsync(client, "products", """
            Code,Name
            P-001,単位列なし
            """);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Message.Contains("Unit"));
    }

    [Fact]
    public async Task 英語では列の案内と取込エラーが英語で返り列キーは変わらない()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");

        var kinds = await client.GetFromJsonAsync<List<CsvKindInfo>>("/api/masters/csv/kinds");
        var products = kinds!.Single(k => k.Kind == "products");
        Assert.Equal("Items", products.Label);
        var code = products.Columns.Single(c => c.Name == "Code");
        Assert.Equal(("Item code", "Updated if it matches an existing code, otherwise created"), (code.Label, code.Note));
        var actuals = await client.GetFromJsonAsync<List<CsvKindInfo>>("/api/actuals/csv/kinds");
        Assert.Equal("Receiving", actuals!.Single(k => k.Kind == "receiving").Label);

        var result = await ImportAsync(client, "products", """
            Code,Name,Unit,Type,StandardDefectRate
            P-001,,個,Product,0
            P-002,範囲外,個,Product,200
            P-003,正しい行,個,Product,0
            P-003,コード重複,個,Product,0
            """);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Line == 2 && e.Message == "Name is required.");
        Assert.Contains(result.Errors, e => e.Line == 3 && e.Message == "StandardDefectRate must be 100 or less ('200').");
        Assert.Contains(result.Errors, e => e.Line == 5 && e.Message == "Item code 'P-003' appears on multiple rows.");
    }

    [Fact]
    public async Task ShiftJISのCSVと日本語ラベルを取り込める()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var csv = "Code,Name,Unit,Type\r\nP-100,日本語の品目名,個,部材\r\n";
        var content = new ByteArrayContent(Encoding.GetEncoding(932).GetBytes(csv));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        var response = await client.PostAsync("/api/masters/csv/products", content);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CsvImportResult>();

        Assert.True(result!.Succeeded, string.Join(" / ", result.Errors.Select(e => e.Message)));
        var products = await client.GetFromJsonAsync<List<ProductResponse>>("/api/products");
        var product = Assert.Single(products!);
        Assert.Equal("日本語の品目名", product.Name);
        Assert.Equal(ProductType.Material, product.Type);
    }

    [Fact]
    public async Task MBOMと工順をコード指定のCSVで一括登録できる()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        var setup = await ImportAsync(client, "products", """
            Code,Name,Unit,Type
            FG-01,完成品,個,Product
            RM-01,部材1,個,Material
            RM-02,部材2,個,Material
            """);
        Assert.True(setup.Succeeded);
        Assert.True((await ImportAsync(client, "processes", """
            Code,Name,Category
            PR-01,加工,InHouse
            PR-02,組立,InHouse
            """)).Succeeded);

        var bom = await ImportAsync(client, "bom", """
            ParentProductCode,ChildProductCode,QuantityPer,MakeOrBuy,AlternativeGroup
            FG-01,RM-01,2,InHouse,
            FG-01,RM-02,1.5,Outsourced,ALT-1
            """);
        Assert.True(bom.Succeeded, string.Join(" / ", bom.Errors.Select(e => e.Message)));
        Assert.Equal(1, bom.Created);

        var products = await client.GetFromJsonAsync<List<ProductResponse>>("/api/products");
        var parentId = products!.Single(p => p.Code == "FG-01").Id;
        var bomLines = await client.GetFromJsonAsync<List<BomItemResponse>>($"/api/products/{parentId}/bom");
        Assert.Equal(2, bomLines!.Count);
        Assert.Equal(1.5m, bomLines.Single(b => b.ChildProductCode == "RM-02").QuantityPer);

        var routing = await ImportAsync(client, "routing", """
            ProductCode,Sequence,ProcessCode,StandardWorkMinutes,StandardSetupMinutes,ControlItems
            FG-01,1,PR-01,30,10,温度
            FG-01,2,PR-02,20,5,
            """);
        Assert.True(routing.Succeeded, string.Join(" / ", routing.Errors.Select(e => e.Message)));
        var steps = await client.GetFromJsonAsync<List<RoutingStepResponse>>($"/api/products/{parentId}/routing");
        Assert.Equal(2, steps!.Count);
        Assert.Equal("PR-01", steps[0].ProcessCode);
        Assert.Equal("温度", steps[0].ControlItems);

        // 同じ品目を再取込すると工順は一括置換される
        var replaced = await ImportAsync(client, "routing", """
            ProductCode,Sequence,ProcessCode,StandardWorkMinutes
            FG-01,1,PR-02,15
            """);
        Assert.True(replaced.Succeeded);
        Assert.Equal(1, replaced.Updated);
        steps = await client.GetFromJsonAsync<List<RoutingStepResponse>>($"/api/products/{parentId}/routing");
        Assert.Equal("PR-02", Assert.Single(steps!).ProcessCode);

        // 未登録コードの参照はエラー
        var unknown = await ImportAsync(client, "routing", """
            ProductCode,Sequence,ProcessCode
            FG-01,1,PR-99
            """);
        Assert.False(unknown.Succeeded);
        Assert.Contains(unknown.Errors, e => e.Message.Contains("PR-99"));
    }

    [Fact]
    public async Task チェックリストは同一コードの複数行が1件にまとまる()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        var result = await ImportAsync(client, "checklists", """
            Code,Name,Category,IsActive,Sequence,Text,IsRequired
            CL-01,始業前点検,Setup,true,1,安全カバー確認,true
            CL-01,始業前点検,Setup,true,2,油量確認,false
            CL-02,HSE点検,Hse,true,1,保護具の着用,true
            """);
        Assert.True(result.Succeeded, string.Join(" / ", result.Errors.Select(e => e.Message)));
        Assert.Equal(2, result.Created);

        var checklists = await client.GetFromJsonAsync<List<ChecklistResponse>>("/api/checklists");
        var checklist = checklists!.Single(c => c.Code == "CL-01");
        Assert.Equal(2, checklist.Items.Count);
        Assert.Equal("油量確認", checklist.Items[1].Text);
        Assert.False(checklist.Items[1].IsRequired);

        // 再取込で項目は一括置換される
        var replaced = await ImportAsync(client, "checklists", """
            Code,Name,Category,Sequence,Text,IsRequired
            CL-01,始業前点検,Setup,1,安全カバー確認,true
            """);
        Assert.True(replaced.Succeeded);
        Assert.Equal(1, replaced.Updated);
        checklists = await client.GetFromJsonAsync<List<ChecklistResponse>>("/api/checklists");
        Assert.Single(checklists!.Single(c => c.Code == "CL-01").Items);
    }

    [Fact]
    public async Task 検査項目は基準が変わる更新で版数が上がる()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        Assert.True((await ImportAsync(client, "inspection-items", """
            Code,Name,Type,LowerLimit,UpperLimit,StandardValue,Method,SamplingCount
            INS-01,外径測定,InProcess,9.5,10.5,10,ノギス,5
            """)).Succeeded);

        // 名称だけの変更では版数は上がらない
        Assert.True((await ImportAsync(client, "inspection-items", """
            Code,Name,Type,LowerLimit,UpperLimit,StandardValue,Method,SamplingCount
            INS-01,外径測定（改称）,InProcess,9.5,10.5,10,ノギス,5
            """)).Succeeded);
        var items = await client.GetFromJsonAsync<List<InspectionItemResponse>>("/api/inspection-items");
        Assert.Equal(1, items!.Single().Version);

        // 規格値の変更で版数が上がる（C-10-10-03）
        Assert.True((await ImportAsync(client, "inspection-items", """
            Code,Name,Type,LowerLimit,UpperLimit,StandardValue,Method,SamplingCount
            INS-01,外径測定（改称）,InProcess,9,11,10,マイクロメータ,10
            """)).Succeeded);
        items = await client.GetFromJsonAsync<List<InspectionItemResponse>>("/api/inspection-items");
        Assert.Equal(2, items!.Single().Version);

        // 下限が上限を超える行はエラー
        var invalid = await ImportAsync(client, "inspection-items", """
            Code,Name,LowerLimit,UpperLimit
            INS-02,逆転,10,1
            """);
        Assert.False(invalid.Succeeded);
        Assert.Contains(invalid.Errors, e => e.Message.Contains("下限"));
    }

    [Fact]
    public async Task ユーザーとスキル割当をCSVで一括登録できる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);

        Assert.True((await ImportAsync(admin, "skills", """
            Code,Name,Type,RequiresExpiry
            SK-01,溶接資格,Certification,true
            """)).Succeeded);

        var users = await ImportAsync(admin, "users", """
            UserName,DisplayName,Roles,IsActive,InitialPassword
            worker1,作業者1,Operator;QualityControl,true,Passw0rd123
            worker2,作業者2,Operator,true,Passw0rd123
            """);
        Assert.True(users.Succeeded, string.Join(" / ", users.Errors.Select(e => e.Message)));
        Assert.Equal(2, users.Created);

        var list = await admin.GetFromJsonAsync<List<UserSummaryResponse>>("/api/users");
        var worker1 = list!.Single(u => u.UserName == "worker1");
        Assert.Equal(["Operator", "QualityControl"], worker1.Roles.Order());
        Assert.True(worker1.MustChangePassword);

        // 登録したパスワードでログインできる
        using var workerClient = factory.CreateClient();
        await TestAuth.LoginAsync(workerClient, "worker1", "Passw0rd123");

        // スキル割当
        var skills = await ImportAsync(admin, "user-skills", """
            UserName,SkillCode,AcquiredOn,ExpiresOn
            worker1,SK-01,2024-04-01,2027/03/31
            """);
        Assert.True(skills.Succeeded, string.Join(" / ", skills.Errors.Select(e => e.Message)));
        var assigned = await admin.GetFromJsonAsync<List<UserSkillResponse>>($"/api/users/{worker1.Id}/skills");
        Assert.Equal(new DateOnly(2024, 4, 1), Assert.Single(assigned!).AcquiredOn);

        // 弱いパスワードや不明なロールはエラー
        var invalid = await ImportAsync(admin, "users", """
            UserName,DisplayName,Roles,InitialPassword
            worker3,作業者3,UnknownRole,short
            """);
        Assert.False(invalid.Succeeded);
        Assert.Contains(invalid.Errors, e => e.Message.Contains("UnknownRole"));
    }

    [Fact]
    public async Task 権限のないユーザーは取込できずユーザーCSVは管理者専用()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        using var operator_ = await TestAuth.CreateUserClientAsync(
            factory, admin, "operator1", "Passw0rd123", MesRoles.Operator);
        using var manager = await TestAuth.CreateUserClientAsync(
            factory, admin, "manager1", "Passw0rd123", MesRoles.ProductionManager);

        // 参照は誰でも可、取込はマスタ更新権限が必要
        Assert.Equal(HttpStatusCode.OK, (await operator_.GetAsync("/api/masters/csv/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await PostCsvAsync(operator_, "products", "Code,Name,Unit\nP-1,x,個\n")).StatusCode);

        // 生産管理担当者はマスタを取り込めるが、ユーザーCSVは扱えない
        Assert.Equal(HttpStatusCode.OK,
            (await PostCsvAsync(manager, "products", "Code,Name,Unit\nP-1,x,個\n")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/masters/csv/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await PostCsvAsync(manager, "users", "UserName,DisplayName\nu1,ユーザー1\n")).StatusCode);

        // スキル・資格は単票APIが管理者専用のため、CSV取込も管理者専用（参照は可）
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/masters/csv/skills")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await PostCsvAsync(manager, "skills", "Code,Name\nS-1,溶接\n")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await PostCsvAsync(admin, "skills", "Code,Name\nS-1,溶接\n")).StatusCode);
    }

    [Fact]
    public async Task 対応していない種別は404を返す()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/masters/csv/unknown")).StatusCode);

        var kinds = await client.GetFromJsonAsync<List<CsvKindInfo>>("/api/masters/csv/kinds");
        Assert.Contains(kinds!, k => k.Kind == "products");
        Assert.Contains(kinds!, k => k.Kind == "users" && k.UserAdminOnly);
    }

    [Fact]
    public async Task サンプルCSVがファイル名の順にそのまま取り込める()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        var directory = FindSampleDirectory();
        var files = Directory.GetFiles(directory, "*.csv").OrderBy(f => f, StringComparer.Ordinal).ToList();
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            // 01_processes.csv → processes（ファイル名の番号は取込順のガイド）
            var kind = Path.GetFileNameWithoutExtension(file).Split('_', 2)[1];
            var result = await ImportAsync(client, kind, await File.ReadAllTextAsync(file));
            Assert.True(result.Succeeded,
                $"{Path.GetFileName(file)}: {string.Join(" / ", result.Errors.Select(e => $"{e.Line}行目 {e.Message}"))}");
            Assert.Equal(result.Created + result.Updated > 0, result.DataRows > 0);
        }

        // 参照が解決され、製造指図を工程展開できる状態（工順が登録済み）になっている
        var products = (await client.GetFromJsonAsync<List<ProductResponse>>("/api/products"))!;
        var pump = products.Single(p => p.Code == "FG-1000");
        var bom = await client.GetFromJsonAsync<List<BomItemResponse>>($"/api/products/{pump.Id}/bom");
        Assert.Equal(7, bom!.Count);
        Assert.Single(bom, b => b.IsAlternative);
        var routing = await client.GetFromJsonAsync<List<RoutingStepResponse>>($"/api/products/{pump.Id}/routing");
        Assert.Equal(3, routing!.Count);
        Assert.All(routing, step => Assert.False(string.IsNullOrEmpty(step.ProcessCode)));

        // 2回目の取込は全件更新になる（同じファイルを取り込み直しても増えない）
        var again = await ImportAsync(client, "products",
            await File.ReadAllTextAsync(files.Single(f => f.Contains("products"))));
        Assert.True(again.Succeeded);
        Assert.Equal(0, again.Created);
        var reloaded = await client.GetFromJsonAsync<List<ProductResponse>>("/api/products");
        Assert.Equal(products.Count, reloaded!.Count);

        // 続けて実績のサンプル（samples/actual-csv）もファイル名の順に取り込める
        foreach (var file in Directory.GetFiles(FindSampleDirectory("actual-csv"), "*.csv")
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            var kind = Path.GetFileNameWithoutExtension(file).Split('_', 2)[1];
            var content = new StringContent(await File.ReadAllTextAsync(file), Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("text/csv") { CharSet = "utf-8" };
            var response = await client.PostAsync($"/api/actuals/csv/{kind}", content);
            response.EnsureSuccessStatusCode();
            var result = (await response.Content.ReadFromJsonAsync<CsvImportResult>())!;
            Assert.True(result.Succeeded,
                $"{Path.GetFileName(file)}: {string.Join(" / ", result.Errors.Select(e => $"{e.Line}行目 {e.Message}"))}");
            Assert.True(result.Created > 0, Path.GetFileName(file));
        }
    }

    /// <summary>テスト実行ディレクトリから遡って samples/{name} を探す</summary>
    private static string FindSampleDirectory(string name = "master-csv")
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var samples = Path.Combine(directory.FullName, "samples", name);
            if (Directory.Exists(samples))
            {
                return samples;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException($"samples/{name} が見つかりません。");
    }

    [Fact]
    public async Task 画面と同じファイルアップロード形式で取り込める()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        // 画面（InputFile）からは multipart/form-data で送られる
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("Code,Name,Unit\r\nP-001,製品1,個\r\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "products.csv");

        var response = await client.PostAsync("/api/masters/csv/products", form);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CsvImportResult>();
        Assert.True(result!.Succeeded, string.Join(" / ", result.Errors.Select(e => e.Message)));
        Assert.Equal(1, result.Created);

        // ファイル未選択のフォームは400
        using var empty = new MultipartFormDataContent { { new StringContent("true"), "dryRun" } };
        var noFile = await client.PostAsync("/api/masters/csv/products", empty);
        Assert.Equal(HttpStatusCode.BadRequest, noFile.StatusCode);
    }

    [Fact]
    public async Task テンプレートはヘッダー行のみを返す()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        var response = await client.GetAsync("/api/masters/csv/locations/template");
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.Equal("Code,WorkCenterCode,AreaType,ShelfNo,IsActive\r\n", text);
    }

    [Fact]
    public async Task 不良理由マスタをCSVで入出力できる()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        // テンプレートの列順（英語固定）
        var template = await client.GetAsync("/api/masters/csv/defect-reasons/template");
        template.EnsureSuccessStatusCode();
        var bytes = await template.Content.ReadAsByteArrayAsync();
        Assert.Equal("Code,Name,Category,IsActive\r\n", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));

        // 取込（新規2件）。区分は日本語ラベルでも受け付ける
        var result = await ImportAsync(client, "defect-reasons",
            "Code,Name,Category,IsActive\nDF-01,寸法外れ,Process,true\nDF-02,キズ,材質・部材,true\n");
        Assert.Equal(2, result.Created);
        Assert.Equal(0, result.Updated);
        Assert.Empty(result.Errors);

        // 同じコードは更新になる
        var again = await ImportAsync(client, "defect-reasons",
            "Code,Name,Category,IsActive\nDF-01,寸法外れ（外径）,Equipment,true\n");
        Assert.Equal(0, again.Created);
        Assert.Equal(1, again.Updated);

        var reasons = await client.GetFromJsonAsync<List<DefectReasonResponse>>("/api/defect-reasons");
        Assert.Equal(2, reasons!.Count);
        var updated = reasons.Single(r => r.Code == "DF-01");
        Assert.Equal("寸法外れ（外径）", updated.Name);
        Assert.Equal(DefectReasonCategory.Equipment, updated.Category);
        Assert.Equal(DefectReasonCategory.Material, reasons.Single(r => r.Code == "DF-02").Category);

        // 出力は取込した内容を返す
        var export = await client.GetAsync("/api/masters/csv/defect-reasons");
        export.EnsureSuccessStatusCode();
        var exported = Encoding.UTF8.GetString(await export.Content.ReadAsByteArrayAsync());
        Assert.Contains("DF-01,寸法外れ（外径）,Equipment,true", exported);
    }

    [Fact]
    public async Task ユーザーCSVで最後のシステム管理者を降格できない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);

        // 唯一の管理者を生産管理担当者へ降格する取込は失敗し、何も反映されない
        var demote = await ImportAsync(admin, "users", $"""
            UserName,DisplayName,Roles,IsActive
            {TestAuth.AdminUser},管理者,ProductionManager,true
            """);
        Assert.False(demote.Succeeded);
        Assert.Contains(demote.Errors, e => e.Message.Contains("システム管理者"));

        var unchanged = (await admin.GetFromJsonAsync<List<UserSummaryResponse>>("/api/users"))!
            .Single(u => u.UserName == TestAuth.AdminUser);
        Assert.Contains(MesRoles.SystemAdmin, unchanged.Roles);

        // 同じ取込の中で別の管理者を立てるなら通る（行の順序に依存しない）
        var handover = await ImportAsync(admin, "users", $"""
            UserName,DisplayName,Roles,IsActive,InitialPassword
            {TestAuth.AdminUser},管理者,ProductionManager,true,
            admin2,管理者2,SystemAdmin,true,Passw0rd123
            """);
        Assert.True(handover.Succeeded, string.Join(" / ", handover.Errors.Select(e => e.Message)));
    }

    [Fact]
    public async Task 作業区をCSVで一括登録でき上位が後の行でも解決される()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        // 上位（P1）を最後に書いても解決できる＝ファイル内の行順に依存しない
        var csv = """
            Code,Name,Level,ParentCode,IsActive
            WC01,溶接作業区,WorkCenter,A1,true
            A1,前工程エリア,Area,L1,true
            L1,組立1ライン,Line,P1,true
            P1,第一工場,Plant,,true
            """;
        var result = await ImportAsync(client, "work-centers", csv);
        Assert.True(result.Succeeded, string.Join(" / ", result.Errors.Select(e => e.Message)));
        Assert.Equal(4, result.Created);

        var items = await client.GetFromJsonAsync<List<WorkCenterResponse>>("/api/work-centers");
        Assert.Equal("A1", items!.Single(x => x.Code == "WC01").ParentCode);
        Assert.Null(items!.Single(x => x.Code == "P1").ParentId);

        // 出力は上の段から並ぶ
        var export = await client.GetAsync("/api/masters/csv/work-centers");
        var bytes = await export.Content.ReadAsByteArrayAsync();
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.StartsWith("Code,Name,Level,ParentCode,IsActive\r\nP1,第一工場,Plant,,true", text);
        Assert.Contains("WC01,溶接作業区,WorkCenter,A1,true", text);
    }

    [Fact]
    public async Task 作業区CSVは段の飛び越しと未登録の上位を行番号付きで拒否する()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        // 単票APIと同じ判定（WorkCenterHierarchyPolicy）が効く
        var skipped = await ImportAsync(client, "work-centers", """
            Code,Name,Level,ParentCode,IsActive
            P1,第一工場,Plant,,true
            WC01,溶接作業区,WorkCenter,P1,true
            """);
        Assert.False(skipped.Succeeded);
        Assert.Contains(skipped.Errors, e => e.Line == 3 && e.Message.Contains("エリア"));

        var missing = await ImportAsync(client, "work-centers", """
            Code,Name,Level,ParentCode,IsActive
            L1,組立1ライン,Line,P9,true
            """);
        Assert.False(missing.Succeeded);
        Assert.Contains(missing.Errors, e => e.Line == 2 && e.Message.Contains("P9"));

        // 1行でもエラーなら全件ロールバックされる
        var items = await client.GetFromJsonAsync<List<WorkCenterResponse>>("/api/work-centers");
        Assert.Empty(items!);
    }

    [Fact]
    public async Task 設備とロケーションのCSVで作業区をコード参照できる()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        Assert.True((await ImportAsync(client, "work-centers", """
            Code,Name,Level,ParentCode,IsActive
            P1,第一工場,Plant,,true
            L1,組立1ライン,Line,P1,true
            A1,前工程エリア,Area,L1,true
            WC01,溶接作業区,WorkCenter,A1,true
            """)).Succeeded);

        var equipments = await ImportAsync(client, "equipments", """
            AssetNo,Name,WorkCenterCode,Site,Status,MaintenanceType,MaintenanceThreshold,MaintenanceParts,IsActive
            EQ-01,プレス機,WC01,,Available,None,,,true
            """);
        Assert.True(equipments.Succeeded, string.Join(" / ", equipments.Errors.Select(e => e.Message)));
        var equipmentList = await client.GetFromJsonAsync<List<EquipmentResponse>>("/api/equipments");
        Assert.Equal("WC01", equipmentList!.Single().WorkCenterCode);

        // 設備に作業区以外の段を指定すると、単票APIと同じ理由で弾かれる
        var wrongLevel = await ImportAsync(client, "equipments", """
            AssetNo,Name,WorkCenterCode,Site,Status,MaintenanceType,MaintenanceThreshold,MaintenanceParts,IsActive
            EQ-02,旋盤,L1,,Available,None,,,true
            """);
        Assert.False(wrongLevel.Succeeded);
        Assert.Contains(wrongLevel.Errors, e => e.Line == 2 && e.Message.Contains("作業区を指定"));

        // 未登録の作業区コードは行番号付きで拒否
        var missing = await ImportAsync(client, "locations", """
            Code,WorkCenterCode,AreaType,ShelfNo,IsActive
            WH-01,WC99,MaterialWarehouse,A-1,true
            """);
        Assert.False(missing.Succeeded);
        Assert.Contains(missing.Errors, e => e.Line == 2 && e.Message.Contains("WC99"));

        // ロケーションは工場にも紐付けられる
        var locations = await ImportAsync(client, "locations", """
            Code,WorkCenterCode,AreaType,ShelfNo,IsActive
            WH-01,P1,MaterialWarehouse,A-1,true
            """);
        Assert.True(locations.Succeeded, string.Join(" / ", locations.Errors.Select(e => e.Message)));
        var locationList = await client.GetFromJsonAsync<List<LocationResponse>>("/api/locations");
        Assert.Equal("P1", locationList!.Single().WorkCenterCode);

        // 列を省くと現在の作業区が保たれる（列単位の部分更新）
        var kept = await ImportAsync(client, "locations", """
            Code,ShelfNo
            WH-01,B-2
            """);
        Assert.True(kept.Succeeded, string.Join(" / ", kept.Errors.Select(e => e.Message)));
        var afterKeep = await client.GetFromJsonAsync<List<LocationResponse>>("/api/locations");
        Assert.Equal("P1", afterKeep!.Single().WorkCenterCode);
        Assert.Equal("B-2", afterKeep!.Single().ShelfNo);

        // 列があって空欄なら紐付けを外す
        var cleared = await ImportAsync(client, "locations", """
            Code,WorkCenterCode
            WH-01,
            """);
        Assert.True(cleared.Succeeded, string.Join(" / ", cleared.Errors.Select(e => e.Message)));
        var afterClear = await client.GetFromJsonAsync<List<LocationResponse>>("/api/locations");
        Assert.Null(afterClear!.Single().WorkCenterId);
    }

    [Fact]
    public async Task 工順とユーザーのCSVで作業区をコード参照できる()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        Assert.True((await ImportAsync(client, "work-centers", """
            Code,Name,Level,ParentCode,IsActive
            P1,第一工場,Plant,,true
            L1,組立1ライン,Line,P1,true
            A1,前工程エリア,Area,L1,true
            WC01,溶接作業区,WorkCenter,A1,true
            """)).Succeeded);
        Assert.True((await ImportAsync(client, "products", """
            Code,Name,Unit,Type
            FG-01,完成品,個,Product
            """)).Succeeded);
        Assert.True((await ImportAsync(client, "processes", """
            Code,Name,Category
            PR-01,組立,InHouse
            """)).Succeeded);

        var routing = await ImportAsync(client, "routing", """
            ProductCode,Sequence,ProcessCode,StandardWorkMinutes,StandardSetupMinutes,WorkCenterCode
            FG-01,1,PR-01,30,10,WC01
            """);
        Assert.True(routing.Succeeded, string.Join(" / ", routing.Errors.Select(e => e.Message)));
        var products = await client.GetFromJsonAsync<List<ProductResponse>>("/api/products");
        var steps = await client.GetFromJsonAsync<List<RoutingStepResponse>>(
            $"/api/products/{products!.Single().Id}/routing");
        Assert.Equal("WC01", steps!.Single().WorkCenterCode);

        // 工順の作業区は最下段のみ。上位の段は候補に入らないため未登録として弾かれる
        var wrongLevel = await ImportAsync(client, "routing", """
            ProductCode,Sequence,ProcessCode,StandardWorkMinutes,StandardSetupMinutes,WorkCenterCode
            FG-01,1,PR-01,30,10,L1
            """);
        Assert.False(wrongLevel.Succeeded);
        Assert.Contains(wrongLevel.Errors, e => e.Line == 2 && e.Message.Contains("L1"));

        // ユーザーの作業場所は段を問わない
        var users = await ImportAsync(client, "users", """
            UserName,DisplayName,Roles,WorkCenterCode,IsActive,InitialPassword
            op1,作業者1,Operator,P1,true,Passw0rd!x
            """);
        Assert.True(users.Succeeded, string.Join(" / ", users.Errors.Select(e => e.Message)));
        var userList = await client.GetFromJsonAsync<List<UserSummaryResponse>>("/api/users");
        Assert.Equal("P1", userList!.Single(u => u.UserName == "op1").WorkCenterCode);
    }

    [Fact]
    public async Task 工程管理項目をCSVで一括登録でき許容範囲の矛盾は行番号付きで拒否される()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);
        Assert.True((await ImportAsync(client, "processes", """
            Code,Name,Category
            PR-01,加熱,InHouse
            """)).Succeeded);

        var result = await ImportAsync(client, "control-items", """
            Code,Name,Unit,TargetProductCode,TargetProcessCode,TargetValue,LowerLimit,UpperLimit,IsActive
            CI-01,加熱温度,℃,,PR-01,180,175,185,true
            """);
        Assert.True(result.Succeeded, string.Join(" / ", result.Errors.Select(e => e.Message)));
        var items = await client.GetFromJsonAsync<List<ControlItemResponse>>("/api/control-items");
        Assert.Equal(180m, items!.Single().TargetValue);
        Assert.Equal("PR-01", items!.Single().TargetProcessCode);

        // 単票APIと同じ条件で弾かれる
        var invalid = await ImportAsync(client, "control-items", """
            Code,Name,TargetValue,LowerLimit,UpperLimit
            CI-02,逆転,,200,100
            CI-03,範囲外,300,175,185
            """);
        Assert.False(invalid.Succeeded);
        Assert.Contains(invalid.Errors, e => e.Line == 2 && e.Message.Contains("許容下限"));
        Assert.Contains(invalid.Errors, e => e.Line == 3 && e.Message.Contains("指示値"));

        // 既存コードの更新で版数が上がる
        var revised = await ImportAsync(client, "control-items", """
            Code,Name,TargetValue,LowerLimit,UpperLimit
            CI-01,加熱温度,182,178,186
            """);
        Assert.True(revised.Succeeded, string.Join(" / ", revised.Errors.Select(e => e.Message)));
        Assert.Equal(1, revised.Updated);
        var after = await client.GetFromJsonAsync<List<ControlItemResponse>>("/api/control-items");
        Assert.Equal(2, after!.Single().Version);
    }

    [Fact]
    public async Task 設備の保全部品をCSVで一括置換できる()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);
        Assert.True((await ImportAsync(client, "equipments", """
            AssetNo,Name,Status,MaintenanceType
            EQ-01,プレス機,Available,None
            """)).Succeeded);
        Assert.True((await ImportAsync(client, "products", """
            Code,Name,Unit,Type
            PT-01,金型A,個,Material
            PT-02,Oリング,個,Material
            """)).Succeeded);

        var result = await ImportAsync(client, "equipment-parts", """
            EquipmentAssetNo,ProductCode,Category,QuantityPer,Note
            EQ-01,PT-01,資産管理部品,1,個体管理
            EQ-01,PT-02,消耗品,2,
            """);
        Assert.True(result.Succeeded, string.Join(" / ", result.Errors.Select(e => e.Message)));
        var equipments = await client.GetFromJsonAsync<List<EquipmentResponse>>("/api/equipments");
        var parts = await client.GetFromJsonAsync<List<EquipmentPartResponse>>(
            $"/api/equipments/{equipments!.Single().Id}/parts");
        Assert.Equal(2, parts!.Count);
        Assert.Equal(MaintenancePartCategory.Asset, parts[0].Category);

        // 未登録の設備は行番号付きで拒否
        var unknown = await ImportAsync(client, "equipment-parts", """
            EquipmentAssetNo,ProductCode,Category,QuantityPer
            EQ-99,PT-01,消耗品,1
            """);
        Assert.False(unknown.Succeeded);
        Assert.Contains(unknown.Errors, e => e.Message.Contains("EQ-99"));

        // 一括置換：CSVから外した行は消える
        var replaced = await ImportAsync(client, "equipment-parts", """
            EquipmentAssetNo,ProductCode,Category,QuantityPer
            EQ-01,PT-02,消耗品,5
            """);
        Assert.True(replaced.Succeeded, string.Join(" / ", replaced.Errors.Select(e => e.Message)));
        var after = await client.GetFromJsonAsync<List<EquipmentPartResponse>>(
            $"/api/equipments/{equipments!.Single().Id}/parts");
        Assert.Equal("PT-02", Assert.Single(after!).ProductCode);
        Assert.Equal(5m, after![0].QuantityPer);
    }

    [Fact]
    public async Task 作業手順書をCSVで登録し工順から番号で紐付けできる()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        var procedures = await ImportAsync(client, "work-procedures", """
            ProcedureNo,Title,Steps,Reference,IsActive
            SOP-01,組立作業手順,1. 部材を並べる,,true
            SOP-02,3Dデータの手順,,DOC-1234,true
            """);
        Assert.True(procedures.Succeeded, string.Join(" / ", procedures.Errors.Select(e => e.Message)));
        Assert.Equal(2, procedures.Created);

        // 手順も所在も無い行は行番号付きで拒否される
        var invalid = await ImportAsync(client, "work-procedures", """
            ProcedureNo,Title,Steps,Reference
            SOP-03,空の手順書,,
            """);
        Assert.False(invalid.Succeeded);
        Assert.Equal(2, invalid.Errors[0].Line);

        // 再取込は更新扱いで版数が上がる
        var revised = await ImportAsync(client, "work-procedures", """
            ProcedureNo,Title,Steps
            SOP-01,組立作業手順,1. 部材を並べる／2. 規定トルクで締結する
            """);
        Assert.True(revised.Succeeded, string.Join(" / ", revised.Errors.Select(e => e.Message)));
        Assert.Equal(1, revised.Updated);
        var saved = await client.GetFromJsonAsync<List<WorkProcedureResponse>>("/api/work-procedures");
        Assert.Equal(2, saved!.Single(p => p.ProcedureNo == "SOP-01").Version);
        Assert.Equal("DOC-1234", saved!.Single(p => p.ProcedureNo == "SOP-02").Reference);

        // 工順CSVから手順書番号で紐付ける
        Assert.True((await ImportAsync(client, "products", """
            Code,Name,Unit,Type
            FG-01,完成品,個,Product
            """)).Succeeded);
        Assert.True((await ImportAsync(client, "processes", """
            Code,Name,Category
            PR-01,組立,InHouse
            """)).Succeeded);
        var routing = await ImportAsync(client, "routing", """
            ProductCode,Sequence,ProcessCode,WorkProcedureNo
            FG-01,1,PR-01,SOP-01
            """);
        Assert.True(routing.Succeeded, string.Join(" / ", routing.Errors.Select(e => e.Message)));

        var products = await client.GetFromJsonAsync<List<ProductResponse>>("/api/products");
        var productId = products!.Single(p => p.Code == "FG-01").Id;
        var steps = await client.GetFromJsonAsync<List<RoutingStepResponse>>($"/api/products/{productId}/routing");
        Assert.Equal("SOP-01", Assert.Single(steps!).WorkProcedureNo);

        // 工順から参照中の手順書はCSVからも無効化できない（単票APIと同じ判定）
        var deactivate = await ImportAsync(client, "work-procedures", """
            ProcedureNo,Title,Steps,IsActive
            SOP-01,組立作業手順,1. 部材を並べる,false
            """);
        Assert.False(deactivate.Succeeded);
        Assert.Equal(2, deactivate.Errors[0].Line);
        Assert.Contains("FG-01", deactivate.Errors[0].Message, StringComparison.Ordinal);

        // 参照していない手順書は無効化できる
        Assert.True((await ImportAsync(client, "work-procedures", """
            ProcedureNo,Title,Reference,IsActive
            SOP-02,3Dデータの手順,DOC-1234,false
            """)).Succeeded);

        // 出力にも手順書番号が出る
        var export = await client.GetAsync("/api/masters/csv/routing");
        export.EnsureSuccessStatusCode();
        var exported = Encoding.UTF8.GetString(await export.Content.ReadAsByteArrayAsync());
        Assert.Contains("SOP-01", exported, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 検査機をCSVで登録し校正期限を入出力できる()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        var imported = await ImportAsync(client, "inspection-devices", """
            Code,Name,SerialNo,Location,CalibratedOn,CalibrationDueOn,CalibrationCycleDays,Note,IsActive
            MD-01,ノギス,SN-100,検査室,2026-01-10,2027-01-10,365,,true
            MD-02,三次元測定機,SN-200,検査室,2025-01-10,2026-01-10,365,期限切れ,true
            """);
        Assert.True(imported.Succeeded, string.Join(" / ", imported.Errors.Select(e => e.Message)));
        Assert.Equal(2, imported.Created);

        var devices = (await client.GetFromJsonAsync<List<InspectionDeviceResponse>>(
            "/api/inspection-devices"))!;
        Assert.False(devices.Single(d => d.Code == "MD-01").IsCalibrationExpired);
        // 2026-01-10 期限の機器は、業務日付（2026-09-16 以降のテスト実行時点）から見て期限切れ
        Assert.True(devices.Single(d => d.Code == "MD-02").IsCalibrationExpired);

        // 日付の書式違いは行番号付きで拒否される
        var badDate = await ImportAsync(client, "inspection-devices", """
            Code,Name,CalibrationDueOn
            MD-03,マイクロメータ,2026年1月10日
            """);
        Assert.False(badDate.Succeeded);
        Assert.Equal(2, badDate.Errors[0].Line);

        // 出力にも校正期限が出る
        var export = await client.GetAsync("/api/masters/csv/inspection-devices");
        export.EnsureSuccessStatusCode();
        var exported = Encoding.UTF8.GetString(await export.Content.ReadAsByteArrayAsync());
        Assert.Contains("2027-01-10", exported, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 勤務シフトをCSVで登録し従業員の所属と直をCSVで設定できる()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        var shifts = await ImportAsync(client, "shifts", """
            Code,Name,StartTime,EndTime,IsActive
            D,昼勤,06:00,18:00,true
            N,夜勤,18:00,06:00,true
            """);
        Assert.True(shifts.Succeeded, string.Join(" / ", shifts.Errors.Select(e => e.Message)));
        Assert.Equal(2, shifts.Created);
        // 境界（既定6時）にちょうど接する直は警告なし
        Assert.Empty(shifts.Warnings);
        var saved = await client.GetFromJsonAsync<List<ShiftResponse>>("/api/shifts");
        Assert.True(saved!.Single(s => s.Code == "N").CrossesMidnight);

        // 時間帯が重なる直は行番号付きで拒否される（単票APIと同じ条件）
        var overlapping = await ImportAsync(client, "shifts", """
            Code,Name,StartTime,EndTime
            E,準夜勤,16:00,00:00
            """);
        Assert.False(overlapping.Succeeded);
        Assert.Equal(2, overlapping.Errors[0].Line);

        // 時刻の書式違いも行番号付きで拒否される
        var badTime = await ImportAsync(client, "shifts", """
            Code,Name,StartTime,EndTime
            E,準夜勤,16時,00:00
            """);
        Assert.False(badTime.Succeeded);

        var users = await ImportAsync(client, "users", """
            UserName,DisplayName,Roles,Department,ShiftCode,IsActive,InitialPassword
            op1,作業者1,Operator,第1製造課,N,true,Passw0rd1
            """);
        Assert.True(users.Succeeded, string.Join(" / ", users.Errors.Select(e => e.Message)));
        var list = await client.GetFromJsonAsync<List<UserSummaryResponse>>("/api/users");
        var op1 = list!.Single(u => u.UserName == "op1");
        Assert.Equal("第1製造課", op1.Department);
        Assert.Equal("N", op1.ShiftCode);

        // 所属者がいる直はCSVからも無効化できない（単票APIと同じ判定）
        var deactivate = await ImportAsync(client, "shifts", """
            Code,Name,StartTime,EndTime,IsActive
            N,夜勤,18:00,06:00,false
            """);
        Assert.False(deactivate.Succeeded);
        Assert.Equal(2, deactivate.Errors[0].Line);

        // 所属者がいない直は無効化できる
        Assert.True((await ImportAsync(client, "shifts", """
            Code,Name,StartTime,EndTime,IsActive
            D,昼勤,06:00,18:00,false
            """)).Succeeded);

        // 製造日の境界をまたぐ直は単票APIと同じく拒否しない。ただし警告は返す（Spec.md 5.7）。
        // 警告はエラーと違いロールバックしないので、取込そのものは成功する
        var crossing = await ImportAsync(client, "shifts", """
            Code,Name,StartTime,EndTime,IsActive
            N,夜勤,22:00,07:00,true
            """);
        Assert.True(crossing.Succeeded, string.Join(" / ", crossing.Errors.Select(e => e.Message)));
        var warning = Assert.Single(crossing.Warnings);
        Assert.Equal(2, warning.Line);
        Assert.Contains("製造日の境界時刻", warning.Message, StringComparison.Ordinal);
        Assert.Contains("'N'", warning.Message, StringComparison.Ordinal);
        // 実際に取り込まれている（警告で取り消されていない）
        var crossed = (await client.GetFromJsonAsync<List<ShiftResponse>>("/api/shifts"))!
            .Single(s => s.Code == "N");
        Assert.Equal(new TimeOnly(22, 0), crossed.StartTime);
        Assert.NotNull(crossed.BoundaryWarning);

        // 境界へ戻せば警告も消える
        var back = await ImportAsync(client, "shifts", """
            Code,Name,StartTime,EndTime,IsActive
            N,夜勤,18:00,06:00,true
            """);
        Assert.True(back.Succeeded);
        Assert.Empty(back.Warnings);

        // 列を書かなければ現状維持（作業場所と同じ扱い）
        Assert.True((await ImportAsync(client, "users", """
            UserName,DisplayName
            op1,作業者1（改称）
            """)).Succeeded);
        var kept = (await client.GetFromJsonAsync<List<UserSummaryResponse>>("/api/users"))!
            .Single(u => u.UserName == "op1");
        Assert.Equal("第1製造課", kept.Department);
        Assert.Equal("N", kept.ShiftCode);

        // 出力にも所属と直が出る
        var export = await client.GetAsync("/api/masters/csv/users");
        export.EnsureSuccessStatusCode();
        var exported = Encoding.UTF8.GetString(await export.Content.ReadAsByteArrayAsync());
        Assert.Contains("第1製造課", exported, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 治工具CSVでも使用中を手で付け外しできない()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(client);

        var created = await ImportAsync(client, "tools", """
            Code,Name,ToolType,Status
            T-01,金型A,型,使用可能
            T-02,金型B,型,使用可能
            """);
        Assert.True(created.Succeeded, string.Join(" / ", created.Errors.Select(e => e.Message)));
        var tools = await client.GetFromJsonAsync<List<ToolResponse>>("/api/tools");
        var t01 = tools!.Single(t => t.Code == "T-01");

        var order = await Phase3TestData.CreateReleasedOrderAsync(client, ctx.ProductId, 10m);
        (await client.PostAsJsonAsync("/api/tool-issues",
            new MesApp.Core.Contracts.Maintenance.ToolAllocateRequest(t01.Id, order.WorkOrders[0].Id, null)))
            .EnsureSuccessStatusCode();

        // 引当中の T-01 を使用可能に戻す行、引当の無い T-02 と新規 T-03 を使用中にする行はエラー
        var rejected = await ImportAsync(client, "tools", """
            Code,Name,ToolType,Status
            T-01,金型A,型,使用可能
            T-02,金型B,型,使用中
            T-03,金型C,型,使用中
            """);
        Assert.False(rejected.Succeeded);
        Assert.Contains(rejected.Errors, e => e.Line == 2 && e.Message.Contains("引当中"));
        Assert.Contains(rejected.Errors, e => e.Line == 3 && e.Message.Contains("引当で設定"));
        Assert.Contains(rejected.Errors, e => e.Line == 4 && e.Message.Contains("引当で設定"));

        // Status 列を省いた取込は現在の状態（使用中）を保つので通る
        var kept = await ImportAsync(client, "tools", """
            Code,Name,ToolType
            T-01,金型A（改）,型
            """);
        Assert.True(kept.Succeeded, string.Join(" / ", kept.Errors.Select(e => e.Message)));
    }

    [Fact]
    public async Task 生産計画はキーで上書きし作業区なしも1つのキーとして扱う()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        Assert.True((await ImportAsync(admin, "processes", "Code,Name\nPR-01,組立\nPR-02,検査\n")).Succeeded);
        Assert.True((await ImportAsync(admin, "products", "Code,Name,Unit\nFG-1,製品1,個\n")).Succeeded);
        Assert.True((await ImportAsync(admin, "work-centers", "Code,Name,Level\nPL,工場,Plant\n")).Succeeded);

        var first = await ImportAsync(admin, "production-plans", """
            BusinessDate,ProductCode,ProcessCode,WorkCenterCode,PlannedQuantity,Note
            2026-10-01,FG-1,PR-01,,100,
            2026-10-02,FG-1,PR-01,PL,50,工場分
            2026-10-02,FG-1,PR-02,,0,休止
            """);
        Assert.True(first.Succeeded, string.Join(" / ", first.Errors.Select(e => e.Message)));
        Assert.Equal(3, first.Created);

        // 作業区が空欄の行は、同じ製造日・品目・工程の作業区なしの計画を上書きする（二重に追加しない）
        var revised = await ImportAsync(admin, "production-plans", """
            BusinessDate,ProductCode,ProcessCode,WorkCenterCode,PlannedQuantity
            2026-10-01,FG-1,PR-01,,120
            2026-10-03,FG-1,PR-01,,10
            """);
        Assert.True(revised.Succeeded, string.Join(" / ", revised.Errors.Select(e => e.Message)));
        Assert.Equal((1, 1), (revised.Created, revised.Updated));
        var plans = (await admin.GetFromJsonAsync<List<ProductionPlanResponse>>("/api/production-plans"))!;
        Assert.Equal(4, plans.Count);
        Assert.Equal(120m, plans.Single(p => p.BusinessDate == new DateOnly(2026, 10, 1) && p.WorkCenterId is null).PlannedQuantity);
        Assert.Equal(50m, plans.Single(p => p.WorkCenterCode == "PL").PlannedQuantity);
        // Note 列が無いファイルでは備考を保つ
        Assert.Equal("休止", plans.Single(p => p.ProcessCode == "PR-02").Note);

        // CSVで入れた作業区なしの計画は、単票APIでも同じキーとして重複になる（判定が共通）
        var nullKey = plans.Single(p => p.BusinessDate == new DateOnly(2026, 10, 3));
        var conflict = await admin.PostAsJsonAsync("/api/production-plans", new ProductionPlanRequest(
            nullKey.BusinessDate, nullKey.ProductId, nullKey.ProcessId, null, 5m, null));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        // 存在しないコード・負の数量・ファイル内のキー重複、作業区なしと作業区ありの混在（既存との間・ファイル内とも）は
        // エラーで、正しい行も含めて全体を取り消す
        var invalid = await ImportAsync(admin, "production-plans", """
            BusinessDate,ProductCode,ProcessCode,WorkCenterCode,PlannedQuantity
            2026-10-04,FG-1,PR-01,,10
            2026-10-05,FG-X,PR-01,,10
            2026-10-05,FG-1,PR-01,WC-X,10
            2026-10-05,FG-1,PR-01,,-1
            2026-10-06,FG-1,PR-01,,5
            2026-10-06,FG-1,PR-01,,6
            2026-10-01,FG-1,PR-01,PL,10
            2026-10-07,FG-1,PR-01,,1
            2026-10-07,FG-1,PR-01,PL,1
            """);
        Assert.False(invalid.Succeeded);
        Assert.Equal([3, 4, 5, 7, 8, 9, 10], invalid.Errors.Select(e => e.Line).Order());
        Assert.Equal(4, (await admin.GetFromJsonAsync<List<ProductionPlanResponse>>("/api/production-plans"))!.Count);

        // 出力したCSVをそのまま取り込み直しても差分が出ない
        var exported = await admin.GetStringAsync("/api/masters/csv/production-plans");
        var roundTrip = await ImportAsync(admin, "production-plans", exported);
        Assert.True(roundTrip.Succeeded, string.Join(" / ", roundTrip.Errors.Select(e => e.Message)));
        Assert.Equal((0, 4), (roundTrip.Created, roundTrip.Updated));
        Assert.Equal(exported, await admin.GetStringAsync("/api/masters/csv/production-plans"));
        var after = (await admin.GetFromJsonAsync<List<ProductionPlanResponse>>("/api/production-plans"))!;
        Assert.Equal(plans.Select(p => p.UpdatedAt), after.Select(p => p.UpdatedAt));

        // 取込の権限は単票の登録と同じ（ProductionManage）。作業者は出力できるが取り込めない
        using var operator_ = await TestAuth.CreateUserClientAsync(factory, admin, "op1", "Passw0rd123", MesRoles.Operator);
        Assert.Equal(HttpStatusCode.OK, (await operator_.GetAsync("/api/masters/csv/production-plans")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostCsvAsync(operator_, "production-plans", exported)).StatusCode);
        var kind = (await admin.GetFromJsonAsync<List<CsvKindInfo>>("/api/masters/csv/kinds"))!
            .Single(k => k.Kind == "production-plans");
        Assert.Equal(MesRoleGroups.ProductionManage, kind.WriteRoles);
    }

    private static async Task<CsvImportResult> ImportAsync(
        HttpClient client, string kind, string csv, bool dryRun = false)
    {
        var response = await PostCsvAsync(client, kind, csv, dryRun);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CsvImportResult>())!;
    }

    private static async Task<HttpResponseMessage> PostCsvAsync(
        HttpClient client, string kind, string csv, bool dryRun = false)
    {
        var content = new StringContent(csv, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/csv") { CharSet = "utf-8" };
        return await client.PostAsync($"/api/masters/csv/{kind}?dryRun={(dryRun ? "true" : "false")}", content);
    }
    [Fact]
    public async Task 品目CSVで既定ロケーションをコード参照できる()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);
        (await client.PostAsJsonAsync("/api/locations",
            new LocationRequest("LOC-A", LocationAreaType.MaterialWarehouse, null))).EnsureSuccessStatusCode();

        var result = await ImportAsync(client, "products", """
            Code,Name,Unit,Type,DefaultLocationCode
            P-001,部材A,個,Material,LOC-A
            """);
        Assert.True(result.Succeeded, string.Join(" / ", result.Errors.Select(e => e.Message)));
        var product = (await client.GetFromJsonAsync<List<ProductResponse>>("/api/products"))!
            .Single(p => p.Code == "P-001");
        Assert.Equal("LOC-A", product.DefaultLocationCode);

        // 登録の無いロケーションは行ごと弾く（推奨に出せない参照を通さない）
        var invalid = await ImportAsync(client, "products", """
            Code,Name,Unit,Type,DefaultLocationCode
            P-001,部材A,個,Material,LOC-X
            """);
        Assert.False(invalid.Succeeded);
        Assert.Contains(invalid.Errors, e => e.Message.Contains("LOC-X"));
    }

    [Fact]
    public async Task 三か月分のサンプル実績を取り込むとダッシュボードで週次月次と内訳を比べられる()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);
        var masters = await PostBundleAsync(client, "masters", ZipDirectory(FindSampleDirectory()));
        Assert.True(masters.Succeeded, Describe(masters));

        // 指図・生産実績・設備稼働記録（equipment-logs）の3ファイル
        var bulk = await PostBundleAsync(client, "actuals", ZipDirectory(FindSampleDirectory("actual-csv-bulk")));
        Assert.True(bulk.Succeeded, Describe(bulk));
        Assert.Equal(3, bulk.Files.Count);

        // 月次：7月・8月・9月がそろい、不良率は改善していく
        var monthly = await client.GetFromJsonAsync<DashboardSummaryResponse>(
            "/api/dashboard/trend?from=2026-07-01&to=2026-09-30&unit=Month");
        Assert.Equal(["2026-07-01", "2026-08-01", "2026-09-01"], monthly!.Rows.Select(r => r.Key));
        Assert.All(monthly.Rows, r => Assert.True(r.GoodQuantity > 0));
        Assert.True(monthly.Rows[0].DefectRate > monthly.Rows[2].DefectRate);
        Assert.All(monthly.Rows, r => Assert.NotNull(r.UtilizationRate));

        // 週次：お盆休み（8/10の週）は実績なし
        var weekly = await client.GetFromJsonAsync<DashboardSummaryResponse>(
            "/api/dashboard/trend?from=2026-06-29&to=2026-09-20&unit=Week");
        Assert.Equal(12, weekly!.Rows.Count);
        var obon = weekly.Rows.Single(r => r.Key == "2026-08-10");
        Assert.Equal(0m, obon.GoodQuantity);
        Assert.Null(obon.UtilizationRate);

        // 内訳：夜勤は昼勤より不良率が高く、設備は5台とも出る
        var byShift = await client.GetFromJsonAsync<DashboardSummaryResponse>(
            "/api/dashboard/breakdown?from=2026-06-29&to=2026-09-20&axis=Shift");
        Assert.True(byShift!.Rows.Single(r => r.Key == "N 夜勤").DefectRate
                    > byShift.Rows.Single(r => r.Key == "D 昼勤").DefectRate);
        var byEquipment = await client.GetFromJsonAsync<DashboardSummaryResponse>(
            "/api/dashboard/breakdown?from=2026-06-29&to=2026-09-20&axis=Equipment");
        Assert.Equal(["EQ-01", "EQ-02", "EQ-03", "EQ-04", "EQ-05"], byEquipment!.Rows.Select(r => r.Key));
        var byLine = await client.GetFromJsonAsync<DashboardSummaryResponse>(
            "/api/dashboard/breakdown?from=2026-06-29&to=2026-09-20&axis=Line");
        Assert.Contains(byLine!.Rows, r => r.Key == "LN-MC" && r.UtilizationRate is not null);

        // 設備稼働記録CSVも単票APIと同じ判定を通る（停止には原因が要る、設備は資産番号で指す）
        var invalid = await Phase3TestData.ImportActualCsvAsync(client, "equipment-logs",
            "EquipmentAssetNo,Status,StartedAt,EndedAt,StopCause\n" +
            "EQ-01,Stopped,2026-09-21 08:00,2026-09-21 09:00,\n" +
            "EQ-99,Running,2026-09-21 08:00,2026-09-21 09:00,\n");
        Assert.False(invalid.Succeeded);
        Assert.Contains(invalid.Errors, e => e.Line == 2 && e.Message.Contains("停止原因"));
        Assert.Contains(invalid.Errors, e => e.Line == 3 && e.Message.Contains("EQ-99"));
    }

    // ---- ZIPによる一括取込・一括出力 ----

    [Fact]
    public async Task サンプルのマスタと実績をそれぞれZIPで一括取込できる()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);
        // README.md などCSV以外のファイルは無視される
        var masters = ZipDirectory(FindSampleDirectory());
        var actuals = ZipDirectory(FindSampleDirectory("actual-csv"));

        // 検証のみでは何も残らない
        var dry = await PostBundleAsync(client, "masters", masters, dryRun: true);
        Assert.True(dry.Succeeded, Describe(dry));
        Assert.Equal(19, dry.Files.Count);
        Assert.Empty((await client.GetFromJsonAsync<List<ProductResponse>>("/api/products"))!);

        var imported = await PostBundleAsync(client, "masters", masters);
        Assert.True(imported.Succeeded, Describe(imported));
        Assert.Equal(["01_work-centers.csv", "02_processes.csv"], imported.Files.Take(2).Select(f => f.FileName));
        var pump = (await client.GetFromJsonAsync<List<ProductResponse>>("/api/products"))!.Single(p => p.Code == "FG-1000");
        Assert.Equal(7, (await client.GetFromJsonAsync<List<BomItemResponse>>($"/api/products/{pump.Id}/bom"))!.Count);

        // 実績は同じ種別を番号違いで複数含む（05_consumptions と 07_consumptions）
        var actualResult = await PostBundleAsync(client, "actuals", actuals);
        Assert.True(actualResult.Succeeded, Describe(actualResult));
        Assert.Equal(12, actualResult.Files.Count);

        // マスタと実績が混ざったZIPはどちらの一括取込でも受け付けない
        var mixed = ZipFiles(("01_processes.csv", "Code,Name\nPR-99,検査\n"), ("02_receiving.csv", "ProductCode,Quantity,LocationCode\nRM-3001,1,WH-M01\n"));
        var mixedToMasters = await PostBundleRawAsync(client, "masters", mixed);
        Assert.Equal(HttpStatusCode.BadRequest, mixedToMasters.StatusCode);
        Assert.Contains("02_receiving.csv", await mixedToMasters.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await PostBundleRawAsync(client, "actuals", mixed)).StatusCode);
    }

    [Fact]
    public async Task 一括取込は1ファイルでもエラーがあれば全ファイルを取り消す()
    {
        using var factory = new ApiFactory();
        using var client = await TestAuth.CreateAdminClientAsync(factory);

        var zip = ZipFiles(
            ("01_processes.csv", "Code,Name\nPR-01,組立\n"),
            ("02_products.csv", "Code,Name,Unit,Type\nP-001,部材,個,Material\nP-002,部材,個,NoSuchType\n"),
            ("03_locations.csv", "Code,AreaType\nLOC-1,MaterialWarehouse\n"));
        var result = await PostBundleAsync(client, "masters", zip);
        Assert.False(result.Succeeded);
        Assert.True(result.Files[0].Result.Succeeded);
        var failed = result.Files[1];
        Assert.Equal("02_products.csv", failed.FileName);
        Assert.Contains(failed.Result.Errors, e => e.Line == 3 && e.Message.Contains("NoSuchType"));
        Assert.Equal(["03_locations.csv"], result.NotProcessed);
        // 先に成功した 01_processes.csv も取り消されている
        Assert.Empty((await client.GetFromJsonAsync<List<ProcessResponse>>("/api/processes"))!);

        // ZIPでないもの・CSVの無いZIP・種別が分からないファイルは、取り込む前に拒否する
        Assert.Equal(HttpStatusCode.BadRequest,
            (await PostBundleRawAsync(client, "masters", Encoding.UTF8.GetBytes("Code,Name\n"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await PostBundleRawAsync(client, "masters", ZipFiles(("README.md", "# x")))).StatusCode);
        var unknown = await PostBundleRawAsync(client, "masters", ZipFiles(("01_processes.csv", "Code,Name\nPR-01,組立\n"), ("02_unknown.csv", "A\n1\n")));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<ProcessResponse>>("/api/processes"))!);

        // 展開すると大きすぎるファイル（ZIP爆弾）は展開の途中で打ち切る
        var bomb = ZipFiles(("01_processes.csv", "Code,Name\n" + new string('x', 6 * 1024 * 1024)));
        var bombResponse = await PostBundleRawAsync(client, "masters", bomb);
        Assert.Equal(HttpStatusCode.BadRequest, bombResponse.StatusCode);
        Assert.Contains("大きすぎ", await bombResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task 一括取込はZIP内の全種別の権限が必要()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        using var manager = await TestAuth.CreateUserClientAsync(
            factory, admin, "manager1", "Passw0rd123", MesRoles.ProductionManager);

        // 生産管理担当者は工程を取り込めるが、ユーザーは取り込めない。混ざったZIPは1件も反映しない
        var zip = ZipFiles(
            ("01_processes.csv", "Code,Name\nPR-01,組立\n"),
            ("02_users.csv", "UserName,DisplayName,InitialPassword\nop9,作業者,Passw0rd123\n"));
        Assert.Equal(HttpStatusCode.Forbidden, (await PostBundleRawAsync(manager, "masters", zip)).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<List<ProcessResponse>>("/api/processes"))!);
        Assert.True((await PostBundleAsync(manager, "masters", ZipFiles(("01_processes.csv", "Code,Name\nPR-01,組立\n")))).Succeeded);

        // 実績も同じ（作業者は受入を取り込めない）
        using var operator_ = await TestAuth.CreateUserClientAsync(
            factory, admin, "operator1", "Passw0rd123", MesRoles.Operator);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostBundleRawAsync(operator_, "actuals",
            ZipFiles(("01_receiving.csv", "ProductCode,Quantity,LocationCode\nX,1,Y\n")))).StatusCode);
    }

    [Fact]
    public async Task 全マスタを番号付きZIPで出力しそのまま別のDBへ取り込める()
    {
        // 番号の順は全種別を網羅している（種別を追加したら ImportOrder にも入れる）
        Assert.Equal(
            MesApp.Api.Services.MasterCsvKinds.All.Select(k => k.Kind).Order(),
            MesApp.Api.Services.MasterCsvKinds.ImportOrder.Order());

        byte[] exported;
        using (var source = new ApiFactory())
        {
            using var admin = await TestAuth.CreateAdminClientAsync(source);
            Assert.True((await PostBundleAsync(admin, "masters", ZipDirectory(FindSampleDirectory()))).Succeeded);
            Assert.True((await ImportAsync(admin, "production-plans",
                "BusinessDate,ProductCode,ProcessCode,WorkCenterCode,PlannedQuantity\n2026-10-01,FG-1000,PR-10,LN-MC,80\n")).Succeeded);
            var response = await admin.GetAsync("/api/masters/csv/bundle");
            response.EnsureSuccessStatusCode();
            Assert.Equal("application/zip", response.Content.Headers.ContentType!.MediaType);
            exported = await response.Content.ReadAsByteArrayAsync();

            // 参照を絞っている種別（ユーザー等）は、管理者以外の出力には入らない
            using var manager = await TestAuth.CreateUserClientAsync(
                source, admin, "manager1", "Passw0rd123", MesRoles.ProductionManager);
            var managerNames = EntryNames(await manager.GetByteArrayAsync("/api/masters/csv/bundle"));
            Assert.DoesNotContain("18_users.csv", managerNames);
            Assert.Contains("04_products.csv", managerNames);
        }
        var names = EntryNames(exported);
        Assert.Equal(20, names.Count);
        Assert.Equal("01_work-centers.csv", names[0]);
        Assert.Equal("20_production-plans.csv", names[^1]);

        // パスワードは出力しないため、ユーザー系を除けば空のDBへそのまま取り込める
        using var target = new ApiFactory();
        using var targetAdmin = await TestAuth.CreateAdminClientAsync(target);
        var roundTrip = await PostBundleAsync(targetAdmin, "masters", ExcludeEntries(exported, "18_users.csv", "19_user-skills.csv"));
        Assert.True(roundTrip.Succeeded, Describe(roundTrip));
        var products = (await targetAdmin.GetFromJsonAsync<List<ProductResponse>>("/api/products"))!;
        Assert.Equal(12, products.Count);
        Assert.Equal("WH-P01", products.Single(p => p.Code == "FG-1000").DefaultLocationCode);
        var plan = Assert.Single((await targetAdmin.GetFromJsonAsync<List<ProductionPlanResponse>>("/api/production-plans"))!);
        Assert.Equal(("LN-MC", 80m), (plan.WorkCenterCode, plan.PlannedQuantity));
    }

    private static string Describe(CsvBundleImportResult result) =>
        string.Join(" / ", result.Files.SelectMany(f => f.Result.Errors.Select(e => $"{f.FileName} {e.Line}行目 {e.Message}")));

    private static async Task<CsvBundleImportResult> PostBundleAsync(
        HttpClient client, string area, byte[] zip, bool dryRun = false)
    {
        var response = await PostBundleRawAsync(client, area, zip, dryRun);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CsvBundleImportResult>())!;
    }

    private static Task<HttpResponseMessage> PostBundleRawAsync(
        HttpClient client, string area, byte[] zip, bool dryRun = false)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(zip);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(file, "file", "bundle.zip");
        return client.PostAsync($"/api/{area}/csv/bundle?dryRun={(dryRun ? "true" : "false")}", content);
    }

    private static byte[] ZipDirectory(string directory) =>
        ZipFiles([.. Directory.GetFiles(directory).Select(f => (Path.GetFileName(f), File.ReadAllText(f)))]);

    private static byte[] ZipFiles(params (string Name, string Content)[] files)
    {
        using var buffer = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            foreach (var (name, text) in files)
            {
                using var stream = archive.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(text));
            }
        }
        return buffer.ToArray();
    }

    private static List<string> EntryNames(byte[] zip)
    {
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(zip));
        return [.. archive.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal)];
    }

    private static byte[] ExcludeEntries(byte[] zip, params string[] excluded)
    {
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(zip));
        return ZipFiles([.. archive.Entries
            .Where(e => !excluded.Contains(e.FullName))
            .Select(e =>
            {
                using var reader = new StreamReader(e.Open());
                return (e.FullName, reader.ReadToEnd());
            })]);
    }
}
