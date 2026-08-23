using MesApp.Infrastructure;

namespace MesApp.Desktop;

/// <summary>
/// 起動処理の記録。WinExeのためコンソール出力は誰にも見えず、
/// 起動できなかったときに手掛かりが何も残らないので、データディレクトリのファイルに残す。
/// </summary>
internal static class StartupLog
{
    private static readonly object _gate = new();

    private static readonly Lazy<string?> _path = new(() =>
    {
        try
        {
            var path = Path.Combine(MesAppDataDirectory.Current, "startup.log");

            // 起動のたびに追記するため、肥大化したら一度だけ切り詰める
            if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024)
            {
                File.Delete(path);
            }

            return path;
        }
        catch
        {
            // ログのために起動を止めない
            return null;
        }
    });

    public static void Write(string message)
    {
        var path = _path.Value;
        if (path is null)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                File.AppendAllText(
                    path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 同上
        }
    }

    public static void WriteException(string context, Exception exception)
    {
        Write($"{context}: {exception.GetType().Name}: {exception.Message}");
        Write(exception.ToString());
    }
}
