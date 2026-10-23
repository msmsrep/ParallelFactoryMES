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

    /// <summary>
    /// 発行時点のセキュリティスタンプ（Identity の SecurityStamp）。無効化・ロール変更・パスワード変更で
    /// DB側の値が変わると、発行済みのアクセストークンは有効期限を待たずに使えなくなる
    /// </summary>
    public const string SecurityStamp = "security_stamp";
}
