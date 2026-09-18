using MesApp.Core.Contracts.Common;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api;

/// <summary>
/// 一覧クエリをページング応答へ変換する。並べ替えは呼び出し側で確定させてから渡すこと
/// （順序が決まっていないとページ間で内容が重複・欠落する）。
/// </summary>
public static class QueryablePagingExtensions
{
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query, PageQuery page, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query.Skip(page.Skip).Take(page.NormalizedPageSize).ToListAsync(ct);
        return new PagedResult<T>(items, total, page.NormalizedPage, page.NormalizedPageSize);
    }
}
