using Bunit;
using MesApp.Client.Web;
using MesApp.Client.Web.Auth;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Auth;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace MesApp.Client.Web.Tests;

/// <summary>
/// 権限が足りない画面を開いたときの振る舞い（App.razorの<c>NotAuthorized</c>）。
/// ルーティング全体に効くため、画面ごとではなくここで確認する（Spec.md 6.1）。
/// </summary>
public class AuthorizationTests : BunitContext
{
    public AuthorizationTests()
    {
        // 全画面が文言の訳（L）を注入するため登録しておく（Spec.md 7.9）
        Services.AddLocalization();

        var tokenStore = new TokenStore();
        Services.AddSingleton(tokenStore);

        var stateProvider = new ApiAuthenticationStateProvider(tokenStore);
        Services.AddSingleton(stateProvider);
        Services.AddSingleton(new AuthService(
            new HttpClient(new NeverCalledHandler()) { BaseAddress = new Uri("http://localhost/") },
            tokenStore, stateProvider));

        tokenStore.Set("token", new UserInfo(
            "id-1", "worker1", "作業者1", [MesRoles.Operator], MustChangePassword: false));
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    [Fact]
    public void ログイン済みで権限が足りない画面はログインへ戻さず理由を表示する()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("作業者1");
        auth.SetRoles(MesRoles.Operator);
        Navigation.NavigateTo("audit-logs");

        var component = Render<App>();

        Assert.Contains("権限がありません", component.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("/login", Navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void 未認証ならログイン画面へ移動する()
    {
        AddAuthorization().SetNotAuthorized();
        Navigation.NavigateTo("audit-logs");

        Render<App>();

        Assert.EndsWith("/login", Navigation.Uri, StringComparison.Ordinal);
    }

    /// <summary>呼ばれない想定のHTTPハンドラ（呼ばれたらテストを落とす）</summary>
    private sealed class NeverCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("このテストではAPIを呼ばない想定です。");
    }
}
