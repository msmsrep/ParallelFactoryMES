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
    public ActionResult<List<CsvKindInfo>> Kinds() =>
        ActualCsvKinds.All.Select(k => k.Info with { WriteRoles = k.WriteRoles }).ToList();

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

    /// <summary>
    /// 複数の実績CSVをまとめたZIPの一括取込（Spec.md 3.8）。ファイル名の昇順に取り込み、
    /// 種別はファイル名の「_」より後ろで決める（同じ種別を番号を変えて複数入れてよい）。
    /// 全ファイルを1つのトランザクションで処理し、どれか1つでもエラーがあれば全ファイルを取り消す。
    /// <para>
    /// ZIPに含まれる<b>全種別の取込権限</b>が要る。マスタCSVが混ざったZIPは受け付けない。
    /// </para>
    /// </summary>
    [HttpPost("bundle")]
    [RequestSizeLimit(CsvBundle.MaxZipBytes)]
    public async Task<ActionResult<CsvBundleImportResult>> ImportBundle(
        [FromQuery] bool dryRun = false, CancellationToken ct = default)
    {
        var bytes = await CsvImport.ReadUploadBytesAsync(Request, ct);
        if (bytes is null)
        {
            return BadRequest(new ProblemDetails { Title = "ZIPファイルが選択されていないか、内容が空です。" });
        }
        if (CsvBundle.ReadZip(bytes, out var zipError) is not { } entries)
        {
            return BadRequest(new ProblemDetails { Title = zipError });
        }

        var unknown = entries.Where(e => ActualCsvKinds.Find(e.Kind) is null).ToList();
        if (unknown.Count > 0)
        {
            var masters = unknown.Where(e => MasterCsvKinds.Find(e.Kind) is not null).ToList();
            return BadRequest(new ProblemDetails
            {
                Title = masters.Count > 0
                    ? $"マスタのCSVが含まれています：{string.Join("、", masters.Select(e => e.FileName))}。マスタはマスタ管理で別のZIPとして取り込んでください。"
                    : $"取込に対応していない実績のCSVが含まれています：{string.Join("、", unknown.Select(e => e.FileName))}" +
                      "（ファイル名は「番号_種別.csv」。例 01_receiving.csv）。",
            });
        }
        var files = entries
            .Select(e => new CsvBundleFile<ActualCsvKind>(e.FileName, ActualCsvKinds.Find(e.Kind)!, e.Text))
            .ToList();
        if (files.Any(f => !MesRoleGroups.IsInGroup(User, f.Kind.WriteRoles)))
        {
            return Forbid();
        }
        return await service.ImportBundleAsync(files, dryRun, User.FindFirstValue(ClaimTypes.NameIdentifier), ct);
    }
}
