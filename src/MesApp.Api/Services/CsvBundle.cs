using System.IO.Compression;
using MesApp.Core.Contracts.Masters;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>ZIPから取り出したCSV1ファイル（Kind はファイル名から決めた種別）</summary>
public sealed record CsvBundleEntry(string FileName, string Kind, string Text);

/// <summary>取込対象として解決済みの1ファイル（種別の定義まで引いたもの）</summary>
public sealed record CsvBundleFile<TKind>(string FileName, TKind Kind, string Text);

/// <summary>1ファイル分の取込結果（件数）。保存まで済ませて返す</summary>
public sealed record CsvFileImportCount(int Created, int Updated);

/// <summary>
/// 複数のCSVをまとめたZIPの一括取込・一括出力（Spec.md 3.8）。マスタCSVと実績CSVで共有する。
/// <para>
/// <b>ファイル名の昇順に取り込み、種別はファイル名の最初の「_」より後ろで決める</b>
/// （<c>01_work-centers.csv</c> → <c>work-centers</c>）。番号は取り込む順番の指定で、
/// 同じ種別を番号を変えて複数回入れてもよい（実績の <c>05_consumptions.csv</c> と <c>07_consumptions.csv</c>）。
/// </para>
/// <para>
/// <b>全ファイルを1つのトランザクションで処理する。</b>後のファイルは前のファイルの登録結果を参照するため
/// ファイルごとに保存はするが、どれか1つでもエラーがあれば全ファイルを取り消す。
/// 途中まで反映された状態が残ると、どこから取り込み直せばよいか分からなくなるため。
/// </para>
/// </summary>
public static class CsvBundle
{
    /// <summary>アップロードできるZIPの上限（20MB）</summary>
    public const long MaxZipBytes = 20 * 1024 * 1024;

    /// <summary>ZIPに入れられるCSVの数</summary>
    private const int MaxEntries = 200;

    /// <summary>展開後の合計サイズの上限（ZIP爆弾対策。圧縮率の高いZIPで展開時にメモリを食い潰されないよう、読みながら数える）</summary>
    private const long MaxTotalUncompressedBytes = 50 * 1024 * 1024;

    /// <summary>
    /// ZIPからCSVを取り出し、ファイル名の昇順に並べる。CSV以外（README.md など）とフォルダは無視し、
    /// フォルダ階層はファイル名だけを見る。取り込めないZIPなら error に理由を入れて null を返す。
    /// </summary>
    public static List<CsvBundleEntry>? ReadZip(byte[] bytes, out string? error)
    {
        error = null;
        var entries = new List<CsvBundleEntry>();
        try
        {
            using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                var fileName = Path.GetFileName(entry.FullName);
                if (fileName.Length == 0
                    || entry.FullName.StartsWith("__MACOSX/", StringComparison.Ordinal)
                    || !fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (entries.Count >= MaxEntries)
                {
                    error = $"ZIPに入れられるCSVは{MaxEntries}ファイルまでです。";
                    return null;
                }
                if (entries.Any(e => string.Equals(e.FileName, fileName, StringComparison.OrdinalIgnoreCase)))
                {
                    error = $"同じ名前のCSV '{fileName}' が複数あります（フォルダが違っても順番を決められないため、名前を変えてください）。";
                    return null;
                }

                using var stream = entry.Open();
                using var buffer = new MemoryStream();
                var chunk = new byte[81920];
                int read;
                while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    total += read;
                    if (buffer.Length + read > CsvImport.MaxUploadBytes)
                    {
                        error = $"'{fileName}' が大きすぎます（1ファイル {CsvImport.MaxUploadBytes / 1024 / 1024} MB まで）。";
                        return null;
                    }
                    if (total > MaxTotalUncompressedBytes)
                    {
                        error = $"ZIPを展開した合計が大きすぎます（{MaxTotalUncompressedBytes / 1024 / 1024} MB まで）。";
                        return null;
                    }
                    buffer.Write(chunk, 0, read);
                }
                entries.Add(new CsvBundleEntry(fileName, KindOf(fileName), CsvFile.Decode(buffer.ToArray())));
            }
        }
        catch (InvalidDataException)
        {
            error = "ZIPファイルとして読み込めませんでした。";
            return null;
        }

        if (entries.Count == 0)
        {
            error = "ZIPにCSVファイルが入っていません。";
            return null;
        }
        return [.. entries.OrderBy(e => e.FileName, StringComparer.Ordinal)];
    }

    /// <summary>ファイル名から種別を決める（最初の「_」より後ろ。「_」が無ければ拡張子を除いた全体）</summary>
    public static string KindOf(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var index = name.IndexOf('_');
        return index < 0 ? name : name[(index + 1)..];
    }

    /// <summary>
    /// 解決済みのファイルを1つのトランザクションで順に取り込む。
    /// importOne は1ファイル分を検証・保存して件数を返す（エラーは errors に積む）。
    /// エラーが出たら以降のファイルは処理せず、全体を取り消す。
    /// </summary>
    public static async Task<CsvBundleImportResult> ImportAsync<TKind>(
        MesAppDbContext db,
        IReadOnlyList<CsvBundleFile<TKind>> files,
        Func<TKind, CsvKindInfo> infoOf,
        bool dryRun,
        Func<TKind, CsvTable, List<CsvImportError>, CancellationToken, Task<CsvFileImportCount>> importOne,
        CancellationToken ct)
    {
        var results = new List<CsvBundleFileResult>();
        var failed = false;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var file in files)
        {
            var info = infoOf(file.Kind);
            var errors = new List<CsvImportError>();
            var table = CsvImport.Prepare(info, file.Text, errors);
            var count = new CsvFileImportCount(0, 0);
            if (table is not null)
            {
                try
                {
                    count = await importOne(file.Kind, table, errors, ct);
                }
                catch (DbUpdateException ex)
                {
                    errors.Add(new CsvImportError(0, $"DBへの反映に失敗しました：{ex.InnerException?.Message ?? ex.Message}"));
                }
            }
            results.Add(new CsvBundleFileResult(file.FileName,
                CsvImport.Result(info, table?.Rows.Count ?? 0, count.Created, count.Updated, dryRun, errors)));
            if (errors.Count > 0)
            {
                failed = true;
                break;
            }
        }

        if (failed || dryRun)
        {
            await transaction.RollbackAsync(ct);
        }
        else
        {
            await transaction.CommitAsync(ct);
        }
        return new CsvBundleImportResult(dryRun, !failed, results,
            [.. files.Skip(results.Count).Select(f => f.FileName)]);
    }

    /// <summary>番号付きのCSVをZIPにまとめる（Excelで開けるようUTF-8 BOM付き）</summary>
    public static byte[] ToZip(IEnumerable<(string FileName, string Csv)> files)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (fileName, csv) in files)
            {
                var entry = archive.CreateEntry(fileName, CompressionLevel.Optimal);
                using var stream = entry.Open();
                stream.Write(CsvFile.ToUtf8Bom(csv));
            }
        }
        return buffer.ToArray();
    }
}
