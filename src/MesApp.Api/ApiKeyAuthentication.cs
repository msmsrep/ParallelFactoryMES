using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MesApp.Api;

/// <summary>
/// 連携API の APIキー認証（Spec.md 7.4 連携APIの認証。docs-dev/IntegrationApiTasks.md INT-01）。
/// <para>
/// キーは <c>X-Api-Key</c> ヘッダで受け取る。<b>連携API（<c>api/integration/v1</c>）の Controller だけが
/// <see cref="IntegrationSchemes"/> でこの方式を受け付ける</b>。画面用の API は既定の JWT のままなので、
/// キーでは呼べない（画面用 API を外部との約束にしない。キーが漏れたときの影響も絞る）。
/// </para>
/// </summary>
public static class ApiKeyAuthentication
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    /// <summary>キーの先頭に付ける印（ログや設定ファイルに紛れたときにキーだと分かるように）</summary>
    public const string KeyPrefix = "pfm_";

    /// <summary>連携API の Controller に付ける認証方式（画面から確かめられるようログインの JWT も通す）</summary>
    public const string IntegrationSchemes = $"{JwtBearerDefaults.AuthenticationScheme},{Scheme}";

    /// <summary>一覧で見分けるために残す先頭の文字数（印を含む）</summary>
    private const int VisiblePrefixLength = 12;

    /// <summary>新しいキーを作る（平文・保存するハッシュ・一覧に出す先頭）。平文は呼び出し側が一度だけ返す</summary>
    public static (string Plain, string Hash, string Prefix) Generate()
    {
        var plain = KeyPrefix + Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return (plain, Hash(plain), plain[..VisiblePrefixLength]);
    }

    /// <summary>キーのハッシュ（SHA-256・Base64）。乱数32バイトのキーなので、パスワードのような遅いハッシュは要らない</summary>
    public static string Hash(string plain) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(plain)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// <see cref="ApiKeyAuthentication"/> の認証ハンドラ。<b>要求ごとに</b>キーの失効・期限と利用者の有効を確かめ、
/// ロールも DB から読み直す（<see cref="AccessTokenValidation"/> と同じく、無効化・ロール変更を即時に効かせる。キャッシュは挟まない）
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    MesAppDbContext db) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>最後に使われた日時を書き直す間隔（要求のたびに書くと、連携の呼び出しがそのままDBの書き込みになる）</summary>
    private static readonly TimeSpan LastUsedResolution = TimeSpan.FromMinutes(1);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthentication.HeaderName, out var values) ||
            values.ToString() is not { Length: > 0 } plain)
        {
            return AuthenticateResult.NoResult();
        }

        var hash = ApiKeyAuthentication.Hash(plain.Trim());
        var now = DateTimeOffset.UtcNow;
        var ct = Context.RequestAborted;
        var key = await db.ApiKeys.AsNoTracking()
            .Where(k => k.KeyHash == hash)
            .Select(k => new
            {
                k.Id, k.Name, k.UserId, k.ExpiresAt, k.RevokedAt, k.LastUsedAt,
                UserName = k.User!.UserName, k.User.DisplayName, UserActive = k.User.IsActive,
            })
            .FirstOrDefaultAsync(ct);
        // 理由を呼び出し側へ細かく返さない（存在するキーかどうかを探らせない）。サーバーのログにだけ残す
        if (key is null)
        {
            return AuthenticateResult.Fail("APIキーが見つかりません。");
        }
        if (key.RevokedAt is not null)
        {
            return AuthenticateResult.Fail($"APIキー {key.Id} は失効しています。");
        }
        // SQLite は DateTimeOffset をDB側で比べられないので、取得してから比べる
        if (key.ExpiresAt is { } expires && expires <= now)
        {
            return AuthenticateResult.Fail($"APIキー {key.Id} は期限切れです。");
        }
        if (!key.UserActive)
        {
            return AuthenticateResult.Fail($"APIキー {key.Id} の利用者は無効です。");
        }

        var roles = await db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == key.UserId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
            .ToListAsync(ct);

        if (key.LastUsedAt is null || now - key.LastUsedAt >= LastUsedResolution)
        {
            // 認証の一部として書くが、業務の保存とは別にする（変更追跡に乗せると業務の SaveChanges に混ざる）
            await db.ApiKeys.Where(k => k.Id == key.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, now), ct);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, key.UserId),
            new(ClaimTypes.Name, key.UserName ?? string.Empty),
            new(MesClaimTypes.DisplayName, key.DisplayName),
            new(MesClaimTypes.ApiKeyId, key.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(MesClaimTypes.ApiKeyName, key.Name),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var identity = new ClaimsIdentity(claims, ApiKeyAuthentication.Scheme, ClaimTypes.Name, ClaimTypes.Role);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
