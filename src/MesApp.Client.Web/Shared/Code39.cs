using System.Text;

namespace MesApp.Client.Web.Shared;

/// <summary>
/// Code39バーコードのSVG生成（現品票・ラベル発行用。Spec.md 3.8 帳票・ラベル発行基盤）。
/// USB HIDリーダーで広くサポートされる方式で、英大文字・数字・ハイフン等を扱える。
/// 対応外の文字は '-' に置換する。
/// </summary>
public static class Code39
{
    // 各文字の9エレメント（バー・スペース交互、'w'=太、'n'=細）
    private static readonly Dictionary<char, string> Patterns = new()
    {
        ['0'] = "nnnwwnwnn", ['1'] = "wnnwnnnnw", ['2'] = "nnwwnnnnw", ['3'] = "wnwwnnnnn",
        ['4'] = "nnnwwnnnw", ['5'] = "wnnwwnnnn", ['6'] = "nnwwwnnnn", ['7'] = "nnnwnnwnw",
        ['8'] = "wnnwnnwnn", ['9'] = "nnwwnnwnn",
        ['A'] = "wnnnnwnnw", ['B'] = "nnwnnwnnw", ['C'] = "wnwnnwnnn", ['D'] = "nnnnwwnnw",
        ['E'] = "wnnnwwnnn", ['F'] = "nnwnwwnnn", ['G'] = "nnnnnwwnw", ['H'] = "wnnnnwwnn",
        ['I'] = "nnwnnwwnn", ['J'] = "nnnnwwwnn", ['K'] = "wnnnnnnww", ['L'] = "nnwnnnnww",
        ['M'] = "wnwnnnnwn", ['N'] = "nnnnwnnww", ['O'] = "wnnnwnnwn", ['P'] = "nnwnwnnwn",
        ['Q'] = "nnnnnnwww", ['R'] = "wnnnnnwwn", ['S'] = "nnwnnnwwn", ['T'] = "nnnnwnwwn",
        ['U'] = "wwnnnnnnw", ['V'] = "nwwnnnnnw", ['W'] = "wwwnnnnnn", ['X'] = "nwnnwnnnw",
        ['Y'] = "wwnnwnnnn", ['Z'] = "nwwnwnnnn",
        ['-'] = "nwnnnnwnw", ['.'] = "wwnnnnwnn", [' '] = "nwwnnnwnn", ['$'] = "nwnwnwnnn",
        ['/'] = "nwnwnnnwn", ['+'] = "nwnnnwnwn", ['%'] = "nnnwnwnwn", ['*'] = "nwnnwnwnn",
    };

    private const int Narrow = 2;
    private const int Wide = 6;

    /// <summary>バーコードのSVGマークアップを生成する（スタート/ストップの '*' は自動付与）</summary>
    public static string ToSvg(string text, int height = 48)
    {
        var normalized = Normalize(text);
        var content = $"*{normalized}*";

        var bars = new StringBuilder();
        var x = 0;
        foreach (var ch in content)
        {
            var pattern = Patterns[ch];
            for (var i = 0; i < pattern.Length; i++)
            {
                var width = pattern[i] == 'w' ? Wide : Narrow;
                // 偶数インデックスがバー（黒）、奇数がスペース
                if (i % 2 == 0)
                {
                    bars.Append($"<rect x=\"{x}\" y=\"0\" width=\"{width}\" height=\"{height}\" fill=\"#000\"/>");
                }
                x += width;
            }
            x += Narrow; // 文字間ギャップ
        }

        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{x}\" height=\"{height}\" " +
               $"viewBox=\"0 0 {x} {height}\" role=\"img\" aria-label=\"{normalized}\">{bars}</svg>";
    }

    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var raw in text.ToUpperInvariant())
        {
            builder.Append(raw != '*' && Patterns.ContainsKey(raw) ? raw : '-');
        }
        return builder.ToString();
    }
}
