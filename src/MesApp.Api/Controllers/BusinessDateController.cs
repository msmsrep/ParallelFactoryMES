using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MesApp.Api.Controllers;

/// <summary>製造日（業務日付）の現在値（Spec.md 3.9。ダッシュボード等の「当日」の基準）</summary>
[ApiController]
[Route("api/business-date")]
[Authorize]
public class BusinessDateController(IBusinessDateService businessDate) : ControllerBase
{
    [HttpGet]
    public ActionResult<BusinessDateResponse> Get()
        => new BusinessDateResponse(businessDate.Today, businessDate.BoundaryHour);
}
