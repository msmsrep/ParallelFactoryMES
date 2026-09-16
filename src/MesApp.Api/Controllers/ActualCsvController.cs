using System.Security.Claims;
using MesApp.Api.Services;
using MesApp.Core.Contracts.Masters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MesApp.Api.Controllers;

/// <summary>
/// 実績（取引データ）のCSV一括取込（Spec.md 3.8。受入 D-10-10-02 ほか）。
/// 取込に必要な権限は種別ごとに単票APIと同じロールグループ。種別と列定義の参照は認証済みユーザー全員。
/// 登録済み実績の出力は各業務画面・帳票で行うため、ここではテンプレートだけを返す。
/// </summary>
[ApiController]
[Route("api/actuals/csv")]
[Authorize]
public class ActualCsvController(ActualCsvService service) : ControllerBase
{
    /// <summary>CSV取込に対応する実績種別と列定義</summary>
    [HttpGet("kinds")]
    public ActionResult<List<CsvKindInfo>> Kinds() => ActualCsvKinds.All.Select(k => k.Info).ToList();

    /// <summary>ヘッダーのみのテンプレートCSV</summary>
    [HttpGet("{kind}/template")]
    public IActionResult Template(string kind)
    {
        var info = ActualCsvKinds.Find(kind);
        if (info is null)
        {
            return NotFound(new ProblemDetails { Title = $"CSV取込に対応していない実績です：{kind}" });
        }
        var csv = CsvFile.Format(info.Info.Columns.Select(c => c.Name), []);
        return File(CsvFile.ToUtf8Bom(csv), "text/csv; charset=utf-8", $"{info.Info.Kind}_template.csv");
    }

    /// <summary>
    /// 実績CSVの一括取込（multipart/form-data のファイル、またはCSVそのものをボディに送る）。
    /// dryRun=true で検証のみ（DBには反映しない）。
    /// </summary>
    [HttpPost("{kind}")]
    [RequestSizeLimit(CsvImport.MaxUploadBytes)]
    public async Task<ActionResult<CsvImportResult>> Import(
        string kind, [FromQuery] bool dryRun = false, CancellationToken ct = default)
    {
        var info = ActualCsvKinds.Find(kind);
        if (info is null)
        {
            return NotFound(new ProblemDetails { Title = $"CSV取込に対応していない実績です：{kind}" });
        }
        if (!MesRoleGroups.IsInGroup(User, info.WriteRoles))
        {
            return Forbid();
        }

        var csv = await CsvImport.ReadUploadAsync(Request, ct);
        if (csv is null)
        {
            return BadRequest(new ProblemDetails { Title = "CSVファイルが選択されていないか、内容が空です。" });
        }
        return await service.ImportAsync(info, csv, dryRun, User.FindFirstValue(ClaimTypes.NameIdentifier), ct);
    }
}
