using MesApp.Api.Policies;
using MesApp.Api.Services;
using MesApp.Core.Abstractions;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Common;
using MesApp.Core.Contracts.Users;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// ユーザー/工場従業員管理（F-10-10）とスキル・資格の割当（F-20-10-02〜05）。
/// 管理はシステム管理者専用。削除は論理削除（IsActive=false。退職時は無効化しログイン不可にする）。
/// <para>
/// クラスではロールを絞らない（Spec.md 7.4）。絞ると差立（B-10-20）で作業者を選ぶための
/// <see cref="Options"/> まで管理者専用になり、生産管理担当者が作業者を割り当てられなくなる。
/// </para>
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController(
    UserManager<AppUser> userManager,
    MesAppDbContext db,
    RefreshTokenService refreshTokenService,
    IBusinessDateService businessDate,
    IAuditLogger auditLogger) : ControllerBase
{
    /// <summary>
    /// 作業者の選択肢（Spec.md 7.5）。差立で作業者を選ばせるために全ロールへ開く。
    /// 有効なユーザーだけを、氏名の分かる最小限の項目で返す
    /// </summary>
    [HttpGet("options")]
    public async Task<ActionResult<OptionsResult<UserOptionResponse>>> Options(
        [FromQuery] OptionQuery options, CancellationToken ct = default)
    {
        var keyword = options.Keyword;
        return await userManager.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .Where(u => keyword == null
                || u.DisplayName.Contains(keyword)
                || (u.UserName != null && u.UserName.Contains(keyword)))
            .OrderBy(u => u.DisplayName)
            .Select(u => new UserOptionResponse(u.Id, u.UserName!, u.DisplayName))
            .ToOptionsResultAsync(options, ct);
    }

    [HttpGet]
    [Authorize(Roles = MesRoleGroups.UserAdmin)]
    public async Task<ActionResult<List<UserSummaryResponse>>> List(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var users = await userManager.Users.AsNoTracking()
            .Where(u => includeInactive || u.IsActive)
            .OrderBy(u => u.UserName)
            .ToListAsync(ct);

        var result = new List<UserSummaryResponse>(users.Count);
        foreach (var user in users)
        {
            result.Add(await ToSummaryAsync(user));
        }
        return result;
    }

    [HttpGet("{id}")]
    [Authorize(Roles = MesRoleGroups.UserAdmin)]
    public async Task<ActionResult<UserSummaryResponse>> Get(string id)
    {
        var user = await userManager.FindByIdAsync(id);
        return user is null ? NotFound() : await ToSummaryAsync(user);
    }

    [HttpPost]
    [Authorize(Roles = MesRoleGroups.UserAdmin)]
    public async Task<ActionResult<UserSummaryResponse>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var invalidRoles = request.Roles.Except(MesRoles.All).ToList();
        if (invalidRoles.Count > 0)
        {
            return BadRequest(new ProblemDetails { Title = $"不明なロールが含まれています: {string.Join(", ", invalidRoles)}" });
        }

        var user = new AppUser
        {
            UserName = request.UserName,
            DisplayName = request.DisplayName,
            IsActive = true,
            // 管理者が発行した初期パスワードは初回ログイン時に変更を強制する
            MustChangePassword = true,
        };
        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return BadRequest(new ValidationProblemDetails(
                result.Errors.GroupBy(e => e.Code)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray())));
        }
        if (request.Roles.Count > 0)
        {
            await userManager.AddToRolesAsync(user, request.Roles);
        }
        await auditLogger.LogAsync("User", "Create", nameof(AppUser), user.Id,
            detail: $"userName={user.UserName}, roles={string.Join(",", request.Roles)}", ct: ct);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, await ToSummaryAsync(user));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = MesRoleGroups.UserAdmin)]
    public async Task<ActionResult<UserSummaryResponse>> Update(string id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }
        var invalidRoles = request.Roles.Except(MesRoles.All).ToList();
        if (invalidRoles.Count > 0)
        {
            return BadRequest(new ProblemDetails { Title = $"不明なロールが含まれています: {string.Join(", ", invalidRoles)}" });
        }

        // 最後のシステム管理者を無効化・降格すると誰も権限操作できなくなる（復旧はDB操作のみ）
        var lastAdmin = await LastAdminPolicy.CheckUpdateAsync(userManager, user, request.IsActive, request.Roles);
        if (lastAdmin is not null)
        {
            return Conflict(new ProblemDetails { Title = lastAdmin });
        }

        user.DisplayName = request.DisplayName;
        var deactivated = user.IsActive && !request.IsActive;
        user.IsActive = request.IsActive;
        await userManager.UpdateAsync(user);

        var currentRoles = await userManager.GetRolesAsync(user);
        await userManager.RemoveFromRolesAsync(user, currentRoles.Except(request.Roles));
        await userManager.AddToRolesAsync(user, request.Roles.Except(currentRoles));

        if (deactivated)
        {
            // 無効化されたユーザーのセッションを止める（Spec.md 7.4）
            await refreshTokenService.RevokeAllForUserAsync(user.Id, ct);
        }
        await auditLogger.LogAsync("User", "Update", nameof(AppUser), user.Id,
            detail: $"userName={user.UserName}, isActive={user.IsActive}, roles={string.Join(",", request.Roles)}", ct: ct);
        return await ToSummaryAsync(user);
    }

    /// <summary>パスワードリセット（管理者操作。次回ログイン時に変更を強制）</summary>
    [HttpPost("{id}/reset-password")]
    [Authorize(Roles = MesRoleGroups.UserAdmin)]
    public async Task<IActionResult> ResetPassword(string id, ResetPasswordRequest request, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
        {
            return BadRequest(new ValidationProblemDetails(
                result.Errors.GroupBy(e => e.Code)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray())));
        }

        user.MustChangePassword = true;
        await userManager.UpdateAsync(user);
        await refreshTokenService.RevokeAllForUserAsync(user.Id, ct);
        await auditLogger.LogAsync("User", "ResetPassword", nameof(AppUser), user.Id, ct: ct);
        return NoContent();
    }

    // ---- スキル・資格（F-20-10）----

    [HttpGet("{id}/skills")]
    [Authorize(Roles = MesRoleGroups.UserAdmin)]
    public async Task<ActionResult<List<UserSkillResponse>>> GetSkills(string id, CancellationToken ct)
    {
        if (await userManager.FindByIdAsync(id) is null)
        {
            return NotFound();
        }
        var today = businessDate.Today;
        return await db.UserSkills.AsNoTracking()
            .Where(s => s.UserId == id)
            .OrderBy(s => s.Skill!.Code)
            .Select(s => new UserSkillResponse(
                s.Id, s.SkillId, s.Skill!.Code, s.Skill!.Name, s.Skill!.RequiresExpiry,
                s.AcquiredOn, s.ExpiresOn,
                s.Skill!.RequiresExpiry && s.ExpiresOn != null && s.ExpiresOn < today))
            .ToListAsync(ct);
    }

    /// <summary>スキル・資格の一括置換（登録・変更・有効期間の管理 F-20-10-02〜05）</summary>
    [HttpPut("{id}/skills")]
    [Authorize(Roles = MesRoleGroups.UserAdmin)]
    public async Task<ActionResult<List<UserSkillResponse>>> ReplaceSkills(
        string id, List<UserSkillRequest> skills, CancellationToken ct)
    {
        if (await userManager.FindByIdAsync(id) is null)
        {
            return NotFound();
        }
        if (skills.GroupBy(s => s.SkillId).Any(g => g.Count() > 1))
        {
            return BadRequest(new ProblemDetails { Title = "同一スキルが重複しています。" });
        }
        var skillIds = skills.Select(s => s.SkillId).ToList();
        var found = await db.Skills.CountAsync(s => skillIds.Contains(s.Id), ct);
        if (found != skillIds.Count)
        {
            return BadRequest(new ProblemDetails { Title = "存在しないスキルIDが含まれています。" });
        }

        var existing = await db.UserSkills.Where(s => s.UserId == id).ToListAsync(ct);
        db.UserSkills.RemoveRange(existing);
        db.UserSkills.AddRange(skills.Select(s => new UserSkill
        {
            UserId = id,
            SkillId = s.SkillId,
            AcquiredOn = s.AcquiredOn,
            ExpiresOn = s.ExpiresOn,
        }));
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("User", "UpdateSkills", nameof(AppUser), id,
            detail: $"skills={skillIds.Count}", ct: ct);
        return await GetSkills(id, ct);
    }

    private async Task<UserSummaryResponse> ToSummaryAsync(AppUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return new UserSummaryResponse(
            user.Id, user.UserName ?? string.Empty, user.DisplayName,
            user.IsActive, user.MustChangePassword, roles.ToList());
    }
}
