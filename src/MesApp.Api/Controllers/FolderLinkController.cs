using MesApp.Api.Localization;
using MesApp.Api.Services.FolderLink;
using MesApp.Core.Contracts.Integration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MesApp.Api.Controllers;

/// <summary>
/// フォルダ連携（CSVの自動取込）の状態と手動の走査（Spec.md 3.8 フォルダ連携。A-20-10-01 ほか）。
/// サーバーのフォルダの場所と取込の利用者を返すので、参照もシステム管理者だけにする
/// </summary>
[ApiController]
[Route("api/folder-link")]
[Authorize]
public class FolderLinkController(FolderLinkService folderLink) : ControllerBase
{
    /// <summary>設定と最近処理したファイル</summary>
    [HttpGet("status")]
    [Authorize(Roles = MesRoleGroups.UserAdmin)]
    public ActionResult<FolderLinkStatusResponse> Status() => folderLink.Status();

    /// <summary>次の間隔を待たずに inbox を走査し、走査後の状態を返す</summary>
    [HttpPost("scan")]
    [Authorize(Roles = MesRoleGroups.UserAdmin)]
    public async Task<ActionResult<FolderLinkStatusResponse>> Scan(CancellationToken ct)
    {
        if (!folderLink.Enabled)
        {
            return this.BadRequestProblem(ApiText.T("フォルダ連携は設定されていません（FolderLink:Root）。"));
        }
        await folderLink.ScanAsync(ct);
        return folderLink.Status();
    }
}
