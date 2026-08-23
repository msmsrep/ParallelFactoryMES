namespace MesApp.Desktop;

/// <summary>
/// 単独PC向けデスクトップ配布（MSIX）のエントリポイント。
/// サーバー実行は MesApp.Api の Program.cs、こちらは同じ MesAppHost を1台のPC内で起動する。
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args));
    }
}
