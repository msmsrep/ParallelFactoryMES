using MesApp.Client.Web.Shared.Charts;

namespace MesApp.Client.Web.Tests;

/// <summary>
/// グラフの目盛り計算（<see cref="ChartScale"/>）。ダッシュボードのすべてのグラフが使い、
/// 画面の目視では端の値（0・小数・ちょうど割り切れる値）のずれに気づきにくいため単体で確認する。
/// </summary>
public class ChartScaleTests
{
    [Theory]
    [InlineData(0.064, 0.1)]
    [InlineData(0.64, 1)]
    [InlineData(1, 1)]
    [InlineData(1.2, 2)]
    [InlineData(3, 5)]
    [InlineData(17, 20)]
    [InlineData(20, 20)]
    [InlineData(51, 100)]
    [InlineData(240, 500)]
    public void 間隔は1_2_5系列で元の値以上の最小値になる(double raw, double expected)
    {
        Assert.Equal((decimal)expected, ChartScale.NiceStep((decimal)raw));
    }

    [Fact]
    public void 目盛りは0から始まり最大値を覆う()
    {
        Assert.Equal([0m, 20m, 40m, 60m, 80m, 100m], ChartScale.Ticks(85m));
        Assert.Equal([0m, 1m, 2m, 3m, 4m], ChartScale.Ticks(3.2m));
    }

    [Fact]
    public void 最大値がちょうど目盛りに乗るときは余分な目盛りを足さない()
    {
        Assert.Equal([0m, 20m, 40m, 60m, 80m, 100m], ChartScale.Ticks(100m));
    }

    [Fact]
    public void 値が無いときは0除算にならない目盛りを返す()
    {
        Assert.Equal([0m, 1m], ChartScale.Ticks(0m));
        Assert.Equal([0m, 1m], ChartScale.Ticks(-5m));
    }

    [Fact]
    public void 件数の目盛りは小数にしない()
    {
        Assert.Equal([0m, 1m, 2m], ChartScale.Ticks(2m, integer: true));
        Assert.Equal([0m, 0.5m, 1m, 1.5m, 2m], ChartScale.Ticks(2m));
    }

    [Theory]
    [InlineData(100, true, "danger")]
    [InlineData(80, true, "warning")]
    [InlineData(79.9, true, null)]
    [InlineData(49.9, false, "warning")]
    [InlineData(50, false, null)]
    public void 水準のしきい値(double value, bool highIsBad, string? expected)
    {
        Assert.Equal(expected, ChartScale.Level((decimal)value, highIsBad));
    }

    [Fact]
    public void 座標はカルチャに依らずピリオドで書く()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("12.5", ChartScale.F(12.5m));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    public void 文字の中身はエンコードする()
    {
        Assert.Contains("A&amp;B&lt;1&gt;", ChartScale.Text(0, 0, "A&B<1>").Value, StringComparison.Ordinal);
    }
}
