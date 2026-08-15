using System.ComponentModel.DataAnnotations;

namespace MesApp.Core.Contracts.Users;

// ユーザー/工場従業員管理（F-10-10）とスキル・資格管理（F-20-10）

public record CreateUserRequest(
    [Required, MaxLength(256)] string UserName,
    [Required] string Password,
    [Required, MaxLength(200)] string DisplayName,
    List<string> Roles);

public record UpdateUserRequest(
    [Required, MaxLength(200)] string DisplayName,
    List<string> Roles,
    bool IsActive);

public record ResetPasswordRequest([Required] string NewPassword);

public record UserSummaryResponse(
    string Id, string UserName, string DisplayName, bool IsActive,
    bool MustChangePassword, List<string> Roles);

/// <summary>従業員スキル・資格の登録（F-20-10-02〜03）</summary>
public record UserSkillRequest(
    int SkillId,
    DateOnly? AcquiredOn,
    DateOnly? ExpiresOn);

public record UserSkillResponse(
    int Id, int SkillId, string SkillCode, string SkillName, bool RequiresExpiry,
    DateOnly? AcquiredOn, DateOnly? ExpiresOn,
    /// <summary>有効期限切れか（F-20-10-04 警告用）</summary>
    bool IsExpired);
