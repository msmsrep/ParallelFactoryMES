using MesApp.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api;

/// <summary>
/// 業務上起こりうる例外を日本語の <see cref="ProblemDetails"/> へ変換する。
/// <para>
/// 在庫の楽観的同時実行制御（<c>InventoryStock.ConcurrencyStamp</c>。Spec.md 5.3）や
/// 採番の同時発行による一意制約違反は、正常な運用でも起こりうる「競合」であって障害ではない。
/// 変換しないと本文のない 500 になり、クライアント（<c>AuthService.ReadProblemTitleAsync</c>）が
/// 理由を表示できないため、ここで一括して 409 / 400 に変換する。
/// </para>
/// <para>
/// 例外フィルタ（MVCの内側）にしているのは、開発環境の開発者例外ページに横取りされず、
/// 環境によらず同じ応答を返すため。ここで拾わない想定外の例外は、
/// <c>UseExceptionHandler</c> が ProblemDetails 形式の 500 にして返す。
/// </para>
/// </summary>
public class MesAppExceptionFilter(ILogger<MesAppExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var (status, title) = context.Exception switch
        {
            // 楽観的同時実行制御での競合（別の操作が先に同じ在庫を更新した）
            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict,
                "他の操作と競合したため保存できませんでした。最新の内容を読み込み直してから、もう一度実行してください。"),

            // 一意制約違反など、DBが書き込みを拒否した場合（同時採番の衝突が主な原因）
            DbUpdateException => (
                StatusCodes.Status409Conflict,
                "他の操作と競合したため保存できませんでした（番号やコードの重複、または関連データの不整合）。"
                + "内容を確認して、もう一度実行してください。"),

            // 在庫不足など。各コントローラでも捕捉しているが、取りこぼしを500にしないための保険
            InventoryException inventory => (StatusCodes.Status400BadRequest, inventory.Message),

            _ => (0, string.Empty),
        };

        if (status == 0)
        {
            return;
        }

        logger.LogWarning(context.Exception, "業務例外を {Status} に変換しました（{Method} {Path}）",
            status, context.HttpContext.Request.Method, context.HttpContext.Request.Path);

        context.Result = new ObjectResult(new ProblemDetails { Status = status, Title = title })
        {
            StatusCode = status,
        };
        context.ExceptionHandled = true;
    }
}
