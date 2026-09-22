using MesApp.Api.Localization;
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
    public ActionResult<List<CsvKindInfo>> Kinds() => MasterCsvKinds.All.Select(CsvImport.Localize).ToList();

    /// <summary>登録済みマスタのCSV出力</summary>
    [HttpGet("{kind}")]
    public async Task<IActionResult> Export(
        string kind, [FromQuery] bool includeInactive = true, CancellationToken ct = default)
    {
        var info = MasterCsvKinds.Find(kind);
        if (info is null)
        {
            return this.NotFoundProblem(ApiText.T("CSV出力に対応していないマスタです：{0}", kind));
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
            return this.NotFoundProblem(ApiText.T("CSV出力に対応していないマスタです：{0}", kind));
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
            return this.NotFoundProblem(ApiText.T("CSV取込に対応していないマスタです：{0}", kind));
        }
        if (!CanWrite(info))
        {
            return Forbid();
        }

        var csv = await CsvImport.ReadUploadAsync(Request, ct);
        if (csv is null)
        {
            return this.BadRequestProblem(ApiText.T("CSVファイルが選択されていないか、内容が空です。"));
        }
        return await service.ImportAsync(info, csv, dryRun, ct);
    }

    /// <summary>
    /// 複数のマスタCSVをまとめたZIPの一括取込（Spec.md 3.8）。ファイル名の昇順に取り込み、
    /// 種別はファイル名の「_」より後ろで決める。全ファイルを1つのトランザクションで処理し、
    /// どれか1つでもエラーがあれば全ファイルを取り消す。dryRun=true で検証のみ。
    /// <para>
    /// ZIPに含まれる<b>全種別の取込権限</b>が要る（1種別でも権限が無ければ何も取り込まず403）。
    /// 実績CSVが混ざったZIPは受け付けない（実績は <c>api/actuals/csv/bundle</c> で別に取り込む）。
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
            return this.BadRequestProblem(ApiText.T("ZIPファイルが選択されていないか、内容が空です。"));
        }
        if (CsvBundle.ReadZip(bytes, out var zipError) is not { } entries)
        {
            return this.BadRequestProblem(zipError);
        }

        var unknown = entries.Where(e => MasterCsvKinds.Find(e.Kind) is null).ToList();
        if (unknown.Count > 0)
        {
            var actual = unknown.Where(e => ActualCsvKinds.Find(e.Kind) is not null).ToList();
            return this.BadRequestProblem(
                actual.Count > 0
                    ? ApiText.T("実績のCSVが含まれています：{0}。実績は実績CSV取込で別のZIPとして取り込んでください。",
                        string.Join(ApiText.T("、"), actual.Select(e => e.FileName)))
                    : ApiText.T("取込に対応していないマスタのCSVが含まれています：{0}（ファイル名は「番号_種別.csv」。例 01_work-centers.csv）。",
                        string.Join(ApiText.T("、"), unknown.Select(e => e.FileName))));
        }
        var files = entries
            .Select(e => new CsvBundleFile<CsvKindInfo>(e.FileName, MasterCsvKinds.Find(e.Kind)!, e.Text))
            .ToList();
        if (files.Any(f => !CanWrite(f.Kind)))
        {
            return Forbid();
        }
        return await service.ImportBundleAsync(files, dryRun, ct);
    }

    /// <summary>
    /// 登録済みの全マスタを1つのZIPで出力する（Spec.md 3.8）。ファイル名は取り込む順の番号付き
    /// （<see cref="MasterCsvKinds.ImportOrder"/>）で、そのまま一括取込に使える。
    /// ユーザー系など参照を絞っている種別は、その権限が無ければ含めない。
    /// </summary>
    [HttpGet("bundle")]
    public async Task<IActionResult> ExportBundle(
        [FromQuery] bool includeInactive = true, CancellationToken ct = default)
    {
        var files = new List<(string FileName, string Csv)>();
        for (var i = 0; i < MasterCsvKinds.ImportOrder.Count; i++)
        {
            var info = MasterCsvKinds.Find(MasterCsvKinds.ImportOrder[i])!;
            if (info.UserAdminOnly && !MesRoleGroups.IsInGroup(User, MesRoleGroups.UserAdmin))
            {
                continue;
            }
            files.Add(($"{i + 1:00}_{info.Kind}.csv", await service.ExportAsync(info, includeInactive, ct)));
        }
        return File(CsvBundle.ToZip(files), "application/zip", $"masters_{DateTime.Now:yyyyMMdd}.zip");
    }

    /// <summary>
    /// ユーザー系とスキル・資格はユーザー管理権限、それ以外はマスタ更新権限
    /// （組み合わせはRoleGroupsが持つ）。単票のAPIと同じ権限になるようkind側に持たせる。
    /// 単票APIがマスタ更新権限以外で絞る種別（生産計画）は、kind の WriteRoles をそのまま使う
    /// </summary>
    private bool CanWrite(CsvKindInfo kind) =>
        MesRoleGroups.IsInGroup(User, kind.WriteRoles ?? (kind.UserAdminOnly || kind.UserAdminWrite
            ? MesRoleGroups.UserAdmin
            : MesRoleGroups.MasterWrite));

    /// <summary>Excelでそのまま開けるようUTF-8 BOM付きで返す</summary>
    private FileContentResult CsvFileResult(string csv, string fileName) =>
        File(CsvFile.ToUtf8Bom(csv), "text/csv; charset=utf-8", fileName);
}
