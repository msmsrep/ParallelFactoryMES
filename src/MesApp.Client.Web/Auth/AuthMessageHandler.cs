using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components;

namespace MesApp.Client.Web.Auth;

/// <summary>
/// APIリクエストにBearerトークンを付与し、401時はサイレントリフレッシュして1回だけ再試行する。
/// 状態変更APIは常にAuthorizationヘッダのJWT必須のためCSRF安全（Spec.md 7.4）。
/// </summary>
/// <remarks>
/// リフレッシュにも失敗した場合はセッションを破棄してログイン画面へ戻す。
/// そのままにすると呼び出し元で401の例外になり、どの画面もエラー表示のまま操作できなくなる。
/// </remarks>
public class AuthMessageHandler(
    TokenStore tokenStore, AuthService authService, NavigationManager navigation) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (tokenStore.AccessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenStore.AccessToken);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        // アクセストークン失効の可能性 → リフレッシュして1回だけ再試行
        if (!await authService.TryRefreshAsync())
        {
            authService.EndSession();
            navigation.NavigateTo("login", replace: true);
            return response;
        }
        response.Dispose();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenStore.AccessToken);
        return await base.SendAsync(request, cancellationToken);
    }
}
