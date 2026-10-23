using System.Security.Claims;
using MesApp.Core.Constants;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api;

/// <summary>
/// アクセストークンの即時失効（Spec.md 7.4）。署名・有効期限の検証が通ったあとに、
/// ユーザーが有効か、トークンのセキュリティスタンプがDBの値と一致するかを照合する。
/// </summary>
/// <remarks>
/// 署名と有効期限だけでは、無効化・ロール変更・パスワードリセットをしても発行済みのトークンが
/// 有効期限（既定60分）まで使えてしまう。リクエストごとに主キー検索が1回増えるが、
/// キャッシュは挟まない（挟むとキャッシュが切れるまで無効化が効かない）。
/// </remarks>
public static class AccessTokenValidation
{
    /// <summary>JwtBearer の OnTokenValidated に渡す</summary>
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = principal?.FindFirstValue(MesClaimTypes.SecurityStamp);
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(stamp))
        {
            // スタンプを載せる前に発行されたトークンも弾く（クライアントはリフレッシュで取り直す）
            context.Fail("トークンにユーザーまたはセキュリティスタンプがありません。");
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<MesAppDbContext>();
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.IsActive, u.SecurityStamp })
            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);
        if (user is null || !user.IsActive || user.SecurityStamp != stamp)
        {
            context.Fail("ユーザーが無効化されたか、権限・パスワードが変更されたためトークンは失効しています。");
        }
    }
}
