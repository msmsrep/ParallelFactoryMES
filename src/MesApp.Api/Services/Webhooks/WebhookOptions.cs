namespace MesApp.Api.Services.Webhooks;

/// <summary>Webhook の送信の設定（appsettings の <c>Webhooks</c>。Spec.md 3.8 Webhook）</summary>
public sealed class WebhookOptions
{
    public const string SectionName = "Webhooks";

    /// <summary>常駐処理で送るか（テストでは止めて、送信を直接呼ぶ）</summary>
    public bool WorkerEnabled { get; set; } = true;

    /// <summary>送信待ちを見に行く間隔（秒）</summary>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>1回の送信の待ち時間（秒）。過ぎたら失敗として再送に回す</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>試行の上限（初回を含む）。尽きたら失敗で止め、画面から手で再送する</summary>
    public int MaxAttempts { get; set; } = 8;

    /// <summary>1回に取り出す件数</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>
    /// 取り出したまま戻らない行（送信中に落ちた等）を取り出し直すまでの秒数。
    /// 送信の待ち時間より十分長くする（短いと送信中の行をもう一方が取り出して二重に送る）
    /// </summary>
    public int LockTimeoutSeconds { get; set; } = 300;
}
