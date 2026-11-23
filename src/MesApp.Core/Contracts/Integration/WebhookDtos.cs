namespace MesApp.Core.Contracts.Integration;

/// <summary>Webhook の送信先の一覧の1行（Spec.md 3.8 Webhook）。秘密の値は含めない</summary>
/// <param name="EventTypes">購読するイベントの種類（<c>WebhookEventTypes</c> の名前）</param>
/// <param name="SecretPrefix">秘密の値の先頭（例 whsec_AbCdEf）。受け側に設定した値と見分けるためだけに使う</param>
public record WebhookSubscriptionResponse(
    int Id,
    string Name,
    string Url,
    List<string> EventTypes,
    string SecretPrefix,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? SecretRotatedAt);

/// <summary>Webhook の送信先の登録・変更（秘密の値は変えない。変えるときは再発行）</summary>
/// <param name="Name">用途（例：班長チャットへの不適合通知）</param>
/// <param name="Url">送信先の URL（http・https）</param>
/// <param name="EventTypes">購読するイベントの種類（1つ以上）</param>
/// <param name="IsActive">無効にすると新しい出来事を送らない</param>
public record SaveWebhookSubscriptionRequest(string Name, string Url, List<string> EventTypes, bool IsActive);

/// <summary>登録・秘密の再発行の結果。<paramref name="PlainSecret"/> はこの応答でしか返さない（再表示できない）</summary>
public record CreatedWebhookSecretResponse(WebhookSubscriptionResponse Subscription, string PlainSecret);
