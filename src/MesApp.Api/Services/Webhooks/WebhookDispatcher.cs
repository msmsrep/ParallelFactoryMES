using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MesApp.Api.Services.Webhooks;

/// <summary>
/// Webhook の送信（Spec.md 3.8 Webhook。docs-dev/WebhookTasks.md WHK-02）。
/// <para>
/// 送信待ちを取り出し、署名を付けて送り、結果を残す。失敗は間隔を空けて再送し、尽きたら失敗で止める。
/// 取り出しは「送信待ちで、取り出されていない（または取り出しが古い）行」を条件付きの更新で取り、
/// 更新できた1件だけを送る（複数台で動かしても同じ行を二重に送らない）。
/// </para>
/// </summary>
public sealed class WebhookDispatcher(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    WebhookSecrets secrets,
    IOptions<WebhookOptions> options,
    ILogger<WebhookDispatcher> logger)
{
    /// <summary>送信に使う名前付きの HttpClient（テストは受け側をこの名前で差し替える）</summary>
    public const string HttpClientName = "webhooks";

    /// <summary>送信の確認だけに使う種類（購読の対象ではない。WebhookEventTypes には入れない）</summary>
    public const string PingEventType = "webhook.ping";

    /// <summary>
    /// 失敗の後に次を送るまでの間隔（n 回目の失敗の後は n 番目。尽きたら最後の値）。
    /// 既定の8回で、最初の失敗から約2日弱は送り直し続ける（相手の保守で一時的に落ちることが多いため）
    /// </summary>
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(12),
        TimeSpan.FromHours(24),
    ];

    // 本文は人が読めるように日本語をそのまま書く（署名は送ったバイト列そのものに付くので、どちらでもよい）
    private static readonly JsonSerializerOptions BodyJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly WebhookOptions _options = options.Value;

    /// <summary>送ってよい時刻を過ぎた送信待ちを、1回分（<see cref="WebhookOptions.BatchSize"/>）送る</summary>
    /// <returns>取り出して送った件数（成否を問わない）</returns>
    public async Task<int> DispatchDueAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesAppDbContext>();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var staleLock = now - _options.LockTimeoutSeconds;

        var candidates = await db.WebhookDeliveries.AsNoTracking()
            .Where(d => d.State == WebhookDeliveryState.Pending
                && d.NextAttemptAtUnixSeconds <= now
                && (d.LockedAtUnixSeconds == null || d.LockedAtUnixSeconds < staleLock))
            .OrderBy(d => d.Id)
            .Select(d => d.Id)
            .Take(Math.Max(1, _options.BatchSize))
            .ToListAsync(ct);

        var sent = 0;
        foreach (var id in candidates)
        {
            if (await SendIfClaimedAsync(db, id, ct))
            {
                sent++;
            }
        }
        return sent;
    }

    /// <summary>
    /// 1件をいま送る（テスト送信。送ってよい時刻を待たない）。取り出せなかったら false
    /// （別の処理がちょうど送っている）
    /// </summary>
    public Task<bool> SendNowAsync(MesAppDbContext db, long deliveryId, CancellationToken ct) =>
        SendIfClaimedAsync(db, deliveryId, ct, ignoreSchedule: true);

    private async Task<bool> SendIfClaimedAsync(MesAppDbContext db, long id, CancellationToken ct, bool ignoreSchedule = false)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var staleLock = now - _options.LockTimeoutSeconds;
        // 条件付きの更新で取り出す。同時に取りに来たもう一方は0行になり、送らない
        var claimed = await db.WebhookDeliveries
            .Where(d => d.Id == id
                && d.State == WebhookDeliveryState.Pending
                && (ignoreSchedule || d.NextAttemptAtUnixSeconds <= now)
                && (d.LockedAtUnixSeconds == null || d.LockedAtUnixSeconds < staleLock))
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.LockedAtUnixSeconds, now), ct);
        if (claimed != 1)
        {
            return false;
        }

        var delivery = await db.WebhookDeliveries
            .Include(d => d.Outbox)
            .Include(d => d.Subscription)
            .SingleAsync(d => d.Id == id, ct);
        await SendAsync(delivery, ct);
        delivery.LockedAtUnixSeconds = null;
        // 送った結果は、止める合図（ct）が来ていても残す（残さないと同じ通知をもう一度送る）
        await db.SaveChangesAsync(CancellationToken.None);
        return true;
    }

    private async Task SendAsync(WebhookDelivery delivery, CancellationToken ct)
    {
        var outbox = delivery.Outbox!;
        var subscription = delivery.Subscription!;
        var attemptAt = DateTimeOffset.UtcNow;
        delivery.Attempts++;
        delivery.LastAttemptAt = attemptAt;

        string secret;
        try
        {
            secret = secrets.Unprotect(subscription.SecretProtected);
        }
        catch (CryptographicException)
        {
            // DB だけを戻して Data Protection の鍵を失ったとき（Spec.md 7.7）。送り直しても直らない
            // 保存される値なので訳さない（サーバーのログと同じ扱い）
            Fail(delivery, null, "秘密の値を復号できません（Data Protection の鍵が無い）。送信先の秘密の値を再発行してから再送してください。", retry: false);
            logger.LogWarning("Webhook の秘密の値を復号できません：送信先 {SubscriptionId}", subscription.Id);
            return;
        }

        var body = BuildBody(outbox);
        var timestamp = attemptAt.ToUnixTimeSeconds();
        using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(WebhookSignature.EventHeader, outbox.EventType);
        request.Headers.Add(WebhookSignature.DeliveryHeader, delivery.Id.ToString(CultureInfo.InvariantCulture));
        request.Headers.Add(WebhookSignature.TimestampHeader, timestamp.ToString(CultureInfo.InvariantCulture));
        request.Headers.Add(WebhookSignature.SignatureHeader, WebhookSignature.Compute(secret, timestamp, body));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                delivery.State = WebhookDeliveryState.Delivered;
                delivery.DeliveredAt = DateTimeOffset.UtcNow;
                delivery.LastStatusCode = status;
                delivery.LastError = null;
                return;
            }
            var text = await ReadHeadAsync(response, timeout.Token);
            Fail(delivery, status, $"HTTP {status} {response.ReasonPhrase} {text}".Trim(), ShouldRetry(response.StatusCode));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Fail(delivery, null, $"応答がありませんでした（{_options.TimeoutSeconds}秒）。", retry: true);
        }
        catch (HttpRequestException ex)
        {
            Fail(delivery, null, ex.Message, retry: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UriFormatException)
        {
            // URL として送れない（登録時に検査しているので、通常は起きない）
            Fail(delivery, null, ex.Message, retry: false);
        }
    }

    /// <summary>
    /// 本文。共通の包みはここで付ける（<c>sequence</c>＝送信待ちの ID は保存するまで決まらないため）。
    /// 項目の名前と意味は受け側との約束（WHK-03 で基準ファイルに固定する）
    /// </summary>
    internal static string BuildBody(WebhookOutbox outbox)
    {
        var envelope = new JsonObject
        {
            ["id"] = outbox.EventId,
            ["sequence"] = outbox.Id,
            ["type"] = outbox.EventType,
            ["version"] = outbox.Version,
            ["occurredAt"] = outbox.OccurredAt,
            ["summary"] = outbox.Summary,
            ["data"] = JsonNode.Parse(outbox.Payload),
        };
        return envelope.ToJsonString(BodyJson);
    }

    /// <summary>
    /// 再送するか。接続できない・5xx・408（待ち切れ）・429（混雑）は相手が戻れば通るので再送する。
    /// それ以外の 4xx と 3xx（リダイレクトは追わない）は送り直しても同じなので止める
    /// （受け側の不具合で同じ要求を送り続けない）
    /// </summary>
    internal static bool ShouldRetry(HttpStatusCode status) =>
        (int)status >= 500 || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;

    private void Fail(WebhookDelivery delivery, int? status, string error, bool retry)
    {
        delivery.LastStatusCode = status;
        delivery.LastError = error.Length > 1000 ? error[..1000] : error;
        // テスト送信は再送しない（結果はその場で画面に返している。受け側を直したらもう一度押す）
        if (retry && delivery.Attempts < _options.MaxAttempts && delivery.Outbox?.EventType != PingEventType)
        {
            var delay = RetryDelays[Math.Min(delivery.Attempts, RetryDelays.Length) - 1];
            delivery.NextAttemptAtUnixSeconds = DateTimeOffset.UtcNow.Add(delay).ToUnixTimeSeconds();
            logger.LogInformation("Webhook の送信に失敗しました。{Delay} 後に再送します：送信 {DeliveryId}（{Error}）",
                delay, delivery.Id, delivery.LastError);
            return;
        }
        delivery.State = WebhookDeliveryState.Failed;
        logger.LogWarning("Webhook の送信を止めました：送信 {DeliveryId}・{Attempts} 回目（{Error}）",
            delivery.Id, delivery.Attempts, delivery.LastError);
    }

    // 失敗の手がかりに、応答の本文の先頭だけを残す（大きな HTML のエラー画面を丸ごと残さない）
    private static async Task<string> ReadHeadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            return text.Length > 300 ? text[..300] : text;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            return string.Empty;
        }
    }
}
