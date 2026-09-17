using Microsoft.AspNetCore.Mvc;

namespace MesApp.Api;

/// <summary>
/// 日本語メッセージを Title に載せた ProblemDetails 応答を返す。
/// 応答の形（Title のみ・Status は出力時に補完）を全コントローラで揃えるための窓口。
/// </summary>
public static class ProblemResultExtensions
{
    public static BadRequestObjectResult BadRequestProblem(this ControllerBase controller, string? title) =>
        controller.BadRequest(new ProblemDetails { Title = title });

    public static ConflictObjectResult ConflictProblem(this ControllerBase controller, string? title) =>
        controller.Conflict(new ProblemDetails { Title = title });

    public static NotFoundObjectResult NotFoundProblem(this ControllerBase controller, string? title) =>
        controller.NotFound(new ProblemDetails { Title = title });

    public static UnauthorizedObjectResult UnauthorizedProblem(this ControllerBase controller, string? title) =>
        controller.Unauthorized(new ProblemDetails { Title = title });
}
