using System.Net.Http.Json;
using MesApp.Client.Web.Shared;
using MesApp.Core.Contracts.Auth;

namespace MesApp.Client.Web.Auth;

/// <summary>
/// ログイン・ログアウト・サイレントリフレッシュ（Spec.md 2.2 C、7.4）。
/// リフレッシュトークンはHttpOnly Cookie（同一オリジンのため自動送信）で、
/// アクセストークンの取得・更新のみをここで行う。
/// </summary>
public class AuthService(HttpClient bareClient, TokenStore tokenStore, ApiAuthenticationStateProvider stateProvider)
{
    /// <summary>アプリ起動時のサイレントリフレッシュ（Cookieが残っていればセッションを復元する）</summary>
    public async Task InitializeAsync()
    {
        if (!tokenStore.IsAuthenticated)
        {
            await TryRefreshAsync();
        }
    }

    /// <summary>ログイン。失敗時はエラーメッセージを返す（成功時はnull）。
    /// <paramref name="fallbackError"/> は API が理由を返さなかったときの文言（表示言語に訳して呼び出し側が渡す。Spec.md 7.9）</summary>
    public async Task<string?> LoginAsync(string userName, string password, string fallbackError)
    {
        var response = await bareClient.PostAsJsonAsync("api/auth/login", new LoginRequest(userName, password));
        if (await response.ReadErrorAsync(fallbackError) is { } error)
        {
            return error;
        }
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        tokenStore.Set(token!.AccessToken, token.User);
        stateProvider.NotifyChanged();
        return null;
    }

    /// <summary>リフレッシュトークンCookieによるアクセストークンの更新。成功可否を返す</summary>
    public async Task<bool> TryRefreshAsync()
    {
        try
        {
            var response = await bareClient.PostAsync("api/auth/refresh", null);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
            tokenStore.Set(token!.AccessToken, token.User);
            stateProvider.NotifyChanged();
            return true;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    /// <summary>
    /// サーバーへ問い合わせずに手元のセッションだけ破棄する
    /// （リフレッシュに失敗した＝サーバー側は既に無効、という場面で使う）
    /// </summary>
    public void EndSession()
    {
        if (!tokenStore.IsAuthenticated)
        {
            return;
        }
        tokenStore.Clear();
        stateProvider.NotifyChanged();
    }

    public async Task LogoutAsync()
    {
        try
        {
            await bareClient.PostAsync("api/auth/logout", null);
        }
        finally
        {
            tokenStore.Clear();
            stateProvider.NotifyChanged();
        }
    }

    /// <summary>パスワード変更（変更後は再ログインが必要：サーバー側で全リフレッシュトークンが失効する）。
    /// <paramref name="fallbackError"/> は <see cref="LoginAsync"/> と同じ</summary>
    public async Task<string?> ChangePasswordAsync(string currentPassword, string newPassword, string fallbackError)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/change-password")
        {
            Content = JsonContent.Create(new ChangePasswordRequest(currentPassword, newPassword)),
        };
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenStore.AccessToken);
        var response = await bareClient.SendAsync(request);
        if (await response.ReadErrorAsync(fallbackError) is { } error)
        {
            return error;
        }
        tokenStore.Clear();
        stateProvider.NotifyChanged();
        return null;
    }
}
