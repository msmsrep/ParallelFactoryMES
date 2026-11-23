using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MesApp.Api.Services.Webhooks;

/// <summary>
/// Webhook の署名（Spec.md 3.8 Webhook）。<c>X-Mes-Signature: sha256=&lt;hex&gt;</c> は、
/// 秘密の値を鍵にした <c>"&lt;X-Mes-Timestamp&gt;.&lt;本文&gt;"</c> の HMAC-SHA256（小文字の16進）。
/// <para>
/// 時刻を署名に含めるので、受け側は時刻のずれ（例：5分）で古い要求の送り直し（再生攻撃）を弾ける。
/// 形は Stripe・GitHub と同じなので、受け側の実装例が多い。<b>受け側との約束なので変えない</b>
/// </para>
/// </summary>
public static class WebhookSignature
{
    public const string EventHeader = "X-Mes-Event";
    public const string DeliveryHeader = "X-Mes-Delivery";
    public const string TimestampHeader = "X-Mes-Timestamp";
    public const string SignatureHeader = "X-Mes-Signature";

    /// <summary>署名の値（<c>sha256=</c> を含む）</summary>
    /// <param name="secret">秘密の値の平文（<c>whsec_</c> を含めた全体を鍵にする）</param>
    /// <param name="timestamp">Unix 秒</param>
    /// <param name="body">送る本文（UTF-8 のバイト列と同じ文字列）</param>
    public static string Compute(string secret, long timestamp, string body)
    {
        var message = timestamp.ToString(CultureInfo.InvariantCulture) + "." + body;
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(message));
        return "sha256=" + Convert.ToHexStringLower(hash);
    }
}
