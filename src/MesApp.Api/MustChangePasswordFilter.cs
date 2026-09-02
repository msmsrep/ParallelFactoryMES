using MesApp.Core.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MesApp.Api;

/// <summary>
/// パスワード変更が未了のトークンでは、パスワード変更以外のAPIを使わせない（Spec.md 2.2 E・7.4）。
/// <para>
/// 初期管理者はデスクトップ配布で初期パスワードを画面に表示する（<c>IdentitySeeder</c>）ため、
/// 変更の強制が画面側の案内だけだと、そのままのトークンで全APIを操作できてしまう。
/// 判定はアクセストークンの <see cref="MesClaimTypes.MustChangePassword"/> クレームだけで行い、
/// リクエストごとのDB参照はしない。
/// </para>
/// <para>
/// 管理者によるパスワードリセット直後など、既に発行済みのトークンにはこのクレームが付かない。
/// 反映は次回のトークン発行時（ロール変更と同じ扱い）。
/// </para>
/// </summary>
public sealed class MustChangePasswordFilter : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (!context.HttpContext.User.HasClaim(MesClaimTypes.MustChangePassword, "true"))
        {
            return;
        }

        var endpoint = context.HttpContext.GetEndpoint();
        // 未認証で通せるエンドポイント（ログイン・リフレッシュ・ログアウト・初期セットアップ）は対象外
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            return;
        }
        if (endpoint?.Metadata.GetMetadata<AllowPendingPasswordChangeAttribute>() is not null)
        {
            return;
        }

        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "初期パスワードのままです。パスワードを変更してから操作してください。",
        })
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }
}

/// <summary>
/// パスワード変更が未了でも呼べるアクションに付ける
/// （変更そのものと、変更が必要かをクライアントが判断するための自分の情報）。
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AllowPendingPasswordChangeAttribute : Attribute;
