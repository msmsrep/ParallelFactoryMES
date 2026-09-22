using Bunit;
using MesApp.Client.Web.Auth;
using MesApp.Client.Web.Layout;
using MesApp.Core.Contracts.Auth;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace MesApp.Client.Web.Tests;

/// <summary>
/// MainLayoutの横断的な振る舞い（初期パスワード変更への誘導・画面エラーの受け止め）。
/// どちらも全画面に効くため、画面ごとではなくレイアウトで確認する。
/// </summary>
public class LayoutTests : BunitContext
{
    public LayoutTests()
    {
        // 全画面が文言の訳（L）を注入するため登録しておく（Spec.md 7.9）
        Services.AddLocalization();

        var tokenStore = new TokenStore();
        Services.AddSingleton(tokenStore);

        var stateProvider = new ApiAuthenticationStateProvider(tokenStore);
        Services.AddSingleton(stateProvider);

        // MainLayoutはログアウトでしかAuthServiceを使わないため、通信しないHttpClientで足りる
        Services.AddSingleton(new AuthService(
            new HttpClient(new NeverCalledHandler()) { BaseAddress = new Uri("http://localhost/") },
            tokenStore, stateProvider));

        // ヘッダーの<AuthorizeView>を描画するために認証済みにしておく（本題は認可ではない）
        AddAuthorization().SetAuthorized("作業者1");
    }

    private TokenStore Tokens => Services.GetRequiredService<TokenStore>();
    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    [Fact]
    public void 初期パスワードのままならパスワード変更画面へ移動する()
    {
        Tokens.Set("token", NewUser(mustChangePassword: true));

        Render<MainLayout>(p => p.Add(l => l.Body, BuildContent("本文")));

        Assert.EndsWith("/change-password", Navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void パスワード変更済みなら移動しない()
    {
        Tokens.Set("token", NewUser(mustChangePassword: false));
        var before = Navigation.Uri;

        var component = Render<MainLayout>(p => p.Add(l => l.Body, BuildContent("本文")));

        Assert.Equal(before, Navigation.Uri);
        Assert.Contains("本文", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void パスワード変更画面にいるときは移動を繰り返さない()
    {
        Tokens.Set("token", NewUser(mustChangePassword: true));
        Navigation.NavigateTo("change-password");

        Render<MainLayout>(p => p.Add(l => l.Body, BuildContent("本文")));

        Assert.EndsWith("/change-password", Navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void 画面の読み込みが失敗してもレイアウトはエラー表示に切り替わる()
    {
        Tokens.Set("token", NewUser(mustChangePassword: false));

        var component = Render<MainLayout>(p => p.Add(l => l.Body, builder =>
        {
            builder.OpenComponent<ThrowingComponent>(0);
            builder.CloseComponent();
        }));

        Assert.Contains("画面の読み込みに失敗しました", component.Markup, StringComparison.Ordinal);
        Assert.Contains("再試行", component.Markup, StringComparison.Ordinal);
    }

    private static UserInfo NewUser(bool mustChangePassword) =>
        new("id-1", "worker1", "作業者1", ["Operator"], mustChangePassword);

    private static RenderFragment BuildContent(string text) => builder => builder.AddContent(0, text);

    /// <summary>画面の読み込み失敗（GETの例外）を模したコンポーネント</summary>
    private sealed class ThrowingComponent : ComponentBase
    {
        protected override void OnInitialized() =>
            throw new HttpRequestException("読み込みに失敗しました");
    }

    /// <summary>呼ばれない想定のHTTPハンドラ（呼ばれたらテストを落とす）</summary>
    private sealed class NeverCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("このテストではAPIを呼ばない想定です。");
    }
}
