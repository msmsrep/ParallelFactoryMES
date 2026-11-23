using System.Text.Json;
using System.Text.Json.Serialization;
using MesApp.Core.Constants;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services.Webhooks;

/// <summary>
/// Webhook の出来事を送信待ち（Outbox）に積む（Spec.md 3.8 Webhook。docs-dev/WebhookTasks.md WHK-01）。
/// <para>
/// <b>行を <see cref="MesAppDbContext"/> に足すだけで、保存（<c>SaveChanges</c>）は呼び出し側</b>。
/// 業務の変更と同じトランザクションで保存されるので、業務が巻き戻れば送信待ちも残らない。
/// 状態を変える唯一の経路（各サービス）から呼ぶ。EF の変更の傍受からは作らない
/// （スキーマの変更がそのまま外部の約束を変えてしまうため）。
/// </para>
/// </summary>
public sealed class WebhookPublisher(MesAppDbContext db)
{
    /// <summary>
    /// 本文の <c>data</c> の書き方。項目名は camelCase、区分・状態は列挙の名前（英語）で送る
    /// （表示言語や画面の表示名で変わらないように。連携API の応答と同じ）
    /// </summary>
    public static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    // 1つの要求の中で何度も積むことがあるので、購読は要求（スコープ）ごとに1回だけ読む
    private List<(int Id, HashSet<string> Types)>? _subscriptions;

    /// <summary>
    /// 出来事を積む。その種類を購読している有効な送信先が無ければ何もしない
    /// （<c>sequence</c> は連番にならないが、増えることだけを約束する）
    /// </summary>
    /// <param name="eventType"><see cref="WebhookEventTypes"/> の名前</param>
    /// <param name="summary">人が読める1文</param>
    /// <param name="data">本文の <c>data</c>（WHK-03 の約束の型）</param>
    /// <returns>積んだ行（積まなかったら null）</returns>
    public async Task<WebhookOutbox?> EnqueueAsync(string eventType, string summary, object data, CancellationToken ct)
    {
        if (!WebhookEventTypes.All.Contains(eventType))
        {
            // 呼び出し側の書き間違い。黙って捨てると通知が出ないことに気付けない
            throw new ArgumentException($"未定義の Webhook イベントの種類です: {eventType}", nameof(eventType));
        }

        _subscriptions ??= (await db.WebhookSubscriptions.AsNoTracking()
                .Where(s => s.IsActive)
                .Select(s => new { s.Id, s.EventTypes })
                .ToListAsync(ct))
            .Select(s => (s.Id, WebhookEventTypes.Split(s.EventTypes).ToHashSet(StringComparer.Ordinal)))
            .ToList();

        var targets = _subscriptions.Where(s => s.Types.Contains(eventType)).Select(s => s.Id).ToList();
        if (targets.Count == 0)
        {
            return null;
        }

        var outbox = new WebhookOutbox
        {
            EventType = eventType,
            Summary = summary.Length > 500 ? summary[..500] : summary,
            Payload = JsonSerializer.Serialize(data, data.GetType(), PayloadJson),
            Deliveries = targets.Select(id => new WebhookDelivery { SubscriptionId = id }).ToList(),
        };
        db.WebhookOutbox.Add(outbox);
        return outbox;
    }
}
