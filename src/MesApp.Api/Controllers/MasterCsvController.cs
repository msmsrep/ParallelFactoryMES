using MesApp.Api.Services;
using MesApp.Core.Constants;
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
    /// <summary>アップロード上限（5MB）</summary>
    private const long MaxUploadBytes = 5 * 1024 * 1024;

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
        if (info.UserAdminOnly && !User.IsInRole(MesRoles.SystemAdmin))
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
    [RequestSizeLimit(MaxUploadBytes)]
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

        byte[] bytes;
        if (Request.HasFormContentType)
        {
            var file = Request.Form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
            {
                return BadRequest(new ProblemDetails { Title = "CSVファイルが選択されていません。" });
            }
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }
        else
        {
            using var buffer = new MemoryStream();
            await Request.Body.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }

        if (bytes.Length == 0)
        {
            return BadRequest(new ProblemDetails { Title = "CSVの内容が空です。" });
        }

        var csv = CsvFile.Decode(bytes);
        return await service.ImportAsync(info, csv, dryRun, ct);
    }

    private bool CanWrite(CsvKindInfo kind) =>
        kind.UserAdminOnly
            ? User.IsInRole(MesRoles.SystemAdmin)
            : User.IsInRole(MesRoles.SystemAdmin) || User.IsInRole(MesRoles.ProductionManager);

    /// <summary>Excelでそのまま開けるようUTF-8 BOM付きで返す</summary>
    private FileContentResult CsvFileResult(string csv, string fileName) =>
        File(CsvFile.ToUtf8Bom(csv), "text/csv; charset=utf-8", fileName);
}
