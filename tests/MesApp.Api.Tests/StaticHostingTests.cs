using System.Net;

namespace MesApp.Api.Tests;

/// <summary>Blazor WASMクライアントの静的配信（Spec.md 2.1：APIがWebクライアントの配信も担う）</summary>
public class StaticHostingTests
{
    [Fact]
    public async Task ルートでindexhtmlが配信される()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Parallel Factory MES", html);
        Assert.Contains("blazor.webassembly.js", html);
    }

    [Fact]
    public async Task クライアント側ルートへの直接アクセスはindexhtmlへフォールバックする()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/manufacturing-orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Blazorフレームワークファイルが配信される()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/_framework/blazor.webassembly.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
