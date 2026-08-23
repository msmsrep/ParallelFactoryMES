using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MesApp.Desktop;

/// <summary>
/// 単独PC向けデスクトップ配布（MSIX）のエントリポイント。
/// サーバー実行は MesApp.Api の Program.cs、こちらは同じ MesAppHost を1台のPC内で起動する。
/// </summary>
internal static class Program
{
    /// <summary>二重起動の抑止（ログオンユーザー単位。データもユーザー単位のため）</summary>
    private const string SingleInstanceMutexName = @"Local\ParallelFactoryMES.SingleInstance";

    [STAThread]
    private static void Main(string[] args)
    {
        StartupLog.Write("起動開始");

        // 同じSQLiteファイルを複数プロセスで奪い合うと起動が詰まるため、2つ目以降は既存ウィンドウを前面に出して終了する
        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            StartupLog.Write("既に起動しているため既存のウィンドウを前面に出して終了します");
            ActivateExistingWindow();
            return;
        }

        // 例外で無言のまま消えると原因が何も残らないので、必ず記録して利用者にも見せる
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportFatal(e.ExceptionObject as Exception);

        try
        {
            ApplicationConfiguration.Initialize();
            StartupLog.Write("ウィンドウを作成します");
            Application.Run(new MainForm(args));
            StartupLog.Write("正常終了");
        }
        catch (Exception ex)
        {
            ReportFatal(ex);
        }
    }

    private static void ReportFatal(Exception? exception)
    {
        if (exception is not null)
        {
            StartupLog.WriteException("致命的なエラー", exception);
        }

        MessageBox.Show(
            $"起動できませんでした。\n\n{exception?.Message}\n\n詳細は次のファイルに記録されています:\n"
            + Path.Combine(MesApp.Infrastructure.MesAppDataDirectory.Current, "startup.log"),
            "Parallel Factory MES",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);

        Environment.Exit(1);
    }

    private static void ActivateExistingWindow()
    {
        try
        {
            var current = Environment.ProcessId;
            var other = Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName)
                .FirstOrDefault(p => p.Id != current && p.MainWindowHandle != IntPtr.Zero);

            if (other is not null)
            {
                ShowWindow(other.MainWindowHandle, SW_RESTORE);
                SetForegroundWindow(other.MainWindowHandle);
            }
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("既存ウィンドウの前面化に失敗", ex);
        }
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
