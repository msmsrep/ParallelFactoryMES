namespace MesApp.Core.Contracts.Common;

/// <summary>
/// 選択肢（ドロップダウン）用の絞り込み要求。クエリ文字列 <c>?q=RM-01&amp;limit=50</c> で受け取る。
/// </summary>
/// <remarks>
/// 一覧（<see cref="PageQuery"/>）と分けているのは、用途が違うため。
/// 一覧はページを送って全件を見せるもの、選択肢は「目的の1件を選ばせる」ものであり、
/// 件数が増えたときに要るのはページ送りではなく検索。ロット番号や指図番号を
/// スキャンしてそのまま <c>q</c> に渡せるよう、コードの部分一致で絞り込む。
/// </remarks>
public record OptionQuery
{
    /// <summary>既定の取得件数</summary>
    public const int DefaultLimit = 50;

    /// <summary>1回に返す上限</summary>
    public const int MaxLimit = 200;

    /// <summary>コード・番号・名称の部分一致（未指定なら絞り込まない）</summary>
    public string? Q { get; init; }

    public int Limit { get; init; } = DefaultLimit;

    /// <summary>範囲外の指定を丸めた取得件数</summary>
    public int NormalizedLimit => Math.Clamp(Limit, 1, MaxLimit);

    /// <summary>検索語（空白のみはなしとして扱う）</summary>
    public string? Keyword => string.IsNullOrWhiteSpace(Q) ? null : Q.Trim();
}

/// <summary>
/// 選択肢の応答。
/// <paramref name="Truncated"/> が true なら該当が上限を超えている
/// ＝画面は「絞り込んでください」と伝える必要がある（黙って切り捨てない）。
/// </summary>
public record OptionsResult<T>(IReadOnlyList<T> Items, bool Truncated);
