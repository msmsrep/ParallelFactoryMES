using System.Security.Claims;
using MesApp.Core.Contracts.Integration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MesApp.Api.Controllers.Integration;

/// <summary>
/// 連携API の疎通確認（Spec.md 7.4 連携APIの認証。docs-dev/IntegrationApiTasks.md INT-01）。
/// キーが通るか・誰として扱われるか（ロール）を、業務データに触れずに確かめるための口
/// </summary>
[ApiController]
[Route("api/integration/v1/whoami")]
[Authorize(AuthenticationSchemes = ApiKeyAuthentication.IntegrationSchemes)]
public class WhoAmIController : ControllerBase
{
    [HttpGet]
    public ActionResult<IntegrationWhoAmIResponse> Get(CancellationToken ct)
    {
        var apiKeyName = User.FindFirstValue(MesClaimTypes.ApiKeyName);
        return new IntegrationWhoAmIResponse(
            User.Identity?.Name ?? string.Empty,
            User.FindFirstValue(MesClaimTypes.DisplayName) ?? string.Empty,
            [.. User.FindAll(ClaimTypes.Role).Select(c => c.Value).Order(StringComparer.Ordinal)],
            apiKeyName is null ? "Bearer" : ApiKeyAuthentication.Scheme,
            apiKeyName);
    }
}
