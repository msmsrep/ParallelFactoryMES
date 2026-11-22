namespace MesApp.Core.Contracts.Integration;

/// <summary>
/// フォルダ連携（CSVの自動取込）の設定と最近の結果（Spec.md 3.8 フォルダ連携）。
/// 設定は appsettings の <c>FolderLink</c> 節で、画面からは変えない（変更には再起動が要る）
/// </summary>
/// <param name="Enabled">連携フォルダが設定されていて、走査している</param>
/// <param name="Root">連携フォルダ（絶対パス）。未設定なら null</param>
/// <param name="UserName">取込を行う利用者のユーザー名</param>
/// <param name="PollSeconds">走査の間隔（秒）</param>
/// <param name="ConfigError">設定の誤り（利用者が無い・無効、フォルダを作れない等）。走査は続けるが取り込まない</param>
/// <param name="LastScanAt">最後に走査した日時</param>
/// <param name="Recent">最近処理したファイル（新しい順。再起動すると消える。残るのは processed・error フォルダの結果ファイル）</param>
public record FolderLinkStatusResponse(
    bool Enabled,
    string? Root,
    string? UserName,
    int PollSeconds,
    string? ConfigError,
    DateTimeOffset? LastScanAt,
    List<FolderLinkFileResult> Recent);

/// <summary>フォルダ連携で処理した1ファイルの結果</summary>
/// <param name="FileName">inbox からの相対パス</param>
/// <param name="ProcessedAt">処理した日時</param>
/// <param name="Succeeded">取り込めた（false なら何も反映していない）</param>
/// <param name="Kind">CSVの種別（ZIPは zip）</param>
/// <param name="Rows">データ行数（ZIPは全ファイルの合計）</param>
/// <param name="Created">登録件数</param>
/// <param name="Updated">更新件数</param>
/// <param name="MovedTo">移動先（連携フォルダからの相対パス）</param>
/// <param name="Messages">エラーと警告（先頭から最大20件）</param>
public record FolderLinkFileResult(
    string FileName,
    DateTimeOffset ProcessedAt,
    bool Succeeded,
    string Kind,
    int Rows,
    int Created,
    int Updated,
    string MovedTo,
    List<string> Messages);
