using System.Collections;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Resources;
using System.Text.Json;
using System.Text.RegularExpressions;
using MesApp.Api.Localization;
using MesApp.Api.Services.Webhooks;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Audit;
using MesApp.Core.Contracts.Auth;
using MesApp.Core.Contracts.Common;
using MesApp.Core.Contracts.Integration;
using MesApp.Core.Contracts.Setup;
using MesApp.Core.Contracts.Users;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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

    // ---- Webhook の送信先と送信待ち（Spec.md 3.8 Webhook。WHK-01） ----

    private const string WebhookSubscriptions = "/api/webhook-subscriptions";

    private static async Task<CreatedWebhookSecretResponse> CreateWebhookAsync(
        HttpClient admin, string name, bool isActive = true, params string[] eventTypes)
    {
        var response = await admin.PostAsJsonAsync(WebhookSubscriptions,
            new SaveWebhookSubscriptionRequest(name, "http://192.168.0.10:1880/mes", [.. eventTypes], isActive));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedWebhookSecretResponse>())!;
    }

    [Fact]
    public async Task Webhookの秘密の値は登録と再発行の応答にしか出ず_管理はシステム管理者だけ()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var created = await CreateWebhookAsync(admin, "班長チャット", true,
            WebhookEventTypes.NonconformanceCreated, WebhookEventTypes.LotStatusChanged);
        Assert.StartsWith(WebhookSecrets.Prefix, created.PlainSecret);
        Assert.StartsWith(created.Subscription.SecretPrefix, created.PlainSecret);
        // 保存は画面の一覧の順（WebhookEventTypes.All の順）
        Assert.Equal([WebhookEventTypes.LotStatusChanged, WebhookEventTypes.NonconformanceCreated], created.Subscription.EventTypes);

        var rotated = await (await admin.PostAsync($"{WebhookSubscriptions}/{created.Subscription.Id}/rotate-secret", null))
            .Content.ReadFromJsonAsync<CreatedWebhookSecretResponse>();
        Assert.NotEqual(created.PlainSecret, rotated!.PlainSecret);
        Assert.NotNull(rotated.Subscription.SecretRotatedAt);

        // 一覧・監査ログ・DB に平文が出ない。DB の値は復号すると今の平文に戻る（署名に使うため）
        var list = await admin.GetStringAsync(WebhookSubscriptions);
        var audits = await admin.GetStringAsync("/api/audit-logs?category=Webhook&pageSize=200");
        foreach (var plain in new[] { created.PlainSecret, rotated.PlainSecret })
        {
            Assert.DoesNotContain(plain, list);
            Assert.DoesNotContain(plain, audits);
        }
        Assert.Contains("Create", audits);
        Assert.Contains("RotateSecret", audits);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesAppDbContext>();
            var stored = await db.WebhookSubscriptions.SingleAsync();
            Assert.DoesNotContain(WebhookSecrets.Prefix, stored.SecretProtected);
            Assert.Equal(rotated.PlainSecret,
                scope.ServiceProvider.GetRequiredService<WebhookSecrets>().Unprotect(stored.SecretProtected));
        }

        using var manager = await TestAuth.CreateUserClientAsync(
            factory, admin, "manager", "Passw0rd123", MesRoles.ProductionManager);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync(WebhookSubscriptions)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync(WebhookSubscriptions,
            new SaveWebhookSubscriptionRequest("x", "http://localhost/x", [WebhookEventTypes.LotStatusChanged], true))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.DeleteAsync($"{WebhookSubscriptions}/{created.Subscription.Id}")).StatusCode);
    }

    [Theory]
    [InlineData("", "http://localhost/x", WebhookEventTypes.LotStatusChanged)]
    [InlineData("名前", "ftp://localhost/x", WebhookEventTypes.LotStatusChanged)]
    [InlineData("名前", "localhost/x", WebhookEventTypes.LotStatusChanged)]
    [InlineData("名前", "http://user:pass@localhost/x", WebhookEventTypes.LotStatusChanged)]
    [InlineData("名前", "http://localhost/x", "lot.no-such-event")]
    [InlineData("名前", "http://localhost/x", null)]
    public async Task Webhookの送信先の入力不正は400(string name, string url, string? eventType)
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var response = await admin.PostAsJsonAsync(WebhookSubscriptions,
            new SaveWebhookSubscriptionRequest(name, url, eventType is null ? [] : [eventType], true));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"{WebhookSubscriptions}/999",
            new SaveWebhookSubscriptionRequest("名前", "http://localhost/x", [WebhookEventTypes.LotStatusChanged], true))).StatusCode);
    }

    [Fact]
    public async Task Webhookの送信待ちは購読している有効な送信先にだけ作られ_業務と一緒に巻き戻る()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var lot = await CreateWebhookAsync(admin, "ロット", true, WebhookEventTypes.LotStatusChanged);
        var both = await CreateWebhookAsync(admin, "両方", true,
            WebhookEventTypes.LotStatusChanged, WebhookEventTypes.EquipmentStopped);
        await CreateWebhookAsync(admin, "無効", false, WebhookEventTypes.LotStatusChanged);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesAppDbContext>();
            var publisher = scope.ServiceProvider.GetRequiredService<WebhookPublisher>();
            Assert.NotNull(await publisher.EnqueueAsync(WebhookEventTypes.LotStatusChanged, "ロット L-1 を保留にしました",
                new { lotNo = "L-1", status = Core.Entities.LotStockStatus.OnHold }, CancellationToken.None));
            Assert.NotNull(await publisher.EnqueueAsync(WebhookEventTypes.EquipmentStopped, "設備 EQ-01 が停止しました",
                new { equipmentCode = "EQ-01" }, CancellationToken.None));
            // 購読している送信先が無い種類は積まない
            Assert.Null(await publisher.EnqueueAsync(WebhookEventTypes.NonconformanceCreated, "不適合",
                new { }, CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                publisher.EnqueueAsync("no-such.event", "x", new { }, CancellationToken.None));
            await db.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesAppDbContext>();
            var outbox = await db.WebhookOutbox.AsNoTracking().Include(o => o.Deliveries).OrderBy(o => o.Id).ToListAsync();
            Assert.Equal([WebhookEventTypes.LotStatusChanged, WebhookEventTypes.EquipmentStopped], outbox.Select(o => o.EventType));
            Assert.Equal([lot.Subscription.Id, both.Subscription.Id],
                outbox[0].Deliveries.Select(d => d.SubscriptionId).Order());
            Assert.Equal([both.Subscription.Id], outbox[1].Deliveries.Select(d => d.SubscriptionId));
            Assert.All(outbox.SelectMany(o => o.Deliveries), d => Assert.Equal(Core.Entities.WebhookDeliveryState.Pending, d.State));
            // 区分は列挙の名前、項目名は camelCase
            Assert.Equal("""{"lotNo":"L-1","status":"OnHold"}""", outbox[0].Payload);
            Assert.True(outbox[1].Id > outbox[0].Id);
        }

        // 業務のトランザクションが巻き戻れば、送信待ちも残らない
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesAppDbContext>();
            var publisher = scope.ServiceProvider.GetRequiredService<WebhookPublisher>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await publisher.EnqueueAsync(WebhookEventTypes.LotStatusChanged, "巻き戻す", new { }, CancellationToken.None);
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesAppDbContext>();
            Assert.Equal(2, await db.WebhookOutbox.CountAsync());
        }

        // 送信先を消すと、その送信先の送信待ちも消える（他の送信先の分は残る）
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{WebhookSubscriptions}/{both.Subscription.Id}")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesAppDbContext>();
            Assert.Equal([lot.Subscription.Id], await db.WebhookDeliveries.Select(d => d.SubscriptionId).ToListAsync());
        }
        Assert.Contains("Delete", await admin.GetStringAsync("/api/audit-logs?category=Webhook&pageSize=200"));
    }

    // ---- Webhook の送信・署名・再送（Spec.md 3.8 Webhook。WHK-02） ----

    /// <summary>テストの中に立てる受け側。実ネットワークには出ない。応答は積んだ順に返し、尽きたら200</summary>
    private sealed class WebhookReceiver : HttpMessageHandler
    {
        public sealed record Received(Dictionary<string, string> Headers, string Body);

        private readonly object _gate = new();
        private readonly List<Received> _received = [];
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();

        public List<Received> Requests { get { lock (_gate) { return [.. _received]; } } }

        public void Reply(params HttpStatusCode[] codes)
        {
            lock (_gate)
            {
                foreach (var code in codes)
                {
                    _responses.Enqueue(() => new HttpResponseMessage(code) { Content = new StringContent("受け側の応答") });
                }
            }
        }

        public void ThrowOnce()
        {
            lock (_gate)
            {
                _responses.Enqueue(() => throw new HttpRequestException("接続できませんでした（テスト）"));
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            Func<HttpResponseMessage>? next;
            lock (_gate)
            {
                _received.Add(new Received(
                    request.Headers.Concat(request.Content.Headers).ToDictionary(h => h.Key, h => string.Join(",", h.Value)),
                    body));
                _responses.TryDequeue(out next);
            }
            return next?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static WebApplicationFactory<Program> WithReceiver(ApiFactory factory, WebhookReceiver receiver) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.AddHttpClient(WebhookDispatcher.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => receiver)));

    private static async Task<HttpClient> AdminOfAsync(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        (await client.PostAsJsonAsync("/api/setup/initialize",
            new InitializeRequest(TestAuth.AdminUser, TestAuth.AdminPassword, "管理者"))).EnsureSuccessStatusCode();
        await TestAuth.LoginAsync(client, TestAuth.AdminUser, TestAuth.AdminPassword);
        return client;
    }

    private static async Task EnqueueAsync(WebApplicationFactory<Program> app, string lotNo)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesAppDbContext>();
        await scope.ServiceProvider.GetRequiredService<WebhookPublisher>().EnqueueAsync(
            WebhookEventTypes.LotStatusChanged, $"ロット {lotNo} を保留にしました", new { lotNo }, CancellationToken.None);
        await db.SaveChangesAsync();
    }

    private static Task<int> DispatchAsync(WebApplicationFactory<Program> app) =>
        app.Services.GetRequiredService<WebhookDispatcher>().DispatchDueAsync(CancellationToken.None);

    private static async Task<List<Core.Entities.WebhookDelivery>> DeliveriesAsync(WebApplicationFactory<Program> app)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MesAppDbContext>().WebhookDeliveries.AsNoTracking()
            .OrderBy(d => d.Id).ToListAsync();
    }

    /// <summary>再送の時刻を待たずに送れるよう、送ってよい時刻を今にする</summary>
    private static async Task MakeDueAsync(WebApplicationFactory<Program> app)
    {
        using var scope = app.Services.CreateScope();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await scope.ServiceProvider.GetRequiredService<MesAppDbContext>().WebhookDeliveries
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.NextAttemptAtUnixSeconds, now));
    }

    [Fact]
    public async Task Webhookは署名を付けて送り_受け側は秘密の値で検算できる()
    {
        using var factory = new ApiFactory();
        var receiver = new WebhookReceiver();
        using var app = WithReceiver(factory, receiver);
        using var admin = await AdminOfAsync(app);
        var created = await CreateWebhookAsync(admin, "ロット", true, WebhookEventTypes.LotStatusChanged);
        await EnqueueAsync(app, "L-1");

        Assert.Equal(1, await DispatchAsync(app));
        // 送り終えたものは二度と送らない
        Assert.Equal(0, await DispatchAsync(app));

        var request = Assert.Single(receiver.Requests);
        var timestamp = long.Parse(request.Headers[WebhookSignature.TimestampHeader], CultureInfo.InvariantCulture);
        Assert.InRange(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - timestamp, 0, 60);
        Assert.Equal(WebhookSignature.Compute(created.PlainSecret, timestamp, request.Body),
            request.Headers[WebhookSignature.SignatureHeader]);
        // 署名の形は受け側との約束（sha256= と小文字の16進64桁）
        Assert.Matches("^sha256=[0-9a-f]{64}$", request.Headers[WebhookSignature.SignatureHeader]);
        Assert.Equal(WebhookEventTypes.LotStatusChanged, request.Headers[WebhookSignature.EventHeader]);
        Assert.StartsWith("application/json", request.Headers["Content-Type"]);

        var delivery = Assert.Single(await DeliveriesAsync(app));
        Assert.Equal(delivery.Id.ToString(CultureInfo.InvariantCulture), request.Headers[WebhookSignature.DeliveryHeader]);
        Assert.Equal(Core.Entities.WebhookDeliveryState.Delivered, delivery.State);
        Assert.Equal(1, delivery.Attempts);
        Assert.Equal(200, delivery.LastStatusCode);
        Assert.NotNull(delivery.DeliveredAt);

        using var body = JsonDocument.Parse(request.Body);
        var root = body.RootElement;
        using (var scope = app.Services.CreateScope())
        {
            var outbox = await scope.ServiceProvider.GetRequiredService<MesAppDbContext>().WebhookOutbox.SingleAsync();
            Assert.Equal(outbox.EventId, root.GetProperty("id").GetGuid());
            Assert.Equal(outbox.Id, root.GetProperty("sequence").GetInt64());
        }
        Assert.Equal(WebhookEventTypes.LotStatusChanged, root.GetProperty("type").GetString());
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal("ロット L-1 を保留にしました", root.GetProperty("summary").GetString());
        Assert.Equal("L-1", root.GetProperty("data").GetProperty("lotNo").GetString());
        root.GetProperty("occurredAt").GetDateTimeOffset();
        // 1文字でも変えると署名が合わない
        Assert.NotEqual(WebhookSignature.Compute(created.PlainSecret, timestamp, request.Body + " "),
            request.Headers[WebhookSignature.SignatureHeader]);
    }

    [Fact]
    public async Task Webhookは5xxと接続失敗では間隔を空けて再送し_4xxでは止め_回数が尽きたら止まり_手で再送できる()
    {
        using var factory = new ApiFactory(new Dictionary<string, string> { ["Webhooks:MaxAttempts"] = "3" });
        var receiver = new WebhookReceiver();
        using var app = WithReceiver(factory, receiver);
        using var admin = await AdminOfAsync(app);
        var created = await CreateWebhookAsync(admin, "ロット", true, WebhookEventTypes.LotStatusChanged);

        // 500 → 次の再送まで待つ（すぐには送らない）→ 時刻が来たら送り、届く
        receiver.Reply(HttpStatusCode.InternalServerError);
        await EnqueueAsync(app, "L-1");
        Assert.Equal(1, await DispatchAsync(app));
        var first = Assert.Single(await DeliveriesAsync(app));
        Assert.Equal(Core.Entities.WebhookDeliveryState.Pending, first.State);
        Assert.Equal(1, first.Attempts);
        Assert.Equal(500, first.LastStatusCode);
        Assert.Contains("受け側の応答", first.LastError);
        Assert.True(first.NextAttemptAtUnixSeconds >= DateTimeOffset.UtcNow.AddSeconds(50).ToUnixTimeSeconds());
        Assert.Equal(0, await DispatchAsync(app));
        var listed = await admin.GetFromJsonAsync<List<WebhookSubscriptionResponse>>(WebhookSubscriptions);
        Assert.Equal((0, 1, 0), (listed![0].PendingCount, listed[0].RetryingCount, listed[0].FailedCount));

        await MakeDueAsync(app);
        Assert.Equal(1, await DispatchAsync(app));
        first = Assert.Single(await DeliveriesAsync(app));
        Assert.Equal(Core.Entities.WebhookDeliveryState.Delivered, first.State);
        Assert.Equal(2, first.Attempts);
        Assert.Null(first.LastError);

        // 400 は送り直しても同じなので、1回で止める
        receiver.Reply(HttpStatusCode.BadRequest);
        await EnqueueAsync(app, "L-2");
        await DispatchAsync(app);
        var second = (await DeliveriesAsync(app))[1];
        Assert.Equal(Core.Entities.WebhookDeliveryState.Failed, second.State);
        Assert.Equal(1, second.Attempts);

        // 接続失敗・503 が続き、上限（3回）で止まる
        receiver.ThrowOnce();
        receiver.Reply(HttpStatusCode.ServiceUnavailable, HttpStatusCode.TooManyRequests);
        await EnqueueAsync(app, "L-3");
        for (var i = 0; i < 3; i++)
        {
            await MakeDueAsync(app);
            await DispatchAsync(app);
        }
        var third = (await DeliveriesAsync(app))[2];
        Assert.Equal(Core.Entities.WebhookDeliveryState.Failed, third.State);
        Assert.Equal(3, third.Attempts);
        Assert.Equal(429, third.LastStatusCode);
        listed = await admin.GetFromJsonAsync<List<WebhookSubscriptionResponse>>(WebhookSubscriptions);
        Assert.Equal(2, listed![0].FailedCount);

        // 記録の一覧（新しい順・状態で絞れる）と、手での再送
        var failed = await admin.GetFromJsonAsync<List<WebhookDeliveryResponse>>(
            $"/api/webhook-deliveries?subscriptionId={created.Subscription.Id}&state=Failed");
        Assert.Equal([third.Id, second.Id], failed!.Select(d => d.Id));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/webhook-deliveries/{first.Id}/resend", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/webhook-deliveries/99999/resend", null)).StatusCode);
        var resent = await (await admin.PostAsync($"/api/webhook-deliveries/{third.Id}/resend", null))
            .Content.ReadFromJsonAsync<WebhookDeliveryResponse>();
        Assert.Equal(Core.Entities.WebhookDeliveryState.Pending, resent!.State);
        Assert.Equal(0, resent.Attempts);
        Assert.Equal(1, await DispatchAsync(app));
        Assert.Equal(Core.Entities.WebhookDeliveryState.Delivered, (await DeliveriesAsync(app))[2].State);
        // 送り直しても通知の id は同じ（受け側は重複として捨てられる）
        var bodies = receiver.Requests.Select(r => JsonDocument.Parse(r.Body).RootElement.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(bodies[^2], bodies[^1]);
        Assert.Contains("Resend", await admin.GetStringAsync("/api/audit-logs?category=Webhook&pageSize=200"));

        using var manager = await TestAuth.CreateUserClientAsync(
            factory, admin, "manager", "Passw0rd123", MesRoles.ProductionManager);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/webhook-deliveries")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsync($"/api/webhook-deliveries/{second.Id}/resend", null)).StatusCode);
    }

    [Fact]
    public async Task 同じWebhookの送信待ちを複数の処理が同時に取り出しても送るのは1回()
    {
        using var factory = new ApiFactory();
        var receiver = new WebhookReceiver();
        using var app = WithReceiver(factory, receiver);
        using var admin = await AdminOfAsync(app);
        await CreateWebhookAsync(admin, "ロット", true, WebhookEventTypes.LotStatusChanged);
        for (var i = 0; i < 6; i++)
        {
            await EnqueueAsync(app, $"L-{i}");
        }

        var sent = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => DispatchAsync(app))));
        Assert.Equal(6, sent.Sum());
        Assert.Equal(6, receiver.Requests.Select(r => r.Headers[WebhookSignature.DeliveryHeader]).Distinct().Count());
        Assert.Equal(6, receiver.Requests.Count);
        Assert.All(await DeliveriesAsync(app), d => Assert.Equal(1, d.Attempts));
    }

    [Fact]
    public async Task Webhookのテスト送信はいま送って結果を返し_失敗しても再送しない()
    {
        using var factory = new ApiFactory();
        var receiver = new WebhookReceiver();
        using var app = WithReceiver(factory, receiver);
        using var admin = await AdminOfAsync(app);
        // 無効の送信先にも送れる（有効にする前に受け側を確かめるため）
        var created = await CreateWebhookAsync(admin, "まだ無効", false, WebhookEventTypes.LotStatusChanged);

        var ok = await (await admin.PostAsync($"{WebhookSubscriptions}/{created.Subscription.Id}/test", null))
            .Content.ReadFromJsonAsync<WebhookDeliveryResponse>();
        Assert.Equal(Core.Entities.WebhookDeliveryState.Delivered, ok!.State);
        Assert.Equal(WebhookDispatcher.PingEventType, ok.EventType);
        var request = Assert.Single(receiver.Requests);
        Assert.Equal(WebhookDispatcher.PingEventType, request.Headers[WebhookSignature.EventHeader]);

        receiver.Reply(HttpStatusCode.InternalServerError);
        var ng = await (await admin.PostAsync($"{WebhookSubscriptions}/{created.Subscription.Id}/test", null))
            .Content.ReadFromJsonAsync<WebhookDeliveryResponse>();
        Assert.Equal(Core.Entities.WebhookDeliveryState.Failed, ng!.State);
        Assert.Equal(500, ng.LastStatusCode);
        await MakeDueAsync(app);
        Assert.Equal(0, await DispatchAsync(app));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"{WebhookSubscriptions}/999/test", null)).StatusCode);
    }

    [Fact]
    public async Task Webhookの秘密の値を復号できない送信は再送せずに止め_再発行を促す()
    {
        using var factory = new ApiFactory();
        var receiver = new WebhookReceiver();
        using var app = WithReceiver(factory, receiver);
        using var admin = await AdminOfAsync(app);
        await CreateWebhookAsync(admin, "ロット", true, WebhookEventTypes.LotStatusChanged);
        await EnqueueAsync(app, "L-1");
        using (var scope = app.Services.CreateScope())
        {
            // DB だけを戻して Data Protection の鍵を失ったのと同じ状態
            await scope.ServiceProvider.GetRequiredService<MesAppDbContext>().WebhookSubscriptions
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.SecretProtected, "壊れた暗号文"));
        }

        Assert.Equal(1, await DispatchAsync(app));
        var delivery = Assert.Single(await DeliveriesAsync(app));
        Assert.Equal(Core.Entities.WebhookDeliveryState.Failed, delivery.State);
        Assert.Contains("再発行", delivery.LastError);
        Assert.Empty(receiver.Requests);
    }

    [Fact]
    public async Task Webhookの常駐処理は送信待ちを自分で送る()
    {
        using var factory = new ApiFactory(new Dictionary<string, string>
        {
            ["Webhooks:WorkerEnabled"] = "true",
            ["Webhooks:PollIntervalSeconds"] = "1",
        });
        var receiver = new WebhookReceiver();
        using var app = WithReceiver(factory, receiver);
        using var admin = await AdminOfAsync(app);
        await CreateWebhookAsync(admin, "ロット", true, WebhookEventTypes.LotStatusChanged);
        await EnqueueAsync(app, "L-1");

        for (var i = 0; i < 100 && receiver.Requests.Count == 0; i++)
        {
            await Task.Delay(100);
        }
        Assert.Single(receiver.Requests);
    }
}
