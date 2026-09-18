namespace MesApp.Core.Contracts.Common;

/// <summary>
/// 一覧APIのページング要求（Spec.md 7.5）。クエリ文字列 <c>?page=2&amp;pageSize=50</c> で受け取る。
/// </summary>
/// <remarks>
/// <para>
/// 対象は運用とともに増え続ける一覧（実績・在庫トランザクション・各種指示）に限る。
/// 品目・工程・ロケーション等のマスタは件数が有界で、画面の選択肢に全件が要るためページングしない。
/// </para>
/// <para>
/// <b>アクションの引数名を <c>page</c> にしないこと</b>（<c>paging</c> を使う）。
/// 引数名がモデルバインドのプレフィックスになるため、クエリの <c>?page=2</c> と衝突して
/// <c>pageSize</c> が既定値のままになる。
/// </para>
/// </remarks>
public record PageQuery
{
    /// <summary>既定のページサイズ</summary>
    public const int DefaultPageSize = 50;

    /// <summary>1回に返す上限（クライアントの指定値はここで頭打ちにする）</summary>
    public const int MaxPageSize = 200;

    /// <summary>1始まりのページ番号</summary>
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>範囲外の指定を丸めたページ番号</summary>
    public int NormalizedPage => Page < 1 ? 1 : Page;

    /// <summary>範囲外の指定を丸めたページサイズ</summary>
    public int NormalizedPageSize => Math.Clamp(PageSize, 1, MaxPageSize);

    /// <summary>読み飛ばす件数</summary>
    public int Skip => (NormalizedPage - 1) * NormalizedPageSize;
}

/// <summary>一覧APIのページング応答</summary>
/// <param name="Items">このページの内容</param>
/// <param name="Total">絞り込み後の総件数（「全N件」の表示と次ページ有無の判定に使う）</param>
public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public bool HasNext => (long)Page * PageSize < Total;

    public bool HasPrevious => Page > 1;

    /// <summary>総ページ数（0件のときは1）</summary>
    public int PageCount => Total <= 0 ? 1 : (Total + PageSize - 1) / PageSize;

    /// <summary>
    /// 件数の情報を保ったまま中身だけ変換する
    /// （エンティティで取得してからDTOへ組み立てる一覧で使う）
    /// </summary>
    public PagedResult<TResult> Map<TResult>(Func<T, TResult> selector) =>
        new([.. Items.Select(selector)], Total, Page, PageSize);
}
