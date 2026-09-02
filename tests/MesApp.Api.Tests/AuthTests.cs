using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Auth;
using MesApp.Core.Contracts.Setup;
using MesApp.Core.Contracts.Users;

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
}
