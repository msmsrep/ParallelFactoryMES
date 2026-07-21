using System.ComponentModel.DataAnnotations;

namespace MesApp.Core.Contracts.Auth;

public record LoginRequest(
    [Required] string UserName,
    [Required] string Password);

public record UserInfo(
    string Id,
    string UserName,
    string DisplayName,
    IReadOnlyList<string> Roles,
    bool MustChangePassword);

/// <summary>ログイン/リフレッシュ成功応答。リフレッシュトークンはHttpOnly Cookieで返すためボディに含めない（Spec.md 7.4）</summary>
public record TokenResponse(
    string AccessToken,
    int ExpiresInSeconds,
    UserInfo User);

public record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required] string NewPassword);
