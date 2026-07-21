using Microsoft.AspNetCore.Identity;

namespace MesApp.Core.Entities;

/// <summary>
/// ユーザー/工場従業員（Spec.md 5.1。F-10-10）
/// MESユーザーアカウントと工場従業員情報を統合管理する。
/// </summary>
public class AppUser : IdentityUser
{
    /// <summary>氏名（表示名）</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>在籍状態（false=退職・無効化。無効ユーザーはログイン不可）</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>初回ログイン時などにパスワード変更を強制するか（環境変数シードで作成された場合など）</summary>
    public bool MustChangePassword { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
