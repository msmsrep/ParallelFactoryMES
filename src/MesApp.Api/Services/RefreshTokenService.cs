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
        await db.SaveChangesAsync(ct);
        return (plain, entity);
    }

    /// <summary>有効なトークンを検証して返す（無効ならnull）</summary>
    public async Task<RefreshToken?> ValidateAsync(string plainToken, CancellationToken ct = default)
    {
        var hash = Hash(plainToken);
        var token = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        return token is { } t && t.IsActive && t.User is { IsActive: true } ? token : null;
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

    private static string Hash(string value) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
