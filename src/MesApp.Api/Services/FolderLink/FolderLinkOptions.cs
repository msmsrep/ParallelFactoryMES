namespace MesApp.Api.Services.FolderLink;

/// <summary>
/// フォルダ連携の設定（appsettings の <c>FolderLink</c> 節。Docker では <c>FolderLink__Root</c> 等の環境変数。Spec.md 3.8）。
/// 起動時に読む＝変更には再起動が要る（製造日境界と同じ扱い）
/// </summary>
public sealed class FolderLinkOptions
{
    public const string SectionName = "FolderLink";

    /// <summary>連携フォルダ。空なら連携しない。相対パスはアプリの置き場所（コンテンツルート）から数える</summary>
    public string? Root { get; set; }

    /// <summary>取込を行う利用者のユーザー名。権限の判定・記録者・監査ログはこの利用者で行う</summary>
    public string? UserName { get; set; }

    /// <summary>走査の間隔（秒。5未満は5にする）</summary>
    public int PollSeconds { get; set; } = 30;

    /// <summary>
    /// 最後の書き込みからこの秒数が経つまで取り込まない（書き出し途中のファイルを読まないため）。
    /// 書き込み中で開けないファイルは、この設定に関係なく次の走査に回す
    /// </summary>
    public int SettleSeconds { get; set; } = 5;

    /// <summary>結果ファイルとエラーの言語（ja / en。空・不明は既定の日本語）。画面の表示言語とは別に決める（走査は誰の要求でもないため）</summary>
    public string? Language { get; set; }
}
