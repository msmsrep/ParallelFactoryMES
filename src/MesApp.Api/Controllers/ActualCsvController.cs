using MesApp.Api.Localization;
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
public class ActualCsvController(ActualCsvService service, CsvIntake intake) : ControllerBase
{
    /// <summary>CSV取込に対応する実績種別と列定義</summary>
    [HttpGet("kinds")]
    public ActionResult<List<CsvKindInfo>> Kinds() =>
        ActualCsvKinds.All.Select(k => CsvImport.Localize(k.Info) with { WriteRoles = k.WriteRoles }).ToList();

    /// <summary>ヘッダーのみのテンプレートCSV</summary>
    [HttpGet("{kind}/template")]
    public IActionResult Template(string kind)
    {
        var info = ActualCsvKinds.Find(kind);
        if (info is null)
        {
            return this.NotFoundProblem(ApiText.T("CSV取込に対応していない実績です：{0}", kind));
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
            return this.NotFoundProblem(ApiText.T("CSV取込に対応していない実績です：{0}", kind));
        }
        if (!MesRoleGroups.IsInGroup(User, info.WriteRoles))
        {
            return Forbid();
        }

        var csv = await CsvImport.ReadUploadAsync(Request, ct);
        if (csv is null)
        {
            return this.BadRequestProblem(ApiText.T("CSVファイルが選択されていないか、内容が空です。"));
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
            return this.BadRequestProblem(ApiText.T("ZIPファイルが選択されていないか、内容が空です。"));
        }
        if (CsvBundle.ReadZip(bytes, out var zipError) is not { } entries)
        {
            return this.BadRequestProblem(zipError);
        }

        // 種別の解決と振り分け（マスタの混入の拒否）は画面・フォルダ連携・連携APIで共通
        if (CsvIntake.ResolveBundle(entries, CsvIntakeArea.Actual, out var error) is not { } bundle)
        {
            return this.BadRequestProblem(error);
        }
        if (CsvIntake.FirstDenied(User, bundle) is not null)
        {
            return Forbid();
        }
        return await intake.ImportBundleAsync(bundle, dryRun, User.FindFirstValue(ClaimTypes.NameIdentifier), ct);
    }
}
