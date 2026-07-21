using System.Net;
using System.Net.Http.Json;
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
}
