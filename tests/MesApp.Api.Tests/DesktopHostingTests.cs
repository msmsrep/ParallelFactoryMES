using MesApp.Infrastructure;

namespace MesApp.Api.Tests;

/// <summary>
/// 単独PC向けMSIX配布のためのデータ保存先の解決（Spec.md 4章・7.8）。
/// MSIXのインストール先は読み取り専用なので、相対パス指定は書き込み可能な場所に解決される必要がある。
/// </summary>
public class DesktopHostingTests
{
    [Fact]
    public void データディレクトリは書き込み可能な場所に作られる()
    {
        var directory = MesAppDataDirectory.Current;

        Assert.True(Path.IsPathFullyQualified(directory));
        Assert.True(Directory.Exists(directory));

        // 実行ファイルの隣ではないこと（MSIXでは書き込めないため）
        Assert.NotEqual(
            Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory),
            Path.TrimEndingDirectorySeparator(directory));
    }

    [Fact]
    public void 相対パスはデータディレクトリ基準に解決される()
    {
        var resolved = MesAppDataDirectory.Resolve("mesapp.db");

        Assert.Equal(Path.Combine(MesAppDataDirectory.Current, "mesapp.db"), resolved);
    }

    [Fact]
    public void 絶対パスはそのまま使われる()
    {
        // テスト（ApiFactory）や明示設定が渡す一時ディレクトリの指定を壊さないための規約
        var absolute = Path.Combine(Path.GetTempPath(), "mesapp-explicit", "test.db");

        Assert.Equal(absolute, MesAppDataDirectory.Resolve(absolute));
    }
}
