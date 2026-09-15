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
    bool IsActive,
    int? WorkCenterId = null,
    /// <summary>所属（部署・課）</summary>
    [MaxLength(100)] string? Department = null,
    /// <summary>所属する直（既定のシフト）</summary>
    int? ShiftId = null);

public record ResetPasswordRequest([Required] string NewPassword);

public record UserSummaryResponse(
    string Id, string UserName, string DisplayName, bool IsActive,
    bool MustChangePassword, List<string> Roles,
    int? WorkCenterId = null, string? WorkCenterCode = null, string? WorkCenterName = null,
    string? Department = null,
    int? ShiftId = null, string? ShiftCode = null, string? ShiftName = null);

/// <summary>
/// 作業者を選ばせるための選択肢（Spec.md 7.5）。差立で使う。
/// ユーザー管理の一覧（<see cref="UserSummaryResponse"/>）と分けているのは、
/// ロール・有効フラグ・パスワード状態まで全ロールへ配らずに、名前だけを渡すため。
/// </summary>
public record UserOptionResponse(string Id, string UserName, string DisplayName);

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
