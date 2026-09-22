namespace MesApp.Core.Contracts.Masters;

/// <summary>CSV取込の行単位エラー（Lineは元CSVの物理行番号。ヘッダー行が1行目）</summary>
public record CsvImportError(int Line, string Message);

/// <summary>
/// CSV取込の結果。エラーが1件でもある場合は全件ロールバックし、Succeeded=false を返す。
/// </summary>
public record CsvImportResult(
    string Kind,
    int DataRows,
    int Created,
    int Updated,
    bool DryRun,
    bool Succeeded,
    List<CsvImportError> Errors)
{
    /// <summary>
    /// 取り込めたが伝えたいこと（<b>エラーではないのでロールバックしない</b>。<c>Succeeded</c> に影響しない）。
    /// <para>
    /// 単票APIが応答に載せている警告を、CSVでも同じ条件で返すために置く。現在の用途は
    /// 直（<c>Shift</c>）が製造日の境界をまたぐ場合（Spec.md 5.7）。位置引数を増やすと
    /// ZIP一括取込を含む既存の組み立てが全て壊れるため、既定値を持つプロパティとして足す。
    /// </para>
    /// </summary>
    public List<CsvImportError> Warnings { get; init; } = [];
}

/// <summary>CSV入出力に対応するマスタ種別の情報（列定義は画面のガイド表示にも使う）</summary>
/// <param name="UserAdminOnly">出力も取込もユーザー管理権限が要る種別（個人に紐づくため参照から絞る）</param>
public record CsvKindInfo(
    string Kind,
    string Label,
    bool UserAdminOnly,
    List<CsvColumnInfo> Columns)
{
    /// <summary>
    /// 取込だけユーザー管理権限が要る種別（参照は全員、更新は管理者）。
    /// 単票のAPIが<c>MesRoleGroups.UserAdmin</c>で絞っている種別は、CSVでも同じ権限にする
    /// （片方だけ緩いと、フォームでは登録できないマスタがCSVからは書き換えられる）。
    /// </summary>
    public bool UserAdminWrite { get; init; }

    /// <summary>
    /// 取込に必要なロールグループ（実績CSVと、マスタ更新権限以外で絞るマスタCSV（生産計画）。
    /// 種別ごとに単票APIと同じ定数を返す）。
    /// 画面は取込欄の表示制御にこの値を使い、APIと画面で別々にロールを書かない
    /// </summary>
    public string? WriteRoles { get; init; }
}

/// <summary>CSVの列定義</summary>
public record CsvColumnInfo(string Name, string Label, bool Required, string? Note);
