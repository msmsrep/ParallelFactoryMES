using Microsoft.Extensions.Options;

namespace MesApp.Api.Services.Webhooks;

/// <summary>
/// Webhook の送信の常駐処理（Spec.md 3.8 Webhook。WHK-02）。決めた間隔で送信待ちを見に行き、
/// 1回分を送り切ったらまだ残っている分を続けて送る。1回の失敗で止めず、次の間隔で続ける（<see cref="FolderLink.FolderLinkWorker"/> と同じ形）
/// </summary>
public sealed class WebhookDispatchWorker(
    WebhookDispatcher dispatcher, IOptions<WebhookOptions> options, ILogger<WebhookDispatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        if (!opt.WorkerEnabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, opt.PollIntervalSeconds)));
        do
        {
            try
            {
                // 1回分（BatchSize）を送り切ったら、まだ残っているかもしれないので続ける
                while (await dispatcher.DispatchDueAsync(stoppingToken) >= Math.Max(1, opt.BatchSize)
                    && !stoppingToken.IsCancellationRequested)
                {
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Webhook の送信処理に失敗しました。次の間隔で続けます。");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
