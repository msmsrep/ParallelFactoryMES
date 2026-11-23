using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Auth;
using MesApp.Core.Contracts.Common;
using MesApp.Core.Contracts.Integration;
using MesApp.Core.Contracts.Setup;
using MesApp.Core.Contracts.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>
    /// 管理者クライアントで別ロールのユーザーを作成し、そのユーザーの認証済みクライアントを返す。
    /// 作成直後は MustChangePassword が立っていて業務APIを呼べないため、
    /// 初回パスワード変更まで済ませた状態にしてから返す。
    /// </summary>
    public static async Task<HttpClient> CreateUserClientAsync(
        ApiFactory factory, HttpClient adminClient, string userName, string password, params string[] roles)
    {
        var created = await adminClient.PostAsJsonAsync(
            "/api/users", new CreateUserRequest(userName, password, userName, [.. roles]));
        created.EnsureSuccessStatusCode();

        var client = factory.CreateClient();
        await LoginAsync(client, userName, password);

        // 同じパスワードへの変更でも初回変更の要求は解ける（テストの呼び出し側がパスワードを持ち回れるようにする）
        var changed = await client.PostAsJsonAsync(
            "/api/auth/change-password", new ChangePasswordRequest(password, password));
        changed.EnsureSuccessStatusCode();
        await LoginAsync(client, userName, password);
        return client;
    }

    /// <summary>
    /// 連携API 用に、指定ロールの利用者を作って APIキーを発行する（Spec.md 7.4 連携APIの認証）。
    /// 利用者のIDと発行の結果（平文のキーを含む）を返す
    /// </summary>
    public static async Task<(string UserId, CreatedApiKeyResponse Created)> IssueApiKeyAsync(
        ApiFactory factory, HttpClient adminClient, string userName, params string[] roles)
    {
        using var _ = await CreateUserClientAsync(factory, adminClient, userName, "Passw0rd123", roles);
        var options = await adminClient.GetFromJsonAsync<OptionsResult<UserOptionResponse>>($"/api/users/options?keyword={userName}");
        var userId = options!.Items.Single(u => u.UserName == userName).Id;
        var response = await adminClient.PostAsJsonAsync("/api/api-keys", new CreateApiKeyRequest("販売管理からの取込", userId, null));
        response.EnsureSuccessStatusCode();
        return (userId, (await response.Content.ReadFromJsonAsync<CreatedApiKeyResponse>())!);
    }

    /// <summary>積まれた Webhook の通知（種類・要約・data）</summary>
    public sealed record QueuedWebhook(string Type, string Summary, JsonElement Data);

    /// <summary>
    /// Webhook の送信先を登録する（Spec.md 3.8 Webhook）。種類を省くとすべての種類を購読する。
    /// 業務のテストで「この操作で通知が積まれるか」を確かめるため。送り先には実際には送らない（常駐処理は止めてある）
    /// </summary>
    public static async Task SubscribeWebhooksAsync(HttpClient adminClient, params string[] eventTypes)
    {
        var response = await adminClient.PostAsJsonAsync("/api/webhook-subscriptions", new SaveWebhookSubscriptionRequest(
            "テストの送信先", "http://localhost/webhook", [.. eventTypes.Length == 0 ? WebhookEventTypes.All : eventTypes], true));
        response.EnsureSuccessStatusCode();
    }

    /// <summary>積まれた Webhook の通知（積んだ順）</summary>
    public static async Task<List<QueuedWebhook>> QueuedWebhooksAsync(ApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesApp.Infrastructure.MesAppDbContext>();
        var rows = await db.WebhookOutbox.AsNoTracking().OrderBy(o => o.Id).ToListAsync();
        return [.. rows.Select(o => new QueuedWebhook(o.EventType, o.Summary, JsonDocument.Parse(o.Payload).RootElement.Clone()))];
    }

    /// <summary>APIキーを X-Api-Key ヘッダに付けたクライアント</summary>
    public static HttpClient CreateApiKeyClient(ApiFactory factory, string key)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }
}
