using MesApp.Api.Services;
using MesApp.Core.Contracts.Masters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MesApp.Api.Controllers;

/// <summary>
/// マスタのCSV一括入出力（Spec.md 3.1）。
/// 出力は参照権限（認証済みユーザー）、取込はマスタ更新権限。ユーザー系はシステム管理者専用。
/// </summary>
[ApiController]
[Route("api/masters/csv")]
[Authorize]
public class MasterCsvController(MasterCsvService service) : ControllerBase
{
    /// <summary>CSV入出力に対応するマスタ種別と列定義</summary>
    [HttpGet("kinds")]
    public ActionResult<List<CsvKindInfo>> Kinds() => MasterCsvKinds.All;

    /// <summary>登録済みマスタのCSV出力</summary>
    [HttpGet("{kind}")]
    public async Task<IActionResult> Export(
        string kind, [FromQuery] bool includeInactive = true, CancellationToken ct = default)
    {
        var info = MasterCsvKinds.Find(kind);
        if (info is null)
        {
            return NotFound(new ProblemDetails { Title = $"CSV出力に対応していないマスタです：{kind}" });
        }
        if (info.UserAdminOnly && !MesRoleGroups.IsInGroup(User, MesRoleGroups.UserAdmin))
        {
            return Forbid();
        }

        var csv = await service.ExportAsync(info, includeInactive, ct);
        return CsvFileResult(csv, $"{info.Kind}_{DateTime.Now:yyyyMMdd}.csv");
    }

    /// <summary>ヘッダーのみのテンプレートCSV</summary>
    [HttpGet("{kind}/template")]
    public IActionResult Template(string kind)
    {
        var info = MasterCsvKinds.Find(kind);
        if (info is null)
        {
            return NotFound(new ProblemDetails { Title = $"CSV出力に対応していないマスタです：{kind}" });
        }
        return CsvFileResult(MasterCsvService.Template(info), $"{info.Kind}_template.csv");
    }

    /// <summary>
    /// CSVの一括取込（multipart/form-data のファイル、またはCSVそのものをボディに送る）。
    /// dryRun=true で検証のみ（DBには反映しない）。
    /// </summary>
    [HttpPost("{kind}")]
    [RequestSizeLimit(CsvImport.MaxUploadBytes)]
    public async Task<ActionResult<CsvImportResult>> Import(
        string kind, [FromQuery] bool dryRun = false, CancellationToken ct = default)
    {
        var info = MasterCsvKinds.Find(kind);
        if (info is null)
        {
            return NotFound(new ProblemDetails { Title = $"CSV取込に対応していないマスタです：{kind}" });
        }
        if (!CanWrite(info))
        {
            return Forbid();
        }

        var csv = await CsvImport.ReadUploadAsync(Request, ct);
        if (csv is null)
        {
            return BadRequest(new ProblemDetails { Title = "CSVファイルが選択されていないか、内容が空です。" });
        }
        return await service.ImportAsync(info, csv, dryRun, ct);
    }

    /// <summary>
    /// ユーザー系とスキル・資格はユーザー管理権限、それ以外はマスタ更新権限
    /// （組み合わせはRoleGroupsが持つ）。単票のAPIと同じ権限になるようkind側に持たせる
    /// </summary>
    private bool CanWrite(CsvKindInfo kind) =>
        MesRoleGroups.IsInGroup(User, kind.UserAdminOnly || kind.UserAdminWrite
            ? MesRoleGroups.UserAdmin
            : MesRoleGroups.MasterWrite);

    /// <summary>Excelでそのまま開けるようUTF-8 BOM付きで返す</summary>
    private FileContentResult CsvFileResult(string csv, string fileName) =>
        File(CsvFile.ToUtf8Bom(csv), "text/csv; charset=utf-8", fileName);
}
