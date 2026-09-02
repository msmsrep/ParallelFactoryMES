namespace MesApp.Core.Constants;

/// <summary>
/// アクセストークンに載せる独自クレーム（Spec.md 7.4）
/// </summary>
public static class MesClaimTypes
{
    /// <summary>表示名</summary>
    public const string DisplayName = "display_name";

    /// <summary>
    /// パスワード変更が未了であること。付いているトークンはパスワード変更以外の操作に使えない
    /// （初期パスワードのまま業務APIを叩けないようにするため）
    /// </summary>
    public const string MustChangePassword = "must_change_password";
}
