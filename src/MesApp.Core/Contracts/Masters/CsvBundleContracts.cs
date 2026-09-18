namespace MesApp.Core.Contracts.Masters;

/// <summary>一括取込のファイルごとの結果（FileName はZIP内のファイル名）</summary>
public record CsvBundleFileResult(string FileName, CsvImportResult Result);

/// <summary>
/// CSVの一括取込（ZIP）の結果。全ファイルを1つのトランザクションで処理し、
/// どれか1ファイルでもエラーがあれば全ファイルを取り消す（Succeeded=false）。
/// エラーが出たファイルより後のファイルは処理せず、NotProcessed に名前を返す。
/// </summary>
public record CsvBundleImportResult(
    bool DryRun,
    bool Succeeded,
    List<CsvBundleFileResult> Files,
    List<string> NotProcessed);
