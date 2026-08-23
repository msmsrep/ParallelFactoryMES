using System.Runtime.InteropServices;
using System.Text;

namespace MesApp.Desktop;

/// <summary>
/// MSIXパッケージとして実行されている場合に、アンインストールで一緒に削除される
/// パッケージ専用のデータ領域（%LOCALAPPDATA%\Packages\&lt;PFN&gt;\LocalState）を解決する。
///
/// 既定の %LOCALAPPDATA%\ParallelFactoryMES はパッケージ外のためアンインストールしても残る。
/// ストア配布のアプリが消えたあとにDBだけ残るのは利用者の想定と合わないので、
/// パッケージ実行時はこちらを使う（Spec.md 7.8）。
/// </summary>
internal static class PackagedDataDirectory
{
    /// <summary>パッケージ外で実行している（＝パッケージIDを持たない）ことを示すエラーコード</summary>
    private const int AppModelErrorNoPackage = 15700;

    private const int ErrorInsufficientBuffer = 122;

    /// <summary>
    /// データディレクトリを決めて <see cref="MesApp.Infrastructure.MesAppDataDirectory"/> に伝える。
    /// ログを含むあらゆる書き込みより前に、Main の先頭で呼ぶこと。
    /// </summary>
    public static void Apply()
    {
        // 利用者が明示指定しているときはそれを尊重する（検証・移行用の逃げ道）
        if (!string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(Infrastructure.MesAppDataDirectory.EnvironmentVariableName)))
        {
            return;
        }

        var packageFamilyName = TryGetPackageFamilyName();
        if (packageFamilyName is null)
        {
            // パッケージ外実行（開発時の直接起動など）は従来どおり %LOCALAPPDATA%\ParallelFactoryMES
            return;
        }

        var target = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages", packageFamilyName, "LocalState");

        Directory.CreateDirectory(target);

        Environment.SetEnvironmentVariable(
            Infrastructure.MesAppDataDirectory.EnvironmentVariableName, target);
    }

    private static string? TryGetPackageFamilyName()
    {
        uint length = 0;
        var result = GetCurrentPackageFamilyName(ref length, null);
        if (result == AppModelErrorNoPackage)
        {
            return null;
        }

        if (result != ErrorInsufficientBuffer)
        {
            return null;
        }

        var buffer = new StringBuilder((int)length);
        return GetCurrentPackageFamilyName(ref length, buffer) == 0 ? buffer.ToString() : null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFamilyName(ref uint packageFamilyNameLength, StringBuilder? packageFamilyName);
}
