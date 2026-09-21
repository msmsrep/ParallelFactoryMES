using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace MesApp.Api;

/// <summary>
/// リバースプロキシ（IIS / nginx）でHTTPSを終端する配置への対応（Spec.md 7.4）。
/// プロキシが付ける X-Forwarded-For / X-Forwarded-Proto を読み、元の接続元IPとスキームを復元する。
/// </summary>
/// <remarks>
/// 信頼するプロキシを <c>ReverseProxy:KnownProxies</c>（IP）・<c>ReverseProxy:KnownNetworks</c>（CIDR）で
/// 明示したときだけ有効にする。未設定で誰のヘッダでも信じると、HTTPで直接つないだ端末が
/// ヘッダを偽装して接続元IP（監査ログ）やHTTPS扱い（Cookieの Secure）を詐称できてしまうため。
/// 既定のループバック信頼もあえて外す（同じPCの他プロセスからの偽装を防ぐ）。
/// </remarks>
public static class ReverseProxyExtensions
{
    public const string SectionName = "ReverseProxy";

    /// <summary>設定があれば転送ヘッダを受け入れる。未設定なら何もしない</summary>
    public static void UseTrustedForwardedHeaders(this WebApplication app)
    {
        var section = app.Configuration.GetSection(SectionName);
        var proxies = section.GetSection("KnownProxies").Get<string[]>() ?? [];
        var networks = section.GetSection("KnownNetworks").Get<string[]>() ?? [];
        if (proxies.Length == 0 && networks.Length == 0)
        {
            return;
        }

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        };
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var proxy in proxies)
        {
            if (!IPAddress.TryParse(proxy, out var address))
            {
                throw new InvalidOperationException(
                    $"{SectionName}:KnownProxies の '{proxy}' はIPアドレスとして解釈できません。");
            }
            options.KnownProxies.Add(address);
        }
        foreach (var network in networks)
        {
            if (!System.Net.IPNetwork.TryParse(network, out var parsed))
            {
                throw new InvalidOperationException(
                    $"{SectionName}:KnownNetworks の '{network}' はCIDR（例: 192.168.10.0/24）として解釈できません。");
            }
            options.KnownIPNetworks.Add(parsed);
        }

        app.UseForwardedHeaders(options);
    }
}
