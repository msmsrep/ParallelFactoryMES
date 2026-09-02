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

    [Fact]
    public async Task 未定義のAPIパスにindexhtmlを返さない()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        // 打ち間違い・未実装のAPIパスがindex.htmlの200になると、
        // 呼び出し側は404ではなくJSONパース失敗という無関係なエラーを受け取る
        var unknown = await client.GetAsync("/api/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.NotEqual("text/html", unknown.Content.Headers.ContentType?.MediaType);

        // GETを持たないパス（受入は登録・取消のみ）もフォールバックに吸われない
        var noGet = await client.GetAsync("/api/receiving");
        Assert.Equal(HttpStatusCode.NotFound, noGet.StatusCode);
    }
}
