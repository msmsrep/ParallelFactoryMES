using System.Net.Http.Headers;
using System.Net.Http.Json;
using MesApp.Core.Contracts.Auth;
using MesApp.Core.Contracts.Setup;
using MesApp.Core.Contracts.Users;

namespace MesApp.Api.Tests;

/// <summary>テスト用の認証ヘルパー（初期管理者セットアップ〜ログイン〜Bearer設定）</summary>
public static class TestAuth
{
    public const string AdminUser = "admin";
    public const string AdminPassword = "Passw0rd123";

    /// <summary>初期管理者をセットアップし、認証済み（SystemAdmin）クライアントを返す</summary>
    public static async Task<HttpClient> CreateAdminClientAsync(ApiFactory factory)
    {
        var client = factory.CreateClient();
        var setup = await client.PostAsJsonAsync(
            "/api/setup/initialize", new InitializeRequest(AdminUser, AdminPassword, "管理者"));
        setup.EnsureSuccessStatusCode();
        await LoginAsync(client, AdminUser, AdminPassword);
        return client;
    }

    /// <summary>指定ユーザーでログインしてBearerトークンを設定する</summary>
    public static async Task LoginAsync(HttpClient client, string userName, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, password));
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
    }

    /// <summary>管理者クライアントで別ロールのユーザーを作成し、そのユーザーの認証済みクライアントを返す</summary>
    public static async Task<HttpClient> CreateUserClientAsync(
        ApiFactory factory, HttpClient adminClient, string userName, string password, params string[] roles)
    {
        var created = await adminClient.PostAsJsonAsync(
            "/api/users", new CreateUserRequest(userName, password, userName, [.. roles]));
        created.EnsureSuccessStatusCode();

        var client = factory.CreateClient();
        await LoginAsync(client, userName, password);
        return client;
    }
}
