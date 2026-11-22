using System.Security.Claims;
using System.Text.Json;
using MesApp.Api.Localization;
using MesApp.Api.Services;
using MesApp.Core.Contracts.Integration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace MesApp.Api.Controllers.Integration;

/// <summary>
/// 連携API の CSV 取込・出力（Spec.md 3.8 連携API。docs-dev/IntegrationApiTasks.md INT-02。A-20-10-01 ほか）。
/// <para>
/// 画面のマスタCSV・実績CSVと<b>同じ種別・同じ列・同じ取込処理・同じ権限</b>（<see cref="CsvIntake"/>）。違いは次のとおり。
/// ①APIキーで呼べる。②取込に失敗したら 422（本文は取込結果）を返す（画面用は 200 で <c>Succeeded=false</c>）。
/// ③<c>Idempotency-Key</c> ヘッダを付ければ、同じ要求の送り直しで二重に取り込まない（検証のみ dryRun は対象外）
/// </para>
/// </summary>
[ApiController]
[Route("api/integration/v1")]
[Authorize(AuthenticationSchemes = ApiKeyAuthentication.IntegrationSchemes)]
public class IntegrationCsvController(
    CsvIntake intake,
    MasterCsvService masters,
    IdempotencyService idempotency,
    IOptions<JsonOptions> jsonOptions) : ControllerBase
{
    /// <summary>取り込める種別と列定義（表示名と説明は Accept-Language の言語。列キーは言語によらない）</summary>
    [HttpGet("csv-kinds")]
    public ActionResult<IntegrationCsvKindsResponse> Kinds(CancellationToken ct) => new IntegrationCsvKindsResponse(
        [.. MasterCsvKinds.All.Select(k => CsvImport.Localize(k) with { WriteRoles = MasterCsvKinds.WriteRolesOf(k) })],
        [.. ActualCsvKinds.All.Select(k => CsvImport.Localize(k.Info) with { WriteRoles = k.WriteRoles })]);

    /// <summary>登録済みマスタのCSV出力（画面のマスタCSV出力と同じ内容。UTF-8 BOM 付き）</summary>
    [HttpGet("masters/{kind}")]
    public async Task<IActionResult> ExportMaster(string kind, [FromQuery] bool includeInactive = true, CancellationToken ct = default)
    {
        if (MasterCsvKinds.Find(kind) is not { } info)
        {
            return this.NotFoundProblem(ApiText.T("CSV出力に対応していないマスタです：{0}", kind));
        }
        if (info.UserAdminOnly && !MesRoleGroups.IsInGroup(User, MesRoleGroups.UserAdmin))
        {
            return Forbid();
        }
        var csv = await masters.ExportAsync(info, includeInactive, ct);
        return File(CsvFile.ToUtf8Bom(csv), "text/csv; charset=utf-8", $"{info.Kind}.csv");
    }

    /// <summary>マスタCSVの取込（本文に CSV そのもの、または multipart のファイル）</summary>
    [HttpPost("masters/{kind}")]
    [RequestSizeLimit(CsvImport.MaxUploadBytes)]
    public Task<IActionResult> ImportMaster(string kind, [FromQuery] bool dryRun = false, CancellationToken ct = default) =>
        ImportOneAsync(kind, CsvIntakeArea.Master, dryRun, ct);

    /// <summary>実績CSVの取込（常に新規登録。送り直しに備えるなら Idempotency-Key を付ける）</summary>
    [HttpPost("actuals/{kind}")]
    [RequestSizeLimit(CsvImport.MaxUploadBytes)]
    public Task<IActionResult> ImportActual(string kind, [FromQuery] bool dryRun = false, CancellationToken ct = default) =>
        ImportOneAsync(kind, CsvIntakeArea.Actual, dryRun, ct);

    /// <summary>マスタCSVをまとめたZIPの取込（ファイル名の順。1ファイルでもエラーなら全ファイルを取り消す）</summary>
    [HttpPost("masters/bundle")]
    [RequestSizeLimit(CsvBundle.MaxZipBytes)]
    public Task<IActionResult> ImportMasterBundle([FromQuery] bool dryRun = false, CancellationToken ct = default) =>
        ImportBundleAsync(CsvIntakeArea.Master, dryRun, ct);

    /// <summary>実績CSVをまとめたZIPの取込（ファイル名の順。1ファイルでもエラーなら全ファイルを取り消す）</summary>
    [HttpPost("actuals/bundle")]
    [RequestSizeLimit(CsvBundle.MaxZipBytes)]
    public Task<IActionResult> ImportActualBundle([FromQuery] bool dryRun = false, CancellationToken ct = default) =>
        ImportBundleAsync(CsvIntakeArea.Actual, dryRun, ct);

    private async Task<IActionResult> ImportOneAsync(string kindName, CsvIntakeArea area, bool dryRun, CancellationToken ct)
    {
        if (CsvIntake.Find(kindName, area) is not { } kind)
        {
            return this.NotFoundProblem(area == CsvIntakeArea.Master
                ? ApiText.T("CSV取込に対応していないマスタです：{0}", kindName)
                : ApiText.T("CSV取込に対応していない実績です：{0}", kindName));
        }
        if (!CsvIntake.CanWrite(User, kind))
        {
            return Forbid();
        }
        if (await CsvImport.ReadUploadBytesAsync(Request, ct) is not { } bytes)
        {
            return this.BadRequestProblem(ApiText.T("CSVファイルが選択されていないか、内容が空です。"));
        }
        return await RunAsync(bytes, dryRun, async () =>
        {
            var result = await intake.ImportAsync(kind, CsvFile.Decode(bytes), dryRun, UserId, ct);
            return (result.Succeeded ? StatusCodes.Status200OK : StatusCodes.Status422UnprocessableEntity, result);
        }, ct);
    }

    private async Task<IActionResult> ImportBundleAsync(CsvIntakeArea area, bool dryRun, CancellationToken ct)
    {
        if (await CsvImport.ReadUploadBytesAsync(Request, ct) is not { } bytes)
        {
            return this.BadRequestProblem(ApiText.T("ZIPファイルが選択されていないか、内容が空です。"));
        }
        if (CsvBundle.ReadZip(bytes, out var zipError) is not { } entries)
        {
            return this.BadRequestProblem(zipError);
        }
        if (CsvIntake.ResolveBundle(entries, area, out var error) is not { } bundle)
        {
            return this.BadRequestProblem(error);
        }
        if (CsvIntake.FirstDenied(User, bundle) is not null)
        {
            return Forbid();
        }
        return await RunAsync(bytes, dryRun, async () =>
        {
            var result = await intake.ImportBundleAsync(bundle, dryRun, UserId, ct);
            return (result.Succeeded ? StatusCodes.Status200OK : StatusCodes.Status422UnprocessableEntity, result);
        }, ct);
    }

    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>
    /// 取込を <c>Idempotency-Key</c> の下で行う。キーが無い・検証のみなら、そのまま取り込む。
    /// 応答はエラー（422）も含めて残す——取込の失敗は同じ要求を送り直しても同じ結果になるため
    /// </summary>
    private async Task<IActionResult> RunAsync(byte[] body, bool dryRun, Func<Task<(int Status, object Body)>> import, CancellationToken ct)
    {
        var key = Request.Headers[IdempotencyService.HeaderName].ToString().Trim();
        if (dryRun || key.Length == 0)
        {
            var (status, result) = await import();
            return StatusCode(status, result);
        }
        if (key.Length > IdempotencyService.MaxKeyLength)
        {
            return this.BadRequestProblem(ApiText.T("Idempotency-Key は{0}文字以内にしてください。", IdempotencyService.MaxKeyLength));
        }

        var caller = User.FindFirstValue(MesClaimTypes.ApiKeyId) is { } keyId ? $"key:{keyId}" : $"user:{UserId}";
        switch (await idempotency.StartAsync(caller, key, IdempotencyService.HashRequest(Request, body), ct))
        {
            case IdempotencyStart.Replay replay:
                Response.Headers["Idempotent-Replayed"] = "true";
                return new ContentResult { StatusCode = replay.StatusCode, Content = replay.Body, ContentType = "application/json; charset=utf-8" };
            case IdempotencyStart.Conflict { InProgress: true }:
                return this.ConflictProblem(ApiText.T("同じ Idempotency-Key の要求を処理しています。終わってから送り直してください。"));
            case IdempotencyStart.Conflict:
                return this.ConflictProblem(ApiText.T("この Idempotency-Key は、内容の違う要求で既に使われています。新しい要求には新しいキーを付けてください。"));
            case IdempotencyStart.Proceed proceed:
                try
                {
                    var (status, result) = await import();
                    await idempotency.CompleteAsync(proceed.Id, status,
                        JsonSerializer.Serialize(result, result.GetType(), jsonOptions.Value.JsonSerializerOptions), CancellationToken.None);
                    return StatusCode(status, result);
                }
                catch
                {
                    await idempotency.AbandonAsync(proceed.Id);
                    throw;
                }
            default:
                throw new InvalidOperationException();
        }
    }
}
