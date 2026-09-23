using MesApp.Api.Services;

namespace MesApp.Api.Tests;

/// <summary>
/// 管理図と工程能力指数の計算（Spec.md 3.3。C-50-10-01〜02）。
/// 係数表と群の揃え方を手計算の値で固定する。
/// </summary>
public class ControlChartCalculatorTests
{
    private static int _seq;

    private static ControlChartSubgroup Group(decimal? lsl, decimal? usl, params decimal[] values)
    {
        var n = ++_seq;
        var at = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero).AddHours(n);
        return new ControlChartSubgroup($"IN-{n:000}", at, DateOnly.FromDateTime(at.DateTime), values, lsl, usl);
    }

    [Fact]
    public void XバーR管理図の管理限界と工程能力を係数表どおりに求め大きさの違う群は除外する()
    {
        var chart = ControlChartCalculator.Calculate(1, "INS-01", "外径",
        [
            Group(5m, 15m, 10m, 12m),
            Group(5m, 15m, 11m, 13m),
            Group(5m, 15m, 9m, 11m),
            Group(5m, 15m, 10m, 10m),
            Group(5m, 15m, 10m, 10m, 10m), // 群の大きさ3は揃わないので除外
        ]);

        Assert.False(chart.IsIndividuals);
        Assert.Equal(2, chart.SubgroupSize);
        Assert.Equal(1, chart.ExcludedSubgroupCount);
        Assert.Equal(4, chart.Points.Count);
        // X̄ = 10.75、R̄ = 1.5、A2 = 1.880、D4 = 3.267、d2 = 1.128
        Assert.Equal(10.75m, chart.CenterLine);
        Assert.Equal(13.57m, chart.UpperControlLimit);
        Assert.Equal(7.93m, chart.LowerControlLimit);
        Assert.Equal(1.5m, chart.RangeCenterLine);
        Assert.Equal(4.9005m, chart.RangeUpperControlLimit);
        Assert.Equal(0m, chart.RangeLowerControlLimit);
        Assert.Equal(1.3298m, chart.Sigma);
        Assert.Equal(1.25m, chart.Cp);   // 10 / (6 × 1.3298)
        Assert.Equal(1.07m, chart.Cpk);  // (15 − 10.75) / (3 × 1.3298)
        Assert.False(chart.SpecLimitsChanged);
        Assert.DoesNotContain(chart.Points, p => p.MeanOutOfControl || p.RangeOutOfControl);
    }

    [Fact]
    public void 群の大きさ1なら個別値と移動範囲で管理限界を求め外れた点に印を付ける()
    {
        decimal[] values = [10m, 11m, 10m, 11m, 10m, 11m, 10m, 11m, 10m, 20m];
        var chart = ControlChartCalculator.Calculate(1, "INS-01", "外径",
            [.. values.Select(v => Group(null, 25m, v))]);

        Assert.True(chart.IsIndividuals);
        Assert.Equal(1, chart.SubgroupSize);
        Assert.Null(chart.Points[0].Range); // 先頭の点には移動範囲が無い
        // X̄ = 114 / 10 = 11.4、Rs̄ = 18 / 9 = 2、E2 = 2.660
        Assert.Equal(11.4m, chart.CenterLine);
        Assert.Equal(16.72m, chart.UpperControlLimit);
        Assert.True(chart.Points[^1].MeanOutOfControl);
        Assert.True(chart.Points[^1].RangeOutOfControl);
        Assert.Equal(9, chart.Points.Count(p => !p.MeanOutOfControl));
        // 規格が片側だけなら Cp は出さず、Cpk は片側で求める
        Assert.Null(chart.Cp);
        Assert.NotNull(chart.Cpk);
    }

    [Fact]
    public void 中心線の同じ側に9点続くと並びに印を付け8点なら付けない()
    {
        var nine = ControlChartCalculator.Calculate(1, "INS-01", "外径",
            [.. Enumerable.Repeat(11m, 9).Concat(Enumerable.Repeat(9m, 9)).Select(v => Group(null, null, v))]);
        Assert.All(nine.Points, p => Assert.True(p.InRun));

        var eight = ControlChartCalculator.Calculate(1, "INS-01", "外径",
            [.. Enumerable.Repeat(11m, 8).Concat(Enumerable.Repeat(9m, 8)).Select(v => Group(null, null, v))]);
        Assert.All(eight.Points, p => Assert.False(p.InRun));
    }

    [Fact]
    public void 群が1つでは管理限界を出さず規格の改訂を知らせる()
    {
        var single = ControlChartCalculator.Calculate(1, "INS-01", "外径", [Group(9m, 11m, 10m, 10.2m)]);
        Assert.Single(single.Points);
        Assert.Null(single.CenterLine);
        Assert.Null(single.Cpk);

        var revised = ControlChartCalculator.Calculate(1, "INS-01", "外径",
            [Group(9m, 11m, 10m, 10.2m), Group(9.5m, 10.5m, 10.1m, 10.3m)]);
        Assert.True(revised.SpecLimitsChanged);
        Assert.Equal(9.5m, revised.LowerSpecLimit); // 工程能力は最新の規格で求める
    }
}
