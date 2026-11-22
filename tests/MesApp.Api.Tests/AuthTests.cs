using System.Collections;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Resources;
using System.Text.Json;
using System.Text.RegularExpressions;
using MesApp.Api.Localization;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Audit;
using MesApp.Core.Contracts.Auth;
using MesApp.Core.Contracts.Common;
using MesApp.Core.Contracts.Integration;
using MesApp.Core.Contracts.Setup;
using MesApp.Core.Contracts.Users;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MesApp.Api.Tests;

public class AuthTests
{
    private const string AdminUser = "admin";
    private const string AdminPassword = "Passw0rd123";

    private static async Task SetupAdminAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/setup/initialize", new InitializeRequest(AdminUser, AdminPassword, "管理者"));
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ログイン成功でトークンとユーザー情報が返る()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await SetupAdminAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(AdminUser, AdminPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(token);
        Assert.NotEmpty(token.AccessToken);
        Assert.Contains(MesRoles.SystemAdmin, token.User.Roles);

        // リフレッシュトークンはHttpOnly Cookieで返る
        Assert.Contains(response.Headers.GetValues("Set-Cookie"),
            v => v.StartsWith("mesapp_rt=") && v.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(null, "ユーザー名またはパスワードが正しくありません。")]
    [InlineData("ja", "ユーザー名またはパスワードが正しくありません。")]
    [InlineData("en", "The user name or password is incorrect.")]
    [InlineData("en-US,en;q=0.9", "The user name or password is incorrect.")]
    [InlineData("fr", "ユーザー名またはパスワードが正しくありません。")]
    public async Task エラー文言はAcceptLanguageの言語で返り未対応なら日本語になる(string? language, string expected)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await SetupAdminAsync(client);
        if (language is not null)
        {
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        }

        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(AdminUser, "WrongPass999"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal(expected, problem?.Title);
    }

    [Fact]
    public void APIの英訳は原文と同じ埋め込み位置を持つ()
    {
        // 訳で {0} を落としたり {2} を足したりすると、その文言を返すAPIで FormatException になる
        var resources = new ResourceManager(typeof(ApiText))
            .GetResourceSet(new CultureInfo("en"), createIfNotExists: true, tryParents: false);
        Assert.NotNull(resources);

        var mismatched = resources.Cast<DictionaryEntry>()
            .Where(e => !Placeholders((string)e.Key).SetEquals(Placeholders((string)e.Value!)))
            .Select(e => (string)e.Key)
            .ToList();

        Assert.Empty(mismatched);
    }

    private static HashSet<string> Placeholders(string text) =>
        [.. Regex.Matches(text, @"\{(\d+)(?:[:,][^}]*)?\}").Select(m => m.Groups[1].Value)];

    [Fact]
    public async Task 存在しないAPIの文言も英語で返る()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");

        var response = await client.GetAsync("/api/no-such-api");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal("The requested API does not exist.", problem?.Title);
    }

    [Fact]
    public async Task パスワード誤りでは401が返る()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await SetupAdminAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(AdminUser, "WrongPass999"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task 認証なしでは保護エンドポイントにアクセスできない()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task アクセストークンで保護エンドポイントにアクセスできる()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await SetupAdminAsync(client);

        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(AdminUser, AdminPassword));
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var me = await client.GetFromJsonAsync<UserInfo>("/api/auth/me");

