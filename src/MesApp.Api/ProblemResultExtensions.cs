using MesApp.Api.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace MesApp.Api;

/// <summary>
/// 日本語メッセージを Title に載せた ProblemDetails 応答を返す。
/// 応答の形（Title のみ・Status は出力時に補完）を全コントローラで揃えるための窓口。
/// <para>
/// Title は要求の <c>Accept-Language</c> に合わせてここで訳す（Spec.md 7.9）。呼び出し側は日本語の原文を渡す。
/// 値を埋め込む文言は補間文字列にせず、<c>{0}</c> の書式と <paramref name="args"/> で渡す
/// （補間すると値ごとに原文が変わり、訳のキーに当たらない）。
/// </para>
/// </summary>
public static class ProblemResultExtensions
{
    public static BadRequestObjectResult BadRequestProblem(
        this ControllerBase controller, string? title, params object[] args) =>
        controller.BadRequest(new ProblemDetails { Title = Localize(controller, title, args) });

    public static ConflictObjectResult ConflictProblem(
        this ControllerBase controller, string? title, params object[] args) =>
        controller.Conflict(new ProblemDetails { Title = Localize(controller, title, args) });

    public static NotFoundObjectResult NotFoundProblem(
        this ControllerBase controller, string? title, params object[] args) =>
        controller.NotFound(new ProblemDetails { Title = Localize(controller, title, args) });

    public static UnauthorizedObjectResult UnauthorizedProblem(
        this ControllerBase controller, string? title, params object[] args) =>
        controller.Unauthorized(new ProblemDetails { Title = Localize(controller, title, args) });

    /// <summary>原文を要求の表示言語へ訳す。訳が無ければ原文（書式を適用したもの）を返す</summary>
    private static string? Localize(ControllerBase controller, string? title, object[] args)
    {
        if (title is null)
        {
            return null;
        }
        var localizer = controller.HttpContext?.RequestServices.GetService<IStringLocalizer<ApiText>>();
        if (localizer is null)
        {
            return args.Length == 0 ? title : string.Format(title, args);
        }
        return args.Length == 0 ? localizer[title] : localizer[title, args];
    }
}
