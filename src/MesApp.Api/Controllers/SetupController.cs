using MesApp.Api.Localization;
using MesApp.Core.Abstractions;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Setup;
using MesApp.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 初期セットアップ（Spec.md 2.2 E）：Userテーブルが0件のときのみ初期管理者アカウントを作成できる。
/// 作成完了と同時に本APIは無効化される（ユーザー存在チェックにより）。
/// </summary>
[ApiController]
[Route("api/setup")]
[AllowAnonymous]
public class SetupController(
    UserManager<AppUser> userManager,
    IAuditLogger auditLogger) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<SetupStatusResponse>> Status(CancellationToken ct) =>
        new SetupStatusResponse(!await userManager.Users.AnyAsync(ct));

    [HttpPost("initialize")]
    public async Task<IActionResult> Initialize(InitializeRequest request, CancellationToken ct)
    {
        if (await userManager.Users.AnyAsync(ct))
        {
            return this.ConflictProblem(ApiText.T("初期セットアップは完了済みです。"));
        }

        var user = new AppUser
        {
            UserName = request.UserName,
            DisplayName = request.DisplayName,
            IsActive = true,
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return BadRequest(new ValidationProblemDetails(
                result.Errors.GroupBy(e => e.Code)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray())));
        }

        await userManager.AddToRoleAsync(user, MesRoles.SystemAdmin);
        await auditLogger.LogAsync("Setup", "InitializeAdmin", "User", user.Id,
            detail: $"userName={user.UserName}", ct: ct);
        return NoContent();
    }
}
