using MesApp.Core.Contracts.Common;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api;

/// <summary>
/// 選択肢クエリを <see cref="OptionsResult{T}"/> に変換する。
/// 上限を超えているかを知るために1件多く取得する（総件数のCOUNTは選択肢には不要なので撮らない）。
/// </summary>
public static class QueryableOptionsExtensions
{
    public static async Task<OptionsResult<T>> ToOptionsResultAsync<T>(
        this IQueryable<T> query, OptionQuery options, CancellationToken ct)
    {
        var limit = options.NormalizedLimit;
        var items = await query.Take(limit + 1).ToListAsync(ct);
        return items.Count > limit
            ? new OptionsResult<T>(items.Take(limit).ToList(), true)
            : new OptionsResult<T>(items, false);
    }
}
