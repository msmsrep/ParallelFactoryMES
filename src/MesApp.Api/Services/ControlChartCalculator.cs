using MesApp.Core.Contracts.Quality;

namespace MesApp.Api.Services;

/// <summary>管理図の1群の入力（検査指示ごとの測定値と、その指示が発行時に写した規格値）</summary>
public sealed record ControlChartSubgroup(
    string OrderNo, DateTimeOffset InspectedAt, DateOnly BusinessDate,
    IReadOnlyList<decimal> Values, decimal? LowerSpecLimit, decimal? UpperSpecLimit);

/// <summary>
/// 管理図と工程能力指数の計算（Spec.md 3.3。C-50-10-01〜02。JIS Z 9020-2 のシューハート管理図）。
/// <para>
/// 群の大きさが1なら個別値－移動範囲管理図（X-Rs）、2〜10なら X̄-R 管理図にする。
/// 群の大きさは最も多い大きさに揃え、違う大きさの群は除外して件数を返す
/// （大きさが違う群を同じ係数で混ぜると管理限界が意味を失う。黙って捨てずに除外数を見せる）。
/// 群の大きさが11以上の X̄-s 管理図は扱わず、点だけを返す。
/// </para>
/// <para>
/// 標準偏差は群内のばらつき（R̄/d2）から推定する。全測定値の標準偏差を使うと群間の変動（工程の異常）まで
/// ばらつきに含まれ、管理状態でない工程の Cpk が実際より良く見えるため。
/// </para>
/// </summary>
public static class ControlChartCalculator
{
    /// <summary>中心線の同じ側に続くと異常とみなす点の数（JIS Z 9020-2 のルール2）</summary>
    public const int RunLength = 9;

    /// <summary>X̄-R 管理図の係数（群の大きさ n → A2・D3・D4・d2）。n=2 の値は移動範囲（X-Rs）にも使う</summary>
    private static readonly Dictionary<int, (decimal A2, decimal D3, decimal D4, decimal D2)> Factors = new()
    {
        [2] = (1.880m, 0m, 3.267m, 1.128m),
        [3] = (1.023m, 0m, 2.574m, 1.693m),
        [4] = (0.729m, 0m, 2.282m, 2.059m),
        [5] = (0.577m, 0m, 2.114m, 2.326m),
        [6] = (0.483m, 0m, 2.004m, 2.534m),
        [7] = (0.419m, 0.076m, 1.924m, 2.704m),
        [8] = (0.373m, 0.136m, 1.864m, 2.847m),
        [9] = (0.337m, 0.184m, 1.816m, 2.970m),
        [10] = (0.308m, 0.223m, 1.777m, 3.078m),
    };

    /// <summary>個別値管理図の係数 E2（= 3 / d2(n=2)）</summary>
    private const decimal E2 = 2.660m;

    public static ControlChartResponse Calculate(
        int itemId, string itemCode, string itemName, IReadOnlyList<ControlChartSubgroup> subgroups)
    {
        var ordered = subgroups.Where(s => s.Values.Count > 0).OrderBy(s => s.InspectedAt).ToList();
        var size = ordered.Count == 0 ? 1
            : ordered.GroupBy(s => s.Values.Count)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
        var used = ordered.Where(s => s.Values.Count == size).ToList();
        var excluded = ordered.Count - used.Count;
        var individuals = size == 1;

        var means = used.Select(s => s.Values.Average()).ToList();
        var ranges = individuals
            ? means.Select((m, i) => i == 0 ? (decimal?)null : Math.Abs(m - means[i - 1])).ToList()
            : used.Select(s => (decimal?)(s.Values.Max() - s.Values.Min())).ToList();

        var latest = used.Count > 0 ? used[^1] : null;
        var lsl = latest?.LowerSpecLimit;
        var usl = latest?.UpperSpecLimit;
        var specChanged = used.Any(s => s.LowerSpecLimit != lsl || s.UpperSpecLimit != usl);

        decimal? cl = null, ucl = null, lcl = null, rcl = null, rucl = null, rlcl = null, sigma = null, cp = null, cpk = null;
        var factorKey = individuals ? 2 : size;
        if (used.Count >= 2 && Factors.TryGetValue(factorKey, out var f))
        {
            var xbar = means.Average();
            var rbar = ranges.Where(r => r is not null).Average(r => r!.Value);
            var width = (individuals ? E2 : f.A2) * rbar;
            cl = Round(xbar);
            ucl = Round(xbar + width);
            lcl = Round(xbar - width);
            rcl = Round(rbar);
            rucl = Round(f.D4 * rbar);
            rlcl = Round(f.D3 * rbar);
            if (rbar > 0)
            {
                var s = rbar / f.D2;
                sigma = Round(s);
                if (lsl is { } lo && usl is { } hi)
                {
                    cp = Math.Round((hi - lo) / (6 * s), 2);
                }
                var sides = new List<decimal>();
                if (usl is { } upper)
                {
                    sides.Add((upper - xbar) / (3 * s));
                }
                if (lsl is { } lower)
                {
                    sides.Add((xbar - lower) / (3 * s));
                }
                cpk = sides.Count == 0 ? null : Math.Round(sides.Min(), 2);
            }
        }

        var inRun = RunFlags(means, cl);
        var points = used.Select((s, i) => new ControlChartPoint(
                s.OrderNo, s.InspectedAt, s.BusinessDate, s.Values.Count,
                Round(means[i]), ranges[i] is { } r ? Round(r) : null,
                MeanOutOfControl: ucl is not null && (means[i] > ucl || means[i] < lcl),
                RangeOutOfControl: rucl is not null && ranges[i] is { } range && (range > rucl || range < rlcl),
                InRun: inRun[i]))
            .ToList();

        return new ControlChartResponse(itemId, itemCode, itemName, individuals, size,
            cl, ucl, lcl, rcl, rucl, rlcl, lsl, usl, specChanged, sigma, cp, cpk, excluded, points);
    }

    /// <summary>中心線の同じ側に <see cref="RunLength"/> 点以上続く並びに属する点に印を付ける（中心線上の点は並びを切る）</summary>
    private static bool[] RunFlags(IReadOnlyList<decimal> means, decimal? center)
    {
        var flags = new bool[means.Count];
        if (center is not { } c)
        {
            return flags;
        }
        var start = 0;
        for (var i = 0; i <= means.Count; i++)
        {
            var continues = i < means.Count && i > start
                && Math.Sign(means[i] - c) == Math.Sign(means[start] - c) && means[i] != c;
            if (continues)
            {
                continue;
            }
            if (i - start >= RunLength && means[start] != c)
            {
                for (var k = start; k < i; k++)
                {
                    flags[k] = true;
                }
            }
            start = i;
        }
        return flags;
    }

    private static decimal Round(decimal value) => Math.Round(value, 4);
}
