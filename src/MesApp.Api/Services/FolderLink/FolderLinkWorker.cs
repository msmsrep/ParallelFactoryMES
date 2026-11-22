namespace MesApp.Api.Services.FolderLink;

/// <summary>
/// フォルダ連携の常駐処理（Spec.md 3.8 フォルダ連携）。連携フォルダが設定されているときだけ、
/// 決めた間隔で <see cref="FolderLinkService.ScanAsync"/> を呼ぶ。1回の失敗で止めず、次の間隔で続ける
/// </summary>
public sealed class FolderLinkWorker(FolderLinkService folderLink, ILogger<FolderLinkWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!folderLink.Enabled)
        {
            return;
        }
        logger.LogInformation("フォルダ連携を開始します：{Root}（{Seconds}秒ごと）",
            folderLink.RootPath, folderLink.PollInterval.TotalSeconds);

        using var timer = new PeriodicTimer(folderLink.PollInterval);
        do
        {
            try
            {
                await folderLink.ScanAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "フォルダ連携の走査に失敗しました。次の間隔で続けます。");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
