using MesApp.Api.Services;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Auth;
using MesApp.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace MesApp.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    JwtTokenService jwtTokenService,
    RefreshTokenService refreshTokenService,
    IAuditLogger auditLogger) : ControllerBase
{
    /// <summary>リフレッシュトークンCookie名。Path限定でauth系エンドポイントにのみ送信される</summary>
    public const string RefreshCookieName = "mesapp_rt";
    private const string RefreshCookiePath = "/api/auth";

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await userManager.FindByNameAsync(request.UserName);
        if (user is null || !user.IsActive)
        {
            await auditLogger.LogAsync("Auth", "LoginFailed", detail: $"userName={request.UserName}", ct: ct);
            return Unauthorized(new ProblemDetails { Title = "ユーザー名またはパスワードが正しくありません。" });
        }

        // lockoutOnFailure: true → 連続失敗でロックアウト（総当たり対策。Spec.md 7.4）
        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            await auditLogger.LogAsync("Auth", "LoginLockedOut", "User", user.Id, ct: ct);
            return Unauthorized(new ProblemDetails { Title = "アカウントが一時的にロックされています。しばらく待って再試行してください。" });
        }
        if (!result.Succeeded)
        {
            await auditLogger.LogAsync("Auth", "LoginFailed", "User", user.Id, ct: ct);
            return Unauthorized(new ProblemDetails { Title = "ユーザー名またはパスワードが正しくありません。" });
        }

        var response = await IssueTokensAsync(user, ct);
        await auditLogger.LogAsync("Auth", "Login", "User", user.Id, ct: ct);
        return response;
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<TokenResponse>> Refresh(CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue(RefreshCookieName, out var plainToken) || string.IsNullOrEmpty(plainToken))
        {
            return Unauthorized(new ProblemDetails { Title = "リフレッシュトークンがありません。再ログインしてください。" });
        }

        var current = await refreshTokenService.ValidateAsync(plainToken, ct);
        if (current is null)
        {
            DeleteRefreshCookie();
            return Unauthorized(new ProblemDetails { Title = "リフレッシュトークンが無効です。再ログインしてください。" });
        }

        // ローテーション：旧トークンは即失効、新トークンをCookieで再設定
        var (newPlain, newEntity) = await refreshTokenService.RotateAsync(current, ct);
        SetRefreshCookie(newPlain, newEntity.ExpiresAt);

        var user = current.User!;
        var roles = await userManager.GetRolesAsync(user);
        var (accessToken, expiresIn) = jwtTokenService.CreateAccessToken(user, roles);
        return new TokenResponse(accessToken, expiresIn, ToUserInfo(user, roles));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (Request.Cookies.TryGetValue(RefreshCookieName, out var plainToken) && !string.IsNullOrEmpty(plainToken))
        {
            await refreshTokenService.RevokeAsync(plainToken, ct);
        }
        DeleteRefreshCookie();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserInfo>> Me()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }
        var roles = await userManager.GetRolesAsync(user);
        return ToUserInfo(user, roles);
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return BadRequest(new ValidationProblemDetails(
                result.Errors.GroupBy(e => e.Code)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray())));
        }

        if (user.MustChangePassword)
        {
            user.MustChangePassword = false;
            await userManager.UpdateAsync(user);
        }

        // パスワード変更時は既存のリフレッシュトークンをすべて失効させる（Spec.md 7.4）
        await refreshTokenService.RevokeAllForUserAsync(user.Id, ct);
        DeleteRefreshCookie();
        await auditLogger.LogAsync("Auth", "ChangePassword", "User", user.Id, ct: ct);
        return NoContent();
    }

    private async Task<TokenResponse> IssueTokensAsync(AppUser user, CancellationToken ct)
    {
        var roles = await userManager.GetRolesAsync(user);
        var (accessToken, expiresIn) = jwtTokenService.CreateAccessToken(user, roles);
        var (plainRefresh, entity) = await refreshTokenService.IssueAsync(user, ct);
        SetRefreshCookie(plainRefresh, entity.ExpiresAt);
        return new TokenResponse(accessToken, expiresIn, ToUserInfo(user, roles));
    }

    private void SetRefreshCookie(string value, DateTimeOffset expires) =>
        Response.Cookies.Append(RefreshCookieName, value, new CookieOptions
        {
            HttpOnly = true,
            // ローカルHTTP（開発・プライベートCA未設定環境）でも動作させるため、HTTPS接続時のみSecureを付与
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = RefreshCookiePath,
            Expires = expires,
        });

    private void DeleteRefreshCookie() =>
        Response.Cookies.Delete(RefreshCookieName, new CookieOptions { Path = RefreshCookiePath });

    private static UserInfo ToUserInfo(AppUser user, IEnumerable<string> roles) =>
        new(user.Id, user.UserName ?? string.Empty, user.DisplayName, roles.ToList(), user.MustChangePassword);
}
