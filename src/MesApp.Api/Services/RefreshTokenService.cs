using System.Security.Cryptography;
using System.Text;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MesApp.Api.Services;

/// <summary>
/// リフレッシュトークンの発行・検証・ローテーション（Spec.md 7.4。DBにはSHA-256ハッシュのみ保存）
/// </summary>
public class RefreshTokenService(MesAppDbContext db, IOptions<JwtOptions> options)
{
    /// <summary>期限切れトークンを消すまでの猶予（リフレッシュトークン有効期間の何倍まで残すか）</summary>
    private const int ExpiredRetentionFactor = 2;

    private readonly JwtOptions _options = options.Value;

    public async Task<(string PlainToken, RefreshToken Entity)> IssueAsync(AppUser user, CancellationToken ct = default)
    {
        var plain = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var entity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(plain),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(_options.RefreshTokenLifetimeHours),
        };
        db.RefreshTokens.Add(entity);
        await PurgeExpiredAsync(user.Id, ct);
        await db.SaveChangesAsync(ct);
        return (plain, entity);
    }

    /// <summary>
    /// トークンを検証する。有効なら <see cref="RefreshTokenValidation.Token"/> に返す。
    /// 失効済みトークンの再提示は盗用の可能性があるため、区別して返す（呼び出し側でファミリー失効させる）。
    /// </summary>
    public async Task<RefreshTokenValidation> ValidateAsync(string plainToken, CancellationToken ct = default)
    {
        var hash = Hash(plainToken);
        var token = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (token is null)
        {
            return new RefreshTokenValidation(null, null);
        }
        // ローテーション済みの古いトークンが出てきた＝正規の利用者と盗んだ側の両方が持っている可能性
        if (token.RevokedAt is not null)
        {
            return new RefreshTokenValidation(null, token.UserId);
        }
        return token.IsActive && token.User is { IsActive: true }
            ? new RefreshTokenValidation(token, null)
            : new RefreshTokenValidation(null, null);
    }

    /// <summary>ローテーション：旧トークンを失効させ新トークンを発行する</summary>
    public async Task<(string PlainToken, RefreshToken Entity)> RotateAsync(RefreshToken current, CancellationToken ct = default)
    {
        current.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return await IssueAsync(current.User!, ct);
    }

    public async Task RevokeAsync(string plainToken, CancellationToken ct = default)
    {
        var hash = Hash(plainToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is { RevokedAt: null })
        {
            token.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>ユーザーの全リフレッシュトークンを失効（パスワード変更・アカウント無効化時）</summary>
    public async Task RevokeAllForUserAsync(string userId, CancellationToken ct = default)
    {
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);
    }

    /// <summary>
    /// 保持期間を過ぎたトークンを消す（失効済み・期限切れのレコードが単調増加しないようにする）。
    /// 発行のついでに、そのユーザーの分だけを対象にする
    /// </summary>
    private async Task PurgeExpiredAsync(string userId, CancellationToken ct)
    {
        var threshold = DateTimeOffset.UtcNow.AddHours(-_options.RefreshTokenLifetimeHours * ExpiredRetentionFactor);
        // SQLiteはDateTimeOffsetの比較をSQLへ変換できないため、対象ユーザーの分を読み出してから絞る
        // （1ユーザーが同時に持つトークンは高々数十件）
        var tokens = await db.RefreshTokens.Where(t => t.UserId == userId).ToListAsync(ct);
        var stale = tokens.Where(t => t.ExpiresAt < threshold).ToList();
        if (stale.Count > 0)
        {
            db.RefreshTokens.RemoveRange(stale);
        }
    }

    private static string Hash(string value) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

/// <summary>
/// リフレッシュトークンの検証結果。
/// <paramref name="ReusedByUserId"/> が入っているときは失効済みトークンの再提示（盗用の疑い）。
/// </summary>
public sealed record RefreshTokenValidation(RefreshToken? Token, string? ReusedByUserId)
{
    public bool IsReuse => ReusedByUserId is not null;
}
