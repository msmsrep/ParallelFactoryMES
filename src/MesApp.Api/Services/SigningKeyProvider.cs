using System.Security.Cryptography;
using MesApp.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MesApp.Api.Services;

/// <summary>
/// JWT署名鍵の解決。設定値（Base64）を優先し、無ければ鍵ファイルを読み込み、それも無ければ生成して永続化する。
/// （オンプレ環境で管理者が鍵管理をしなくても、再起動をまたいでトークンが無効化されないようにするため）
/// </summary>
/// <remarks>
/// DIにシングルトンで登録し、トークン発行（<see cref="JwtTokenService"/>）と検証（JwtBearer）の双方が
/// 同じインスタンスを使う。別々に生成すると、鍵ファイルが無い初回に双方が別の鍵を作り、
/// 発行した直後のトークンが検証で弾かれる。
/// </remarks>
public class SigningKeyProvider
{
    public SymmetricSecurityKey Key { get; }

    public SigningKeyProvider(IOptions<JwtOptions> options)
    {
        var opt = options.Value;

        // 相対パス指定はデータディレクトリ基準に解決する（MSIX配布ではインストール先に書き込めないため）
        var keyFile = MesAppDataDirectory.Resolve(opt.SigningKeyFile);

        var keyBytes = !string.IsNullOrWhiteSpace(opt.SigningKey)
            ? Convert.FromBase64String(opt.SigningKey)
            : LoadOrCreateKeyFile(keyFile);

        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException("JWT署名鍵は32バイト（256bit）以上が必要です（Jwt:SigningKey）。");
        }

        Key = new SymmetricSecurityKey(keyBytes);
    }

    private static byte[] LoadOrCreateKeyFile(string keyFile)
    {
        if (File.Exists(keyFile))
        {
            return Convert.FromBase64String(File.ReadAllText(keyFile).Trim());
        }

        var generated = RandomNumberGenerator.GetBytes(64);

        // 別プロセスが同時に作った場合に上書きしない。一時ファイルへ書いてから移動し、
        // 中途半端な内容を読まれないようにする（移動が失敗＝先に作られていたら、そちらを読む）
        var tempFile = keyFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tempFile, Convert.ToBase64String(generated));
            RestrictToOwner(tempFile);
            File.Move(tempFile, keyFile, overwrite: false);
            return generated;
        }
        catch (IOException)
        {
            return Convert.FromBase64String(File.ReadAllText(keyFile).Trim());
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>署名鍵を所有者だけが読めるようにする（Windowsはユーザー配下のデータディレクトリで担保される）</summary>
    private static void RestrictToOwner(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
