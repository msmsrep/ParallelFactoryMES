namespace MesApp.Infrastructure;

/// <summary>
/// 書き込み可能なデータディレクトリの解決（DBファイル・JWT署名鍵の置き場所）。
/// MSIX配布ではインストール先（C:\Program Files\WindowsApps\...）が読み取り専用のため、
/// 相対パス指定はここを基準に絶対パス化する。
/// </summary>
public static class MesAppDataDirectory
{
    /// <summary>データディレクトリを明示指定する環境変数（サーバー運用・検証用の逃げ道）</summary>
    public const string EnvironmentVariableName = "MESAPP_DATA_DIR";

    private const string FolderName = "ParallelFactoryMES";

    private static readonly Lazy<string> _current = new(Create);

    /// <summary>データディレクトリの絶対パス。初回参照時にディレクトリを作成する</summary>
    public static string Current => _current.Value;

    private static string Create()
    {
        var configured = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        var path = !string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(configured)
            // MSIXパッケージ内ではLocalApplicationDataへの書き込みがパッケージ専用の領域に自動リダイレクトされる。
            // Create を付けないと、Linux で ~/.local/share が未作成のとき空文字が返り、カレントディレクトリ直下に作られてしまう
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
                FolderName);

        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// 相対パスをデータディレクトリ基準の絶対パスに解決する。
    /// 絶対パスはそのまま返す（テストや明示設定の指定を尊重するため）。
    /// </summary>
    public static string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        return Path.IsPathFullyQualified(path) ? path : Path.Combine(Current, path);
    }
}
