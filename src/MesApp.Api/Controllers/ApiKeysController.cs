using System.Security.Claims;
using MesApp.Api.Localization;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Integration;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 連携API のAPIキーの発行・失効（Spec.md 7.4 連携APIの認証。docs-dev/IntegrationApiTasks.md INT-01）。
/// キーは利用者に代わって操作する鍵なので、一覧の参照もシステム管理者だけにする
/// </summary>
[ApiController]
[Route("api/api-keys")]
[Authorize]
public class ApiKeysController(MesAppDbContext db, IAuditLogger auditLogger) : ControllerBase
{
    private const int NameMaxLength = 100;

    /// <summary>発行済みのキー（失効したものを含む。新しい順）</summary>
    [HttpGet]
    [Authorize(Roles = MesRoleGroups.UserAdmin)]
    public async Task<ActionResult<List<ApiKeyResponse>>> List(CancellationToken ct)
    {
        var keys = await db.ApiKeys.AsNoTracking()
            .Select(k => new ApiKeyResponse(
                k.Id, k.Name, k.UserId, k.User!.UserName!, k.User.DisplayName, k.Prefix,
                k.CreatedAt, k.ExpiresAt, k.LastUsedAt, k.RevokedAt))
            .ToListAsync(ct);
        // SQLite は DateTimeOffset をDB側で並べられないので、取得してから並べる
        return keys.OrderByDescending(k => k.CreatedAt).ThenByDescending(k => k.Id).ToList();
    }

    /// <summary>キーを発行する。平文はこの応答でしか返さない</summary>
    [HttpPost]
    [Authorize(Roles = MesRoleGroups.UserAdmin)]
    public async Task<ActionResult<CreatedApiKeyResponse>> Create(CreateApiKeyRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return this.BadRequestProblem(ApiText.T("用途（名前）を入力してください。"));
        }
        if (name.Length > NameMaxLength)
        {
            return this.BadRequestProblem(ApiText.T("用途（名前）は{0}文字以内で入力してください。", NameMaxLength));
        }
        if (request.ExpiresAt is { } expires && expires <= DateTimeOffset.UtcNow)
        {
            return this.BadRequestProblem(ApiText.T("有効期限は今より後の日時を指定してください。"));
        }
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.UserId, ct);
        if (user is null)
        {
            return this.NotFoundProblem(ApiText.T("利用者が見つかりません。"));
        }
        if (!user.IsActive)
        {
            return this.BadRequestProblem(ApiText.T("無効な利用者にはAPIキーを発行できません。"));
        }

        var (plain, hash, prefix) = ApiKeyAuthentication.Generate();
        var key = new ApiKey
        {
            Name = name,
            UserId = user.Id,
            KeyHash = hash,
            Prefix = prefix,
            ExpiresAt = request.ExpiresAt,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
        };
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(ct);
        // 平文もハッシュも残さない（監査ログを読める人がキーを使えてはいけない）
        await auditLogger.LogAsync("ApiKey", "Create", "ApiKey", key.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            detail: new { key.Name, user = user.UserName, key.Prefix, key.ExpiresAt }, ct: ct);

        return new CreatedApiKeyResponse(
            new ApiKeyResponse(key.Id, key.Name, user.Id, user.UserName!, user.DisplayName, key.Prefix,
                key.CreatedAt, key.ExpiresAt, null, null),
            plain);
    }

    /// <summary>キーを失効させる（戻せない。必要なら新しく発行する）</summary>
    [HttpPost("{id:int}/revoke")]
    [Authorize(Roles = MesRoleGroups.UserAdmin)]
    public async Task<IActionResult> Revoke(int id, CancellationToken ct)
    {
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (key is null)
        {
            return this.NotFoundProblem(ApiText.T("APIキーが見つかりません。"));
        }
        if (key.RevokedAt is not null)
        {
            return this.ConflictProblem(ApiText.T("このAPIキーは既に失効しています。"));
        }
        key.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("ApiKey", "Revoke", "ApiKey", key.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            detail: new { key.Name, key.Prefix }, ct: ct);
        return NoContent();
    }
}
