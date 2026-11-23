using System.Globalization;
using System.Security.Claims;
using MesApp.Api.Localization;
using MesApp.Api.Services.Webhooks;
using MesApp.Core.Abstractions;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Integration;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// Webhook の送信先の登録・変更・削除と秘密の再発行（Spec.md 3.8 Webhook。docs-dev/WebhookTasks.md WHK-01）。
/// 送信先は業務データの持ち出し口になるので、一覧の参照もシステム管理者だけにする（APIキーと同じ）
/// </summary>
[ApiController]
[Route("api/webhook-subscriptions")]
[Authorize]
public class WebhookSubscriptionsController(MesAppDbContext db, WebhookSecrets secrets, IAuditLogger auditLogger) : ControllerBase
{
    private const int NameMaxLength = 100;
    private const int UrlMaxLength = 2000;

    /// <summary>登録済みの送信先（登録順）</summary>
    [HttpGet]
    [Authorize(Roles = MesRoleGroups.UserAdmin)]
    public async Task<ActionResult<List<WebhookSubscriptionResponse>>> List(CancellationToken ct)
    {
        var rows = await db.WebhookSubscriptions.AsNoTracking().OrderBy(s => s.Id).ToListAsync(ct);
        return rows.Select(ToResponse).ToList();
    }

    /// <summary>送信先を登録する。秘密の値の平文はこの応答でしか返さない</summary>
    [HttpPost]
    [Authorize(Roles = MesRoleGroups.UserAdmin)]
    public async Task<ActionResult<CreatedWebhookSecretResponse>> Create(SaveWebhookSubscriptionRequest request, CancellationToken ct)
    {
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }
        var (plain, protectedSecret, prefix) = secrets.Generate();
        var subscription = new WebhookSubscription
        {
            Name = request.Name.Trim(),
            Url = request.Url.Trim(),
            EventTypes = Join(request.EventTypes),
            SecretProtected = protectedSecret,
            SecretPrefix = prefix,
            IsActive = request.IsActive,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
        };
        db.WebhookSubscriptions.Add(subscription);
        await db.SaveChangesAsync(ct);
        // 平文も暗号文も残さない（監査ログを読める人が署名を作れてはいけない）
        await auditLogger.LogAsync("Webhook", "Create", "WebhookSubscription", Key(subscription),
            detail: Snapshot(subscription), ct: ct);
        return new CreatedWebhookSecretResponse(ToResponse(subscription), plain);
    }

    /// <summary>送信先を変更する（秘密の値は変えない）</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = MesRoleGroups.UserAdmin)]
    public async Task<ActionResult<WebhookSubscriptionResponse>> Update(int id, SaveWebhookSubscriptionRequest request, CancellationToken ct)
    {
        var subscription = await db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (subscription is null)
        {
            return this.NotFoundProblem(ApiText.T("Webhook の送信先が見つかりません。"));
        }
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }
        var before = Snapshot(subscription);
        subscription.Name = request.Name.Trim();
        subscription.Url = request.Url.Trim();
        subscription.EventTypes = Join(request.EventTypes);
        subscription.IsActive = request.IsActive;
        subscription.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Webhook", "Update", "WebhookSubscription", Key(subscription),
            detail: new { before, after = Snapshot(subscription) }, ct: ct);
        return ToResponse(subscription);
    }

    /// <summary>秘密の値を再発行する。古い値はすぐに使えなくなる（以後の送信は新しい値で署名する）</summary>
    [HttpPost("{id:int}/rotate-secret")]
    [Authorize(Roles = MesRoleGroups.UserAdmin)]
    public async Task<ActionResult<CreatedWebhookSecretResponse>> RotateSecret(int id, CancellationToken ct)
    {
        var subscription = await db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (subscription is null)
        {
            return this.NotFoundProblem(ApiText.T("Webhook の送信先が見つかりません。"));
        }
        var beforePrefix = subscription.SecretPrefix;
        var (plain, protectedSecret, prefix) = secrets.Generate();
        subscription.SecretProtected = protectedSecret;
        subscription.SecretPrefix = prefix;
        subscription.SecretRotatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Webhook", "RotateSecret", "WebhookSubscription", Key(subscription),
            detail: new { subscription.Name, before = beforePrefix, after = prefix }, ct: ct);
        return new CreatedWebhookSecretResponse(ToResponse(subscription), plain);
    }

    /// <summary>送信先を削除する。まだ送っていない送信待ちと送信の記録も消える（止めるだけなら無効にする）</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = MesRoleGroups.UserAdmin)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var subscription = await db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (subscription is null)
        {
            return this.NotFoundProblem(ApiText.T("Webhook の送信先が見つかりません。"));
        }
        db.WebhookSubscriptions.Remove(subscription);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Webhook", "Delete", "WebhookSubscription", Key(subscription),
            detail: Snapshot(subscription), ct: ct);
        return NoContent();
    }

    private ActionResult? Validate(SaveWebhookSubscriptionRequest request)
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
        var url = request.Url?.Trim() ?? string.Empty;
        if (url.Length > UrlMaxLength
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return this.BadRequestProblem(ApiText.T("送信先の URL は http:// か https:// で始まる{0}文字以内の URL を入力してください。", UrlMaxLength));
        }
        if (uri.UserInfo.Length > 0)
        {
            // 一覧・監査ログに URL がそのまま出るので、パスワードを書かせない
            return this.BadRequestProblem(ApiText.T("送信先の URL に利用者名・パスワードを含めないでください。受け側は署名で送り手を確かめます。"));
        }
        var types = request.EventTypes ?? [];
        if (types.Count == 0)
        {
            return this.BadRequestProblem(ApiText.T("通知するイベントの種類を1つ以上選んでください。"));
        }
        if (types.FirstOrDefault(t => !WebhookEventTypes.All.Contains(t)) is { } unknown)
        {
            return this.BadRequestProblem(ApiText.T("イベントの種類 '{0}' はありません。", unknown));
        }
        return null;
    }

    // 画面の一覧の順（WebhookEventTypes.All の順）に揃え、重複を除いて保存する
    private static string Join(List<string> types) =>
        string.Join(',', WebhookEventTypes.All.Where(types.Contains));

    private static string Key(WebhookSubscription s) => s.Id.ToString(CultureInfo.InvariantCulture);

    private static object Snapshot(WebhookSubscription s) =>
        new { s.Name, s.Url, s.EventTypes, s.IsActive, s.SecretPrefix };

    private static WebhookSubscriptionResponse ToResponse(WebhookSubscription s) => new(
        s.Id, s.Name, s.Url, WebhookEventTypes.Split(s.EventTypes), s.SecretPrefix, s.IsActive,
        s.CreatedAt, s.UpdatedAt, s.SecretRotatedAt);
}