        Assert.NotNull(me);
        Assert.Equal(AdminUser, me.UserName);
    }

    [Fact]
    public async Task リフレッシュで新しいトークンが発行され_旧トークンは失効する()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await SetupAdminAsync(client);

        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(AdminUser, AdminPassword));
        login.EnsureSuccessStatusCode();

        // CookieはHttpClientのCookieContainerが自動送信する
        var refresh1 = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refresh1.StatusCode);
        var refreshed = await refresh1.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(refreshed);
        Assert.NotEmpty(refreshed.AccessToken);

        // ローテーションされた新Cookieでもう一度リフレッシュできる
        var refresh2 = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refresh2.StatusCode);
    }

    [Fact]
    public async Task ログアウト後はリフレッシュできない()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await SetupAdminAsync(client);

        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(AdminUser, AdminPassword));
        login.EnsureSuccessStatusCode();

        var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var refresh = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task パスワードを変更すると全リフレッシュトークンが失効する()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await SetupAdminAsync(client);

        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(AdminUser, AdminPassword));
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var change = await client.PostAsJsonAsync(
            "/api/auth/change-password", new ChangePasswordRequest(AdminPassword, "NewPassw0rd456"));
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        var refresh = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);

        // 新パスワードでログインできる
        var relogin = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(AdminUser, "NewPassw0rd456"));
        Assert.Equal(HttpStatusCode.OK, relogin.StatusCode);
    }

    [Fact]
    public async Task 初期パスワードのままでは業務APIを呼べずパスワード変更だけができる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);

        // 管理者が作成したユーザーは MustChangePassword が立つ
        var created = await admin.PostAsJsonAsync("/api/users",
            new CreateUserRequest("worker1", "Passw0rd123", "作業者1", [MesRoles.Operator]));
        created.EnsureSuccessStatusCode();

        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest("worker1", "Passw0rd123"));
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.True(token.User.MustChangePassword);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.AccessToken);

        // 業務APIは403（参照も含めて止める）
        var blocked = await client.GetAsync("/api/locations");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);

        // 自分の情報とパスワード変更だけは通る
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        var change = await client.PostAsJsonAsync("/api/auth/change-password",
            new ChangePasswordRequest("Passw0rd123", "NewPassw0rd456"));
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        // 変更後に再ログインすれば業務APIを呼べる
        await TestAuth.LoginAsync(client, "worker1", "NewPassw0rd456");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/locations")).StatusCode);
    }

    [Fact]
    public async Task 失効済みリフレッシュトークンの再提示でセッション全体を失効させる()
    {
        using var factory = new ApiFactory();
        // Cookieを手で持ち回るため自動Cookie管理は使わない
        using var client = factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        await SetupAdminAsync(client);

        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(AdminUser, AdminPassword));
        login.EnsureSuccessStatusCode();
        var oldCookie = ExtractRefreshCookie(login);

        // 正常なローテーション。ここで旧トークンは失効する
        var rotated = await RefreshWithCookieAsync(client, oldCookie);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var newCookie = ExtractRefreshCookie(rotated);

        // 失効済みの旧トークンを再提示（盗用の想定）
        var reuse = await RefreshWithCookieAsync(client, oldCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        // 正規の利用者が持っている新トークンも巻き添えで失効している（再ログインが必要）
        var afterReuse = await RefreshWithCookieAsync(client, newCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode);
    }

    private static string ExtractRefreshCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie")
            .First(v => v.StartsWith("mesapp_rt=", StringComparison.Ordinal))
            .Split(';')[0];

    private static Task<HttpResponseMessage> RefreshWithCookieAsync(HttpClient client, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", cookie);
        return client.SendAsync(request);
    }

    // --- リバースプロキシでHTTPSを終端する配置（Spec.md 7.4） ---

    private const string ProxyAddress = "10.0.0.1";
    private const string ClientAddress = "192.168.1.50";

    [Fact]
    public async Task 信頼するプロキシ経由のHTTPSではCookieにSecureが付き_監査ログに元の接続元IPが残る()
    {
        using var factory = new ApiFactory(new Dictionary<string, string>
        {
            ["ReverseProxy:KnownProxies:0"] = ProxyAddress,
        });
        using var admin = await TestAuth.CreateAdminClientAsync(factory);

        var response = await LoginViaProxyAsync(factory, remoteAddress: ProxyAddress);

        Assert.Equal(StatusCodes.Status200OK, response.Response.StatusCode);
        Assert.Contains(response.Response.Headers.SetCookie,
            v => v!.StartsWith("mesapp_rt=") && v.Contains("secure", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(await LoginAuditAddressesAsync(admin), ip => ip == ClientAddress);
    }

    [Fact]
    public async Task 信頼していない接続元の転送ヘッダは無視する()
    {
        using var factory = new ApiFactory(new Dictionary<string, string>
        {
            ["ReverseProxy:KnownProxies:0"] = ProxyAddress,
        });
        using var admin = await TestAuth.CreateAdminClientAsync(factory);

        // HTTPで直接つないだ端末がヘッダを偽装した想定
        var response = await LoginViaProxyAsync(factory, remoteAddress: "10.0.0.99");

        Assert.Equal(StatusCodes.Status200OK, response.Response.StatusCode);
        Assert.DoesNotContain(response.Response.Headers.SetCookie,
            v => v!.Contains("secure", StringComparison.OrdinalIgnoreCase));
        var addresses = await LoginAuditAddressesAsync(admin);
        Assert.DoesNotContain(ClientAddress, addresses);
        Assert.Contains("10.0.0.99", addresses);
    }

    [Fact]
    public async Task 信頼するプロキシが未設定なら転送ヘッダを受け入れない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);

        var response = await LoginViaProxyAsync(factory, remoteAddress: ProxyAddress);

        Assert.Equal(StatusCodes.Status200OK, response.Response.StatusCode);
        Assert.DoesNotContain(response.Response.Headers.SetCookie,
            v => v!.Contains("secure", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(ClientAddress, await LoginAuditAddressesAsync(admin));
    }

    /// <summary>
    /// プロキシ（remoteAddress）から、HTTPSで受けた ClientAddress のリクエストを転送された形でログインする。
    /// HttpClient経由では接続元IPを指定できないため、TestServerに直接HttpContextを組み立てて渡す
    /// </summary>
    [Fact]
    public async Task ロール変更で発行済みのアクセストークンは失効しリフレッシュで新しいロールになる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        using var worker = await TestAuth.CreateUserClientAsync(factory, admin, "worker1", "Passw0rd123", MesRoles.Operator);
        Assert.Equal(HttpStatusCode.OK, (await worker.GetAsync("/api/locations")).StatusCode);

        var id = await UserIdAsync(admin, "worker1");
        (await admin.PutAsJsonAsync($"/api/users/{id}",
            new UpdateUserRequest("worker1", [MesRoles.ProductionManager], IsActive: true))).EnsureSuccessStatusCode();

        // 古いロールのトークンは有効期限を待たずに使えなくなる
        Assert.Equal(HttpStatusCode.Unauthorized, (await worker.GetAsync("/api/locations")).StatusCode);

        // リフレッシュトークンは生きているので、取り直せば新しいロールで操作を続けられる
        var refresh = await worker.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var token = (await refresh.Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.Equal([MesRoles.ProductionManager], token.User.Roles);
        worker.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await worker.GetAsync("/api/locations")).StatusCode);

        // 表示名だけの更新ではトークンを失効させない
        (await admin.PutAsJsonAsync($"/api/users/{id}",
            new UpdateUserRequest("作業者1", [MesRoles.ProductionManager], IsActive: true))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await worker.GetAsync("/api/locations")).StatusCode);
    }

    [Fact]
    public async Task 無効化とパスワードリセットで発行済みのアクセストークンは即時に失効する()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        using var retired = await TestAuth.CreateUserClientAsync(factory, admin, "retired1", "Passw0rd123", MesRoles.Operator);
        using var reset = await TestAuth.CreateUserClientAsync(factory, admin, "reset1", "Passw0rd123", MesRoles.Operator);

        // 無効化：アクセストークンもリフレッシュも使えない
        (await admin.PutAsJsonAsync($"/api/users/{await UserIdAsync(admin, "retired1")}",
            new UpdateUserRequest("retired1", [MesRoles.Operator], IsActive: false))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await retired.GetAsync("/api/locations")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await retired.PostAsync("/api/auth/refresh", null)).StatusCode);

        // 管理者によるパスワードリセット
        (await admin.PostAsJsonAsync($"/api/users/{await UserIdAsync(admin, "reset1")}/reset-password",
            new ResetPasswordRequest("NewPassw0rd456"))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await reset.GetAsync("/api/locations")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await reset.PostAsync("/api/auth/refresh", null)).StatusCode);

        // 他のユーザー（管理者）のトークンには影響しない
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/locations")).StatusCode);
    }

    private static async Task<string> UserIdAsync(HttpClient admin, string userName) =>
        (await admin.GetFromJsonAsync<List<UserSummaryResponse>>("/api/users"))!.Single(u => u.UserName == userName).Id;

    private static Task<HttpContext> LoginViaProxyAsync(ApiFactory factory, string remoteAddress) =>
        factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/auth/login";
            context.Request.Headers["X-Forwarded-For"] = ClientAddress;
            context.Request.Headers["X-Forwarded-Proto"] = "https";
            context.Request.ContentType = "application/json";
            context.Request.Body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(
                new LoginRequest(TestAuth.AdminUser, TestAuth.AdminPassword), JsonSerializerOptions.Web));
        });

    private static async Task<List<string?>> LoginAuditAddressesAsync(HttpClient admin)
    {
        var page = await admin.GetFromJsonAsync<PagedResult<AuditLogResponse>>(
            "/api/audit-logs?category=Auth&action=Login&pageSize=200");
        return page!.Items.Select(a => a.IpAddress).ToList();
    }

    // ---- 連携API の APIキー（Spec.md 7.4 連携APIの認証。INT-01） ----

    private const string WhoAmI = "/api/integration/v1/whoami";

    private static Task<(string UserId, CreatedApiKeyResponse Created)> IssueKeyAsync(
        ApiFactory factory, HttpClient admin, string userName, params string[] roles) =>
        TestAuth.IssueApiKeyAsync(factory, admin, userName, roles);

    private static HttpClient KeyClient(ApiFactory factory, string key) => TestAuth.CreateApiKeyClient(factory, key);

    [Fact]
    public async Task APIキーで連携APIを呼ぶと紐づく利用者とロールとして扱われる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var (_, created) = await IssueKeyAsync(factory, admin, "erp-link", MesRoles.ProductionManager);
        Assert.StartsWith("pfm_", created.PlainKey);
        Assert.StartsWith(created.Key.Prefix, created.PlainKey);

        using var client = KeyClient(factory, created.PlainKey);
        var me = await client.GetFromJsonAsync<IntegrationWhoAmIResponse>(WhoAmI);
        Assert.Equal("erp-link", me!.UserName);
        Assert.Equal("ApiKey", me.AuthenticatedBy);
        Assert.Equal("販売管理からの取込", me.ApiKeyName);
        Assert.Equal([MesRoles.ProductionManager], me.Roles);

        // ログインのトークンでも連携APIを確かめられる
        var byLogin = await admin.GetFromJsonAsync<IntegrationWhoAmIResponse>(WhoAmI);
        Assert.Equal("Bearer", byLogin!.AuthenticatedBy);

        // 使われた日時が残る
        var keys = await admin.GetFromJsonAsync<List<ApiKeyResponse>>("/api/api-keys");
        Assert.NotNull(keys!.Single().LastUsedAt);
    }

    [Fact]
    public async Task APIキーでは画面用のAPIを呼べず_誤ったキーも通らない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var (_, created) = await IssueKeyAsync(factory, admin, "erp-link", MesRoles.SystemAdmin);

        using var client = KeyClient(factory, created.PlainKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/api-keys")).StatusCode);

        using var wrong = KeyClient(factory, created.PlainKey + "x");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync(WhoAmI)).StatusCode);
        using var none = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await none.GetAsync(WhoAmI)).StatusCode);
    }

    [Fact]
    public async Task APIキーは失効_期限切れ_利用者の無効化で即座に使えなくなり_ロールの変更も即座に効く()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var (userId, created) = await IssueKeyAsync(factory, admin, "erp-link", MesRoles.ProductionManager);
        using var client = KeyClient(factory, created.PlainKey);

        // ロールの変更は次の要求から効く
        (await admin.PutAsJsonAsync($"/api/users/{userId}",
            new UpdateUserRequest("erp-link", [MesRoles.Logistics], true))).EnsureSuccessStatusCode();
        Assert.Equal([MesRoles.Logistics], (await client.GetFromJsonAsync<IntegrationWhoAmIResponse>(WhoAmI))!.Roles);

        // 利用者を無効化すると使えない。有効に戻せばまた使える
        (await admin.PutAsJsonAsync($"/api/users/{userId}",
            new UpdateUserRequest("erp-link", [MesRoles.Logistics], false))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(WhoAmI)).StatusCode);
        (await admin.PutAsJsonAsync($"/api/users/{userId}",
            new UpdateUserRequest("erp-link", [MesRoles.Logistics], true))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(WhoAmI)).StatusCode);

        // 期限切れ（発行時は未来しか指定できないので、DBで過去にする）
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesAppDbContext>();
            var key = await db.ApiKeys.SingleAsync();
            key.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(WhoAmI)).StatusCode);

        // 失効は戻せない
        var (_, second) = await IssueKeyAsync(factory, admin, "erp-link2", MesRoles.ProductionManager);
        using var secondClient = KeyClient(factory, second.PlainKey);
        Assert.Equal(HttpStatusCode.OK, (await secondClient.GetAsync(WhoAmI)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsync($"/api/api-keys/{second.Key.Id}/revoke", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await secondClient.GetAsync(WhoAmI)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsync($"/api/api-keys/{second.Key.Id}/revoke", null)).StatusCode);
    }

    [Fact]
    public async Task APIキーの平文は発行の応答にしか出ず_管理はシステム管理者だけ()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var (userId, created) = await IssueKeyAsync(factory, admin, "erp-link", MesRoles.ProductionManager);
        (await admin.PostAsync($"/api/api-keys/{created.Key.Id}/revoke", null)).EnsureSuccessStatusCode();

        var list = await admin.GetStringAsync("/api/api-keys");
        Assert.DoesNotContain(created.PlainKey, list);
        var audits = await admin.GetStringAsync("/api/audit-logs?category=ApiKey&pageSize=200");
        Assert.DoesNotContain(created.PlainKey, audits);
        Assert.DoesNotContain(created.PlainKey[12..], audits);
        Assert.Contains("Create", audits);
        Assert.Contains("Revoke", audits);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesAppDbContext>();
            Assert.DoesNotContain(await db.ApiKeys.Select(k => k.KeyHash).ToListAsync(), h => h == created.PlainKey);
        }

        // 無効な利用者には発行できない・存在しない利用者は404
        (await admin.PutAsJsonAsync($"/api/users/{userId}",
            new UpdateUserRequest("erp-link", [MesRoles.ProductionManager], false))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync("/api/api-keys", new CreateApiKeyRequest("x", userId, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PostAsJsonAsync("/api/api-keys", new CreateApiKeyRequest("x", "no-such-user", null))).StatusCode);

        using var manager = await TestAuth.CreateUserClientAsync(
            factory, admin, "manager", "Passw0rd123", MesRoles.ProductionManager);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/api-keys")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.PostAsJsonAsync("/api/api-keys", new CreateApiKeyRequest("x", userId, null))).StatusCode);
    }

    // ---- 連携API の OpenAPI 文書（Spec.md 3.8 連携API。INT-04） ----

    [Fact]
    public async Task 連携APIのOpenAPI文書は開発環境でなくても認証なしで取れ_連携APIだけが載る()
    {
        // ApiFactory は Testing 環境（開発環境ではない）
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/integration-v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        var paths = root.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("/api/integration/v1/whoami", paths);
        Assert.Contains("/api/integration/v1/csv-kinds", paths);
        Assert.Contains("/api/integration/v1/actuals/{kind}", paths);
        Assert.Contains("/api/integration/v1/production-results", paths);
        Assert.Contains("/api/integration/v1/production-result-corrections", paths);
        // 画面用の API は外部との約束にしないので載せない
        Assert.All(paths, p => Assert.StartsWith("/api/integration/v1/", p));

        // APIキーの送り方が文書に載る
        var scheme = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("ApiKey");
        Assert.Equal("apiKey", scheme.GetProperty("type").GetString());
        Assert.Equal("header", scheme.GetProperty("in").GetString());
        Assert.Equal("X-Api-Key", scheme.GetProperty("name").GetString());

        // 説明は XML コメントから載る
        var feed = root.GetProperty("paths").GetProperty("/api/integration/v1/production-results").GetProperty("get");
        Assert.Contains("afterId", feed.GetProperty("summary").GetString());

        // 画面用の API を含む既定の文書は開発環境だけ
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
    }
}
