using Bunit;
using MesApp.Client.Web.Shared;
using MesApp.Core.Contracts.Common;

namespace MesApp.Client.Web.Tests;

/// <summary>
/// ページ送り（<see cref="Pager{TItem}"/>）。一覧のある画面すべてが使うため横断的に確認する。
/// </summary>
public class PagerTests : BunitContext
{
    private static PagedResult<string> Page(int page, int total, int pageSize = 2) =>
        new([.. Enumerable.Range(0, Math.Min(pageSize, Math.Max(0, total - ((page - 1) * pageSize))))
                .Select(i => $"item{i}")],
            total, page, pageSize);

    [Fact]
    public void 件数と表示範囲を出す()
    {
        var component = Render<Pager<string>>(p => p
            .Add(x => x.Result, Page(2, 5))
            .Add(x => x.OnPageChanged, _ => { }));

        Assert.Contains("全 5 件中 3〜4 件", component.Markup, StringComparison.Ordinal);
        Assert.Contains("2 / 3 ページ", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void 先頭ページでは前へが押せず最終ページでは次へが押せない()
    {
        var first = Render<Pager<string>>(p => p
            .Add(x => x.Result, Page(1, 5))
            .Add(x => x.OnPageChanged, _ => { }));
        Assert.True(first.Find("button:first-of-type").HasAttribute("disabled"));

        var last = Render<Pager<string>>(p => p
            .Add(x => x.Result, Page(3, 5))
            .Add(x => x.OnPageChanged, _ => { }));
        Assert.True(last.Find("button:last-of-type").HasAttribute("disabled"));
    }

    [Fact]
    public void 次へで次のページ番号を通知する()
    {
        var requested = 0;
        var component = Render<Pager<string>>(p => p
            .Add(x => x.Result, Page(2, 10))
            .Add(x => x.OnPageChanged, page => requested = page));

        component.Find("button:last-of-type").Click();

        Assert.Equal(3, requested);
    }

    [Fact]
    public void 件数が0なら何も出さない()
    {
        var component = Render<Pager<string>>(p => p
            .Add(x => x.Result, Page(1, 0))
            .Add(x => x.OnPageChanged, _ => { }));

        Assert.Empty(component.Markup.Trim());
    }
}
