using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace MesApp.Api.Services.Webhooks;

/// <summary>
/// Webhook の秘密の値の発行と暗号化（Spec.md 3.8 Webhook）。
/// <para>
/// 署名（HMAC）に平文が要るので、APIキーのようにハッシュでは持てない。Data Protection で暗号化して DB に置き、
/// 鍵はデータディレクトリに置く（<c>DataProtection:KeysDirectory</c>。Spec.md 7.7：DB と一緒に退避しないと、
/// 戻した DB の秘密は復号できず再発行が要る）。
/// </para>
/// </summary>
public sealed class WebhookSecrets(IDataProtectionProvider provider)
{
    /// <summary>平文の先頭に付ける印（受け側の設定で APIキーと取り違えないように）</summary>
    public const string Prefix = "whsec_";

    private const int PrefixLength = 12;

    // 目的の文字列を変えると、既存の秘密をすべて復号できなくなる
    private readonly IDataProtector _protector = provider.CreateProtector("MesApp.Webhooks.Secret.v1");

    /// <summary>新しい秘密の値を作る。平文は応答で一度だけ返し、保存は暗号文と先頭だけ</summary>
    public (string Plain, string Protected, string DisplayPrefix) Generate()
    {
        var plain = Prefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        return (plain, _protector.Protect(plain), plain[..PrefixLength]);
    }

    /// <summary>署名のために平文へ戻す。鍵を失った（DB だけ戻した等）ときは <see cref="CryptographicException"/></summary>
    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
