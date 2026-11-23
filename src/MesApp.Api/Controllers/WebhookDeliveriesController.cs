using System.Globalization;
using MesApp.Api.Localization;
using MesApp.Core.Abstractions;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Integration;
using MesApp.Core.Entities;
using MesApp.Core.Localization;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// Webhook の送信の記録と手での再送（Spec.md 3.8 Webhook。docs-dev/WebhookTasks.md WHK-02）。
/// 記録には送った通知の要約が含まれるので、送信先と同じくシステム管理者だけにする
/// </summary>
[ApiController]
[Route("api/webhook-deliveries")]
[Authorize]
public class WebhookDeliveriesController(MesAppDbContext db, IAuditLogger auditLogger) : ControllerBase
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;

    /// <summary>送信の記録（新しい順）。送信先・状態で絞れる</summary>
    [HttpGet]
    [Authorize(Roles = MesRoleGroups.UserAdmin)]
    public async Task<ActionResult<List<WebhookDeliveryResponse>>> List(
        int? subscriptionId, WebhookDeliveryState? state, int? limit, CancellationToken ct)
    {
        var query = db.WebhookDeliveries.AsNoTracking();
        if (subscriptionId is { } sid)
        {
            query = query.Where(d => d.SubscriptionId == sid);
        }
        if (state is { } st)
        {
            query = query.Where(d => d.State == st);
        }
        var rows = await query
            .Include(d => d.Outbox)
            .Include(d => d.Subscription)
            .OrderByDescending(d => d.Id)
            .Take(Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit))
            .ToListAsync(ct);
        return rows.Select(ToResponse).ToList();
    }

    /// <summary>
    /// 止まった送信（失敗）を送信待ちに戻す。回数は数え直し、次の間隔で常駐処理が送る。
    /// 通知の id・sequence は変わらない（受け側は届いていれば重複として捨てられる）
    /// </summary>
    [HttpPost("{id:long}/resend")]
    [Authorize(Roles = MesRoleGroups.UserAdmin)]
    public async Task<ActionResult<WebhookDeliveryResponse>> Resend(long id, CancellationToken ct)
    {
        var delivery = await db.WebhookDeliveries
            .Include(d => d.Outbox)
            .Include(d => d.Subscription)
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        if (delivery is null)
        {
            return this.NotFoundProblem(ApiText.T("Webhook の送信が見つかりません。"));
        }
        if (delivery.State != WebhookDeliveryState.Failed)
        {
            return this.ConflictProblem(ApiText.T("再送できるのは、送信が止まったもの（{0}）だけです。", EnumLabels.Of(WebhookDeliveryState.Failed)));
        }
        var before = new { delivery.State, delivery.Attempts, delivery.LastStatusCode, delivery.LastError };
        delivery.State = WebhookDeliveryState.Pending;
        delivery.Attempts = 0;
        delivery.NextAttemptAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        delivery.LockedAtUnixSeconds = null;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Webhook", "Resend", "WebhookDelivery", delivery.Id.ToString(CultureInfo.InvariantCulture),
            detail: new
            {
                before,
                after = new { delivery.State, delivery.Attempts },
                subscription = delivery.Subscription!.Name,
                delivery.Outbox!.EventType,
            }, ct: ct);
        return ToResponse(delivery);
    }

    internal static WebhookDeliveryResponse ToResponse(WebhookDelivery d) => new(
        d.Id, d.SubscriptionId, d.Subscription!.Name, d.OutboxId, d.Outbox!.EventId, d.Outbox.EventType, d.Outbox.Summary,
        d.Outbox.OccurredAt, d.State, d.Attempts, DateTimeOffset.FromUnixTimeSeconds(d.NextAttemptAtUnixSeconds),
        d.LastAttemptAt, d.LastStatusCode, d.LastError, d.DeliveredAt);
}
