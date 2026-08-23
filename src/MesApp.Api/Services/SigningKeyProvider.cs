using System.Security.Cryptography;
using MesApp.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MesApp.Api.Services;

/// <summary>
/// JWT署名鍵の解決。設定値（Base64）を優先し、無ければ鍵ファイルを読み込み、それも無ければ生成して永続化する。
/// （オンプレ環境で管理者が鍵管理をしなくても、再起動をまたいでトークンが無効化されないようにするため）
/// </summary>
public class SigningKeyProvider
{
    public SymmetricSecurityKey Key { get; }

    public SigningKeyProvider(IOptions<JwtOptions> options)
    {
        var opt = options.Value;
        byte[] keyBytes;

        // 相対パス指定はデータディレクトリ基準に解決する（MSIX配布ではインストール先に書き込めないため）
        var keyFile = MesAppDataDirectory.Resolve(opt.SigningKeyFile);

        if (!string.IsNullOrWhiteSpace(opt.SigningKey))
        {
            keyBytes = Convert.FromBase64String(opt.SigningKey);
        }
        else if (File.Exists(keyFile))
        {
            keyBytes = Convert.FromBase64String(File.ReadAllText(keyFile).Trim());
        }
        else
        {
            keyBytes = RandomNumberGenerator.GetBytes(64);
            File.WriteAllText(keyFile, Convert.ToBase64String(keyBytes));
        }

        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException("JWT署名鍵は32バイト（256bit）以上が必要です（Jwt:SigningKey）。");
        }

        Key = new SymmetricSecurityKey(keyBytes);
    }
}
