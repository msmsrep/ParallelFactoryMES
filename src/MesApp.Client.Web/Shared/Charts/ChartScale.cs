using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Components;

namespace MesApp.Client.Web.Shared.Charts;

/// <summary>
/// ダッシュボードのグラフ（Spec.md 3.8）で共通に使う目盛りの計算と SVG の書き出し。
/// JSのグラフライブラリは使わない（オフラインの工場LANでも配布物だけで動かすため）
/// </summary>
public static class ChartScale
{
    /// <summary>
    /// 0 から max を覆う、きりのいい（1・2・5 × 10のべき）間隔の目盛り。先頭は必ず 0、末尾は max 以上。
    /// max が 0 以下なら [0, 1]（何も無いときに高さ0で割らないように）
    /// </summary>
    /// <param name="max">データの最大値</param>
    /// <param name="target">目盛りの本数の目安（0 を除く）</param>
    /// <param name="integer">件数など小数の目盛りが意味を持たない値なら true（間隔を1以上にする）</param>
    public static IReadOnlyList<decimal> Ticks(decimal max, int target = 5, bool integer = false)
    {
        if (max <= 0)
        {
            return [0m, 1m];
        }
        var step = NiceStep(max / Math.Max(target, 1));
        if (integer)
        {
            step = Math.Max(step, 1m);
        }
        var top = Math.Ceiling(max / step) * step;
        var ticks = new List<decimal>();
        for (var value = 0m; value <= top; value += step)
        {
            ticks.Add(value);
        }
        return ticks;
    }

    /// <summary>raw 以上で最小の「1・2・5 × 10のべき」</summary>
    public static decimal NiceStep(decimal raw)
    {
        if (raw <= 0)
        {
            return 1m;
        }
        var exponent = (int)Math.Floor(Math.Log10((double)raw));
        var power = Pow10(exponent);
        var fraction = raw / power;
        var nice = fraction <= 1m ? 1m : fraction <= 2m ? 2m : fraction <= 5m ? 5m : 10m;
        return nice * power;
    }

    /// <summary>
    /// 値の水準（"danger" / "warning" / null）。割合バーとドーナツで同じしきい値を使う
    /// （別々に書くと、同じ値が画面によって違う色になる）
    /// </summary>
    /// <param name="highIsBad">値が高いほど危険な指標か（寿命消化率など）。false なら高いほど良い指標（稼働率）</param>
    public static string? Level(decimal value, bool highIsBad) => highIsBad switch
    {
        true when value >= 100 => "danger",
        true when value >= 80 => "warning",
        false when value < 50 => "warning",
        _ => null,
    };

    /// <summary>SVG の座標・寸法の書き出し（カルチャに依らず小数点はピリオド）</summary>
    public static string F(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>目盛りの数値の表示（桁区切りあり。小数は2桁まで）</summary>
    public static string Number(decimal value) => value.ToString("#,0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// SVG の text 要素。Razor は &lt;text&gt; を特別扱いするため文字列で組み立てる。
    /// 中身（品目名など）はエンコードする
    /// </summary>
    public static MarkupString Text(decimal x, decimal y, string content, string anchor = "middle",
        string style = "font-size:10px; fill:var(--text-muted);") =>
        new($"<text x=\"{F(x)}\" y=\"{F(y)}\" text-anchor=\"{anchor}\" style=\"{style}\">{WebUtility.HtmlEncode(content)}</text>");

    private static decimal Pow10(int exponent)
    {
        var result = 1m;
        for (var i = 0; i < Math.Abs(exponent); i++)
        {
            result *= 10m;
        }
        return exponent >= 0 ? result : 1m / result;
    }
}

/// <summary>グラフの系列（名前・色・区切りごとの値）。値が null の区切りは描かない（0 とは区別する）</summary>
/// <param name="Color">CSS の色（既存のテーマ変数 <c>var(--primary)</c> 等を使う）</param>
public record ChartSeries(string Name, string Color, IReadOnlyList<decimal?> Values);

/// <summary>横棒グラフの1行（ラベル・積み上げる値・右端の注記）</summary>
public record HBarItem(string Label, IReadOnlyList<decimal> Values, string? Note = null);

/// <summary>凡例の1項目</summary>
public record ChartLegendItem(string Name, string Color);
