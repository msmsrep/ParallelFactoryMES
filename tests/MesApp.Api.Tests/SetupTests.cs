using System.Net;
using System.Net.Http.Json;
using MesApp.Core.Contracts.Auth;
using MesApp.Core.Contracts.Setup;

namespace MesApp.Api.Tests;

public class SetupTests
{
    [Fact]
    public async Task 初期状態ではセットアップが必要と返る()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<SetupStatusResponse>("/api/setup/status");

        Assert.NotNull(status);
        Assert.True(status.SetupRequired);
    }

    [Fact]
    public async Task 初期管理者を作成でき_二回目は409になる()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var request = new InitializeRequest("admin", "Passw0rd123", "管理者");
        var first = await client.PostAsJsonAsync("/api/setup/initialize", request);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var status = await client.GetFromJsonAsync<SetupStatusResponse>("/api/setup/status");
        Assert.False(status!.SetupRequired);

        var second = await client.PostAsJsonAsync("/api/setup/initialize", request);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task 弱いパスワードでは初期管理者を作成できない()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/setup/initialize", new InitializeRequest("admin", "abc", "管理者"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task パスワードの指定がなければ初期管理者を作らず初期セットアップ画面に任せる(bool legacyGenerate)
    {
        // 誰も値を知らないアカウントを残さないための規約（Spec.md 2.2 E）。
        // 廃止した自動生成の設定（MesAdmin:GeneratePassword）が残っていても作らない（改訂164）
        var settings = new Dictionary<string, string> { ["MesAdmin:UserName"] = "admin" };
        if (legacyGenerate)
        {
            settings["MesAdmin:GeneratePassword"] = "true";
        }
        using var factory = new ApiFactory(settings);
        using var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<SetupStatusResponse>("/api/setup/status");
        Assert.True(status!.SetupRequired);
    }

    [Fact]
    public async Task 設定したパスワードでシードした初期管理者はパスワード変更まで業務APIを使えない()
    {
        using var factory = new ApiFactory(new Dictionary<string, string>
        {
            ["MesAdmin:UserName"] = "admin",
            ["MesAdmin:Password"] = "Passw0rd123",
        });
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", "Passw0rd123"));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.True(token.User.MustChangePassword);

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/locations")).StatusCode);
    }
}
