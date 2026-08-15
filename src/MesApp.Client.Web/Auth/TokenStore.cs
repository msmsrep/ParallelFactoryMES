using MesApp.Core.Contracts.Auth;

namespace MesApp.Client.Web.Auth;

/// <summary>
/// アクセストークンとユーザー情報のメモリ保持（Spec.md 7.4：localStorage等の永続ストレージには
/// 保存しない。リフレッシュトークンはHttpOnly Cookieでブラウザが保持する）
/// </summary>
public class TokenStore
{
    public string? AccessToken { get; private set; }
    public UserInfo? User { get; private set; }

    public bool IsAuthenticated => AccessToken is not null && User is not null;

    public void Set(string accessToken, UserInfo user)
    {
        AccessToken = accessToken;
        User = user;
    }

    public void Clear()
    {
        AccessToken = null;
        User = null;
    }
}
