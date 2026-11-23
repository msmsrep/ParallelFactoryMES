namespace MesApp.Core.Entities;

/// <summary>
/// Webhook の送信先（Spec.md 3.8 Webhook。docs-dev/WebhookTasks.md WHK-01）。
/// <para>
/// 購読するイベントの種類を選び、その出来事が起きたら <see cref="Url"/> へ通知する。
/// 秘密の値は署名（HMAC）に使うため、ハッシュではなく復号できる形（Data Protection で暗号化）で持つ。
/// 平文は作成・再発行の応答でだけ返す。
/// </para>
/// </summary>
public class WebhookSubscription
{
    public int Id { get; set; }

    /// <summary>用途（例：班長チャットへの不適合通知）。一覧で見分けるための名前</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>送信先の URL（http・https。工場 LAN 内の私設アドレスも許す）</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>購読するイベントの種類（<c>WebhookEventTypes</c> の名前をカンマ区切り）</summary>
    public string EventTypes { get; set; } = string.Empty;

    /// <summary>署名に使う秘密の値（Data Protection で暗号化したもの）</summary>
    public string SecretProtected { get; set; } = string.Empty;

    /// <summary>秘密の値の先頭（例 <c>whsec_AbCdEf</c>）。受け側に設定した値と見分けるために残す。これだけでは署名できない</summary>
    public string SecretPrefix { get; set; } = string.Empty;

    /// <summary>無効にすると、新しい出来事の送信待ちを作らない（作成済みの送信待ちはそのまま）</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>作成した利用者（システム管理者）</summary>
    public string? CreatedByUserId { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>秘密の値を最後に再発行した日時</summary>
    public DateTimeOffset? SecretRotatedAt { get; set; }

    public List<WebhookDelivery> Deliveries { get; set; } = [];
}

/// <summary>
/// 送信待ちの出来事（Outbox。Spec.md 3.8 Webhook）。
/// <para>
/// 業務の変更と<b>同じトランザクション</b>で積み、常駐処理が後から送る（業務の処理の中で HTTP を呼ばない）。
/// 業務が巻き戻れば、この行も残らない。<see cref="Id"/> は通知の <c>sequence</c>（種類をまたいで増える。連番とは限らない）。
/// 購読している送信先が無い出来事は積まない。7.6節の運用ログ（C 層）。
/// </para>
/// </summary>
public class WebhookOutbox
{
    public long Id { get; set; }

    /// <summary>通知の一意の ID（受け側が重複を捨てるのに使う。再送しても変わらない）</summary>
    public Guid EventId { get; set; } = Guid.NewGuid();

    /// <summary>イベントの種類（例 <c>production-result.created</c>）</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>本文の形の版（v1 は 1）</summary>
    public int Version { get; set; } = 1;

    /// <summary>出来事が起きた日時</summary>
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>人が読める1文（チャットへの転送などにそのまま使える）</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>本文の <c>data</c>（JSON）。共通の包み（id・sequence・type 等）は送るときに付ける</summary>
    public string Payload { get; set; } = "{}";

    public List<WebhookDelivery> Deliveries { get; set; } = [];
}

/// <summary>
/// 送信先ごとの送信（Spec.md 3.8 Webhook）。出来事を積むときに、購読している送信先の数だけ作る。
/// 試行の結果（状態コード・エラー）を残し、再送の時刻を持つ。7.6節の運用ログ（C 層）
/// </summary>
public class WebhookDelivery
{
    public long Id { get; set; }

    public long OutboxId { get; set; }
    public WebhookOutbox? Outbox { get; set; }

    public int SubscriptionId { get; set; }
    public WebhookSubscription? Subscription { get; set; }

    public WebhookDeliveryState State { get; set; } = WebhookDeliveryState.Pending;

    /// <summary>試行した回数</summary>
    public int Attempts { get; set; }

    /// <summary>
    /// 次に送ってよい日時（Unix 秒。再送の間隔を空けるため）。
    /// SQLite は DateTimeOffset を SQL で比べられないので、取り出しの条件に使う時刻は数値で持つ（<see cref="IntegrationRequest"/> と同じ）
    /// </summary>
    public long NextAttemptAtUnixSeconds { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>常駐処理が取り出した日時（Unix 秒。複数台で同じ行を二重に送らないため。古ければ取り出し直してよい）</summary>
    public long? LockedAtUnixSeconds { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }

    /// <summary>最後の試行の HTTP 状態コード（接続できなかったときは null）</summary>
    public int? LastStatusCode { get; set; }

    /// <summary>最後の試行のエラーの先頭（サーバーのログと同じく利用者向けに訳さない）</summary>
    public string? LastError { get; set; }

    public DateTimeOffset? DeliveredAt { get; set; }
}
