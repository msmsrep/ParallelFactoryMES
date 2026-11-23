namespace MesApp.Core.Contracts.Integration;

/// <summary>Webhook の送信先の一覧の1行（Spec.md 3.8 Webhook）。秘密の値は含めない</summary>
/// <param name="EventTypes">購読するイベントの種類（<c>WebhookEventTypes</c> の名前）</param>
/// <param name="SecretPrefix">秘密の値の先頭（例 whsec_AbCdEf）。受け側に設定した値と見分けるためだけに使う</param>
/// <param name="PendingCount">まだ一度も送っていない送信待ち</param>
/// <param name="RetryingCount">失敗して再送を待っている送信</param>
/// <param name="FailedCount">再送が尽きて止まった送信（手で再送するまで送らない）</param>
public record WebhookSubscriptionResponse(
    int Id,
    string Name,
    string Url,
    List<string> EventTypes,
    string SecretPrefix,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? SecretRotatedAt,
    int PendingCount = 0,
    int RetryingCount = 0,
    int FailedCount = 0);

/// <summary>Webhook の送信先の登録・変更（秘密の値は変えない。変えるときは再発行）</summary>
/// <param name="Name">用途（例：班長チャットへの不適合通知）</param>
/// <param name="Url">送信先の URL（http・https）</param>
/// <param name="EventTypes">購読するイベントの種類（1つ以上）</param>
/// <param name="IsActive">無効にすると新しい出来事を送らない</param>
public record SaveWebhookSubscriptionRequest(string Name, string Url, List<string> EventTypes, bool IsActive);

/// <summary>登録・秘密の再発行の結果。<paramref name="PlainSecret"/> はこの応答でしか返さない（再表示できない）</summary>
public record CreatedWebhookSecretResponse(WebhookSubscriptionResponse Subscription, string PlainSecret);

/// <summary>送信の記録の1行（Spec.md 3.8 Webhook。WHK-02）</summary>
/// <param name="Sequence">通知の sequence（送信待ちの ID）</param>
/// <param name="EventId">通知の id（受け側が重複を捨てるのに使う）</param>
/// <param name="State">Pending・Delivered・Failed</param>
/// <param name="NextAttemptAt">次に送ってよい日時（送信待ちのときだけ意味がある）</param>
/// <param name="LastError">最後の試行のエラーの先頭（送ったときの値のまま。訳さない）</param>
public record WebhookDeliveryResponse(
    long Id,
    int SubscriptionId,
    string SubscriptionName,
    long Sequence,
    Guid EventId,
    string EventType,
    string Summary,
    DateTimeOffset OccurredAt,
    MesApp.Core.Entities.WebhookDeliveryState State,
    int Attempts,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? LastAttemptAt,
    int? LastStatusCode,
    string? LastError,
    DateTimeOffset? DeliveredAt);
