using MesApp.Core.Contracts.Masters;

namespace MesApp.Api.Services;

/// <summary>
/// CSV取込の共通の入口と出口（マスタCSV・実績CSVで共有）。
/// 列・行数の事前検証と結果の組み立てを1か所に置き、両者でエラーの出し方を揃える。
/// </summary>
public static class CsvImport
{
    /// <summary>取込可能な最大データ行数</summary>
    public const int MaxRows = 20000;

    /// <summary>アップロード上限（5MB）</summary>
    public const long MaxUploadBytes = 5 * 1024 * 1024;

    /// <summary>応答に含めるエラーの最大件数</summary>
    private const int MaxReportedErrors = 200;

    /// <summary>
    /// CSVを表に読み込み、必須列・データ行の有無・行数上限を検証する。
    /// 取り込める状態でなければエラーを積んで null を返す。
    /// </summary>
    public static CsvTable? Prepare(CsvKindInfo kind, string csvText, List<CsvImportError> errors)
    {
        var table = CsvTable.Create(CsvFile.Parse(csvText));
        if (table is null)
        {
            errors.Add(new CsvImportError(1, "CSVが空です。1行目にヘッダー行が必要です。"));
            return null;
        }

        var missing = kind.Columns.Where(c => c.Required && !table.HasColumn(c.Name)).Select(c => c.Name).ToList();
        if (missing.Count > 0)
        {
            errors.Add(new CsvImportError(table.Header.Line,
                $"必須の列がありません：{string.Join(", ", missing)}。テンプレートCSVの1行目をそのまま使ってください。"));
            return null;
        }
        if (table.Rows.Count == 0)
        {
            errors.Add(new CsvImportError(table.Header.Line, "データ行がありません。"));
            return null;
        }
        if (table.Rows.Count > MaxRows)
        {
            errors.Add(new CsvImportError(table.Header.Line,
                $"1回に取り込めるのは{MaxRows}行までです（{table.Rows.Count}行）。ファイルを分割してください。"));
            return null;
        }
        return table;
    }

    /// <summary>取込結果を組み立てる（失敗時は件数を0にし、エラーは上限件数で打ち切る）</summary>
    public static CsvImportResult Result(
        CsvKindInfo kind, int dataRows, int created, int updated, bool dryRun, List<CsvImportError> errors)
    {
        var succeeded = errors.Count == 0;
        if (errors.Count > MaxReportedErrors)
        {
            var omitted = errors.Count - MaxReportedErrors;
            errors = [.. errors.Take(MaxReportedErrors), new CsvImportError(0, $"他 {omitted} 件のエラーは省略しました。")];
        }
        return new CsvImportResult(
            kind.Kind, dataRows,
            succeeded ? created : 0,
            succeeded ? updated : 0,
            dryRun, succeeded, errors);
    }

    /// <summary>
    /// アップロードされたCSVを文字列にする（multipart/form-data のファイル、またはボディそのもの）。
    /// 空なら null を返す。
    /// </summary>
    public static async Task<string?> ReadUploadAsync(HttpRequest request, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        if (request.HasFormContentType)
        {
            var file = request.Form.Files.FirstOrDefault();
            if (file is null)
            {
                return null;
            }
            await file.CopyToAsync(buffer, ct);
        }
        else
        {
            await request.Body.CopyToAsync(buffer, ct);
        }
        return buffer.Length == 0 ? null : CsvFile.Decode(buffer.ToArray());
    }
}
